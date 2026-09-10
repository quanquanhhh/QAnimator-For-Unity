using System;
using System.IO;

namespace QAnimator.Unity
{
    internal enum QFrameType : byte
    {
        Key = 0,
        Delta = 1,
    }

    internal readonly struct QAnimatorHeader
    {
        public const uint Magic = 0x4D4E4151;
        public const ushort SupportedVersion = 1;
        public const int SerializedSize = 64;
        public const byte Rgba32PixelFormat = 1;
        public const byte BlockDeltaCodec = 1;
        public const byte DeflateCompression = 1;

        public readonly int Width;
        public readonly int Height;
        public readonly int FpsNumerator;
        public readonly int FpsDenominator;
        public readonly int FrameCount;
        public readonly double Duration;
        public readonly byte PixelFormat;
        public readonly bool HasAlpha;
        public readonly byte Codec;
        public readonly byte Compression;
        public readonly ushort KeyFrameInterval;
        public readonly ushort BlockSize;
        public readonly long FrameIndexOffset;
        public readonly long FrameDataOffset;

        public float Fps => (float)FpsNumerator / FpsDenominator;

        public QAnimatorHeader(BinaryReader reader)
        {
            uint magic = reader.ReadUInt32();
            if (magic != Magic)
                throw new InvalidDataException("Not a QAnimator file (QANM magic missing).");

            ushort version = reader.ReadUInt16();
            if (version != SupportedVersion)
                throw new InvalidDataException($"Unsupported QAnimator version {version}.");

            ushort headerSize = reader.ReadUInt16();
            if (headerSize != SerializedSize)
                throw new InvalidDataException($"Invalid QAnimator v1 header size {headerSize}.");

            Width = reader.ReadInt32();
            Height = reader.ReadInt32();
            FpsNumerator = reader.ReadInt32();
            FpsDenominator = reader.ReadInt32();
            FrameCount = reader.ReadInt32();
            Duration = reader.ReadDouble();
            PixelFormat = reader.ReadByte();
            HasAlpha = reader.ReadBoolean();
            Codec = reader.ReadByte();
            Compression = reader.ReadByte();
            KeyFrameInterval = reader.ReadUInt16();
            BlockSize = reader.ReadUInt16();
            FrameIndexOffset = reader.ReadInt64();
            FrameDataOffset = reader.ReadInt64();

            if (Width <= 0 || Height <= 0 || FrameCount <= 0 || FpsNumerator <= 0 || FpsDenominator <= 0)
                throw new InvalidDataException("Invalid QAnimator dimensions, frame count, or FPS.");
            if (double.IsNaN(Duration) || double.IsInfinity(Duration) || Duration <= 0)
                throw new InvalidDataException("Invalid QAnimator duration.");
            if (KeyFrameInterval == 0 || BlockSize == 0)
                throw new InvalidDataException("Invalid QAnimator key-frame interval or block size.");
            if (PixelFormat != Rgba32PixelFormat || Codec != BlockDeltaCodec || Compression != DeflateCompression)
                throw new InvalidDataException("This runtime currently supports RGBA32 + BlockDelta + Deflate only.");

            try
            {
                _ = checked(Width * Height * 4);
            }
            catch (OverflowException ex)
            {
                throw new InvalidDataException("QAnimator dimensions are too large.", ex);
            }
        }
    }

    internal readonly struct QFrameIndexEntry
    {
        public const int SerializedSize = 32;

        public readonly QFrameType Type;
        public readonly int NearestKeyFrame;
        public readonly long DataOffset;
        public readonly int CompressedLength;
        public readonly int UncompressedLength;
        public readonly double Timestamp;

        public QFrameIndexEntry(BinaryReader reader)
        {
            byte rawType = reader.ReadByte();
            if (rawType > (byte)QFrameType.Delta)
                throw new InvalidDataException($"Unknown QAnimator frame type {rawType}.");
            Type = (QFrameType)rawType;

            reader.ReadByte();
            reader.ReadByte();
            reader.ReadByte();
            NearestKeyFrame = reader.ReadInt32();
            DataOffset = reader.ReadInt64();
            CompressedLength = reader.ReadInt32();
            UncompressedLength = reader.ReadInt32();
            Timestamp = reader.ReadDouble();

            if (NearestKeyFrame < 0 || DataOffset < 0 || CompressedLength <= 0 || UncompressedLength <= 0)
                throw new InvalidDataException("Invalid QAnimator frame index entry.");
            if (double.IsNaN(Timestamp) || double.IsInfinity(Timestamp) || Timestamp < 0)
                throw new InvalidDataException("Invalid QAnimator frame timestamp.");
        }
    }
}
