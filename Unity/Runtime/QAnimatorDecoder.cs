using System;
using System.IO;
using System.IO.Compression;

namespace QAnimator.Unity
{
    internal sealed class QAnimatorDecoder
    {
        private readonly byte[] _data;
        private readonly QAnimatorHeader _header;
        private readonly QFrameIndexEntry[] _frames;
        private readonly byte[] _rgba;
        private byte[] _payloadBuffer;
        private int _decodedFrame = -1;

        public int Width => _header.Width;
        public int Height => _header.Height;
        public int FrameCount => _header.FrameCount;
        public float Fps => _header.Fps;
        public float Duration => (float)_header.Duration;
        public bool HasAlpha => _header.HasAlpha;
        public byte[] RgbaBuffer => _rgba;
        public int DecodedFrame => _decodedFrame;

        public QAnimatorDecoder(byte[] data)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            if (data.Length < QAnimatorHeader.SerializedSize)
                throw new InvalidDataException("QAnimator data is smaller than the v1 header.");

            using var stream = new MemoryStream(data, writable: false);
            using var reader = new BinaryReader(stream);

            _header = new QAnimatorHeader(reader);
            int frameBytes = checked(_header.Width * _header.Height * 4);
            int blocksX = (_header.Width + _header.BlockSize - 1) / _header.BlockSize;
            int blocksY = (_header.Height + _header.BlockSize - 1) / _header.BlockSize;
            long maxDeltaPayloadLong = 4L + frameBytes + 8L * blocksX * blocksY;
            if (maxDeltaPayloadLong > int.MaxValue)
                throw new InvalidDataException("QAnimator delta payload limit exceeds supported memory range.");
            int maxDeltaPayload = (int)maxDeltaPayloadLong;

            ValidateTopLevelOffsets(data.LongLength);
            _frames = new QFrameIndexEntry[_header.FrameCount];

            stream.Position = _header.FrameIndexOffset;
            int maxPayload = 0;
            double previousTimestamp = -1;
            for (int i = 0; i < _frames.Length; i++)
            {
                QFrameIndexEntry frame = new QFrameIndexEntry(reader);
                ValidateFrameEntry(i, frame, frameBytes, maxDeltaPayload, previousTimestamp);
                _frames[i] = frame;
                previousTimestamp = frame.Timestamp;
                if (frame.UncompressedLength > maxPayload)
                    maxPayload = frame.UncompressedLength;
            }

            ValidateFramePayloadBounds(data.LongLength);
            _rgba = new byte[frameBytes];
            _payloadBuffer = new byte[Math.Max(4, maxPayload)];
        }

        public void DecodeFrame(int targetFrame)
        {
            if ((uint)targetFrame >= (uint)_frames.Length)
                throw new ArgumentOutOfRangeException(nameof(targetFrame));
            if (targetFrame == _decodedFrame)
                return;

            int key = _frames[targetFrame].NearestKeyFrame;
            if ((uint)key >= (uint)_frames.Length || key > targetFrame || _frames[key].Type != QFrameType.Key)
                throw new InvalidDataException("Invalid nearest key-frame index.");

            // For normal playback, applying the next delta(s) is cheapest. If Unity skips
            // far ahead and there is a newer key frame between the decoded frame and target,
            // jump to it instead of reconstructing every skipped delta frame.
            if (_decodedFrame >= 0 && targetFrame > _decodedFrame && key <= _decodedFrame)
            {
                for (int i = _decodedFrame + 1; i <= targetFrame; i++)
                    DecodeSingle(i);
                return;
            }

            DecodeSingle(key);
            for (int i = key + 1; i <= targetFrame; i++)
                DecodeSingle(i);
        }

        private void DecodeSingle(int frameIndex)
        {
            QFrameIndexEntry entry = _frames[frameIndex];
            EnsurePayloadCapacity(entry.UncompressedLength);

            long absoluteOffsetLong = checked(_header.FrameDataOffset + entry.DataOffset);
            int absoluteOffset = checked((int)absoluteOffsetLong);
            using var source = new MemoryStream(_data, absoluteOffset, entry.CompressedLength, writable: false, publiclyVisible: true);
            using var deflate = new DeflateStream(source, CompressionMode.Decompress, leaveOpen: false);

            int total = 0;
            while (total < entry.UncompressedLength)
            {
                int read = deflate.Read(_payloadBuffer, total, entry.UncompressedLength - total);
                if (read <= 0)
                    break;
                total += read;
            }
            if (total != entry.UncompressedLength)
                throw new InvalidDataException($"Frame {frameIndex} decompressed to {total} bytes; expected {entry.UncompressedLength}.");

            if (deflate.ReadByte() != -1)
                throw new InvalidDataException($"Frame {frameIndex} expands beyond its declared payload size.");

            if (entry.Type == QFrameType.Key)
            {
                Buffer.BlockCopy(_payloadBuffer, 0, _rgba, 0, _rgba.Length);
            }
            else
            {
                ApplyDelta(_payloadBuffer, entry.UncompressedLength);
            }

            _decodedFrame = frameIndex;
        }

