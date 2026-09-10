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

        public float Fps => FpsDenominator == 0 ? 0f : (float)FpsNumerator / FpsDenominator;

        public QAnimatorHeader(BinaryReader reader)
        {
            uint magic = reader.ReadUInt32();
            if (magic != Magic)
                throw new InvalidDataException("Not a QAnimator file (QANM magic missing).");

            ushort version = reader.ReadUInt16();
            if (version != SupportedVersion)
                throw new InvalidDataException($"Unsupported QAnimator version {version}.");

            ushort headerSize = reader.ReadUInt16();
            if (headerSize < SerializedSize)
                throw new InvalidDataException("Invalid QAnimator v1 header size.");

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
                throw new InvalidDataException("Invalid QAnimator metadata.");
            if (PixelFormat != 1 || Codec != 1 || Compression != 1)
                throw new InvalidDataException("This runtime currently supports RGBA32 + BlockDelta + Deflate only.");
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
            Type = (QFrameType)reader.ReadByte();
            reader.ReadByte();
            reader.ReadByte();
            reader.ReadByte();
            NearestKeyFrame = reader.ReadInt32();
            DataOffset = reader.ReadInt64();
            CompressedLength = reader.ReadInt32();
            UncompressedLength = reader.ReadInt32();
            Timestamp = reader.ReadDouble();

            if (DataOffset < 0 || CompressedLength <= 0 || UncompressedLength <= 0)
                throw new InvalidDataException("Invalid QAnimator frame index entry.");
        }
    }
}
