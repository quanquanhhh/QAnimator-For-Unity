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
            using var stream = new MemoryStream(data, writable: false);
            using var reader = new BinaryReader(stream);

            _header = new QAnimatorHeader(reader);
            _frames = new QFrameIndexEntry[_header.FrameCount];

            stream.Position = _header.FrameIndexOffset;
            int maxPayload = 0;
            for (int i = 0; i < _frames.Length; i++)
            {
                _frames[i] = new QFrameIndexEntry(reader);
                if (_frames[i].UncompressedLength > maxPayload)
                    maxPayload = _frames[i].UncompressedLength;
            }

            ValidateBounds(data.LongLength);
            _rgba = new byte[checked(_header.Width * _header.Height * 4)];
            _payloadBuffer = new byte[Math.Max(4, maxPayload)];
        }

        public void DecodeFrame(int targetFrame)
        {
            if ((uint)targetFrame >= (uint)_frames.Length)
                throw new ArgumentOutOfRangeException(nameof(targetFrame));
            if (targetFrame == _decodedFrame)
                return;

            if (_decodedFrame >= 0 && targetFrame > _decodedFrame)
            {
                for (int i = _decodedFrame + 1; i <= targetFrame; i++)
                    DecodeSingle(i);
                return;
            }

            int key = _frames[targetFrame].NearestKeyFrame;
            if (key < 0 || key > targetFrame || _frames[key].Type != QFrameType.Key)
                throw new InvalidDataException("Invalid nearest key-frame index.");

            DecodeSingle(key);
            for (int i = key + 1; i <= targetFrame; i++)
                DecodeSingle(i);
        }

        private void DecodeSingle(int frameIndex)
        {
            QFrameIndexEntry entry = _frames[frameIndex];
            EnsurePayloadCapacity(entry.UncompressedLength);

            long absoluteOffset = _header.FrameDataOffset + entry.DataOffset;
            using var source = new MemoryStream(_data, checked((int)absoluteOffset), entry.CompressedLength, writable: false, publiclyVisible: true);
            using var deflate = new DeflateStream(source, CompressionMode.Decompress, leaveOpen: false);

            int total = 0;
            while (total < entry.UncompressedLength)
            {
                int read = deflate.Read(_payloadBuffer, total, entry.UncompressedLength - total);
                if (read <= 0) break;
                total += read;
            }
            if (total != entry.UncompressedLength)
                throw new InvalidDataException($"Frame {frameIndex} decompressed to {total} bytes; expected {entry.UncompressedLength}.");

            if (entry.Type == QFrameType.Key)
            {
                if (entry.UncompressedLength != _rgba.Length)
                    throw new InvalidDataException("Key frame RGBA byte count does not match texture size.");
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
            for (int i = 0; i < changedBlocks; i++)
            {
                int x = ReadUInt16(payload, ref p, length);
                int y = ReadUInt16(payload, ref p, length);
                int bw = ReadUInt16(payload, ref p, length);
                int bh = ReadUInt16(payload, ref p, length);

                if (x < 0 || y < 0 || bw <= 0 || bh <= 0 || x + bw > _header.Width || y + bh > _header.Height)
                    throw new InvalidDataException("Delta block is outside frame bounds.");

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

        private void EnsurePayloadCapacity(int required)
        {
            if (_payloadBuffer.Length < required)
                Array.Resize(ref _payloadBuffer, required);
        }

        private void ValidateBounds(long fileLength)
        {
            long indexEnd = _header.FrameIndexOffset + (long)_frames.Length * QFrameIndexEntry.SerializedSize;
            if (_header.FrameIndexOffset < QAnimatorHeader.SerializedSize || indexEnd > fileLength)
                throw new InvalidDataException("QAnimator frame index lies outside file bounds.");
            if (_header.FrameDataOffset < indexEnd || _header.FrameDataOffset > fileLength)
                throw new InvalidDataException("QAnimator frame data offset is invalid.");

            foreach (QFrameIndexEntry frame in _frames)
            {
                long start = _header.FrameDataOffset + frame.DataOffset;
                long end = start + frame.CompressedLength;
                if (start < _header.FrameDataOffset || end > fileLength)
                    throw new InvalidDataException("QAnimator frame payload lies outside file bounds.");
            }
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