        private void ApplyDelta(byte[] payload, int length)
        {
            int p = 0;
            int changedBlocks = ReadInt32(payload, ref p, length);
            if (changedBlocks < 0)
                throw new InvalidDataException("Delta payload has a negative block count.");

            int maxBlocksX = (_header.Width + _header.BlockSize - 1) / _header.BlockSize;
            int maxBlocksY = (_header.Height + _header.BlockSize - 1) / _header.BlockSize;
            int maxBlocks = checked(maxBlocksX * maxBlocksY);
            if (changedBlocks > maxBlocks)
                throw new InvalidDataException("Delta payload declares too many changed blocks.");

            for (int i = 0; i < changedBlocks; i++)
            {
                int x = ReadUInt16(payload, ref p, length);
                int y = ReadUInt16(payload, ref p, length);
                int bw = ReadUInt16(payload, ref p, length);
                int bh = ReadUInt16(payload, ref p, length);

                if (bw <= 0 || bh <= 0 || x + bw > _header.Width || y + bh > _header.Height)
                    throw new InvalidDataException("Delta block is outside frame bounds.");
                if (bw > _header.BlockSize || bh > _header.BlockSize)
                    throw new InvalidDataException("Delta block is larger than the declared block size.");
                if (x % _header.BlockSize != 0 || y % _header.BlockSize != 0)
                    throw new InvalidDataException("Delta block is not aligned to the declared block grid.");

                int rowBytes = checked(bw * 4);
                for (int row = 0; row < bh; row++)
                {
                    if (p + rowBytes > length)
                        throw new InvalidDataException("Delta payload is truncated.");
                    int dst = checked(((y + row) * _header.Width + x) * 4);
                    Buffer.BlockCopy(payload, p, _rgba, dst, rowBytes);
                    p += rowBytes;
                }
            }

            if (p != length)
                throw new InvalidDataException("Delta payload has unexpected trailing bytes.");
        }

        private void ValidateTopLevelOffsets(long fileLength)
        {
            long indexBytes = checked((long)_header.FrameCount * QFrameIndexEntry.SerializedSize);
            long indexEnd = checked(_header.FrameIndexOffset + indexBytes);
            if (_header.FrameIndexOffset < QAnimatorHeader.SerializedSize || indexEnd > fileLength)
                throw new InvalidDataException("QAnimator frame index lies outside file bounds.");
            if (_header.FrameDataOffset < indexEnd || _header.FrameDataOffset > fileLength)
                throw new InvalidDataException("QAnimator frame data offset is invalid.");
        }

        private void ValidateFrameEntry(int index, QFrameIndexEntry frame, int frameBytes, int maxDeltaPayload, double previousTimestamp)
        {
            if (frame.NearestKeyFrame > index)
                throw new InvalidDataException($"Frame {index} points to a future key frame.");
            if (frame.Timestamp < previousTimestamp)
                throw new InvalidDataException("QAnimator frame timestamps are not monotonic.");

            if (frame.Type == QFrameType.Key)
            {
                if (frame.NearestKeyFrame != index)
                    throw new InvalidDataException($"Key frame {index} must reference itself as nearest key frame.");
                if (frame.UncompressedLength != frameBytes)
                    throw new InvalidDataException($"Key frame {index} RGBA byte count does not match texture size.");
            }
            else if (frame.UncompressedLength < 4 || frame.UncompressedLength > maxDeltaPayload)
            {
                throw new InvalidDataException($"Delta frame {index} payload size is invalid.");
            }

            if (index == 0 && frame.Type != QFrameType.Key)
                throw new InvalidDataException("The first QAnimator frame must be a key frame.");
        }

        private void ValidateFramePayloadBounds(long fileLength)
        {
            for (int i = 0; i < _frames.Length; i++)
            {
                QFrameIndexEntry frame = _frames[i];
                if ((uint)frame.NearestKeyFrame >= (uint)_frames.Length || _frames[frame.NearestKeyFrame].Type != QFrameType.Key)
                    throw new InvalidDataException($"Frame {i} references an invalid nearest key frame.");

                long start = checked(_header.FrameDataOffset + frame.DataOffset);
                long end = checked(start + frame.CompressedLength);
                if (start < _header.FrameDataOffset || end > fileLength)
                    throw new InvalidDataException($"Frame {i} payload lies outside file bounds.");
            }
        }

        private void EnsurePayloadCapacity(int required)
        {
            if (_payloadBuffer.Length < required)
                Array.Resize(ref _payloadBuffer, required);
        }

        private static int ReadInt32(byte[] data, ref int p, int length)
        {
            if (p + 4 > length) throw new InvalidDataException("Truncated delta payload.");
            int value = data[p] | (data[p + 1] << 8) | (data[p + 2] << 16) | (data[p + 3] << 24);
            p += 4;
            return value;
        }

        private static int ReadUInt16(byte[] data, ref int p, int length)
        {
            if (p + 2 > length) throw new InvalidDataException("Truncated delta payload.");
            int value = data[p] | (data[p + 1] << 8);
            p += 2;
            return value;
        }
    }
}
