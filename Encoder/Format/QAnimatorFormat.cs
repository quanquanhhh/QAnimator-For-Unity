using System;
using System.IO;
using System.Text;

namespace QAnimator.Encoder.Format;

internal enum QFrameType : byte
{
    Key = 0,
    Delta = 1,
}

internal enum QPixelFormat : byte
{
    Rgba32 = 1,
}

internal enum QCodecType : byte
{
    BlockDelta = 1,
}

internal enum QCompressionType : byte
{
    Deflate = 1,
}

internal sealed class QAnimatorHeader
{
    public const uint Magic = 0x4D4E4151; // "QANM" in little-endian
    public const ushort Version = 1;
    public const int SerializedSize = 64;

    public int Width;
    public int Height;
    public int FpsNumerator;
    public int FpsDenominator = 1;
    public int FrameCount;
    public double Duration;
    public QPixelFormat PixelFormat = QPixelFormat.Rgba32;
    public bool HasAlpha;
    public QCodecType Codec = QCodecType.BlockDelta;
    public QCompressionType Compression = QCompressionType.Deflate;
    public ushort KeyFrameInterval;
    public ushort BlockSize;
    public long FrameIndexOffset;
    public long FrameDataOffset;

    public void Write(BinaryWriter writer)
    {
        long start = writer.BaseStream.Position;
        writer.Write(Magic);
        writer.Write(Version);
        writer.Write((ushort)SerializedSize);
        writer.Write(Width);
        writer.Write(Height);
        writer.Write(FpsNumerator);
        writer.Write(FpsDenominator);
        writer.Write(FrameCount);
        writer.Write(Duration);
        writer.Write((byte)PixelFormat);
        writer.Write(HasAlpha);
        writer.Write((byte)Codec);
        writer.Write((byte)Compression);
        writer.Write(KeyFrameInterval);
        writer.Write(BlockSize);
        writer.Write(FrameIndexOffset);
        writer.Write(FrameDataOffset);

        int written = checked((int)(writer.BaseStream.Position - start));
        if (written > SerializedSize)
            throw new InvalidDataException("QAnimator header exceeded fixed v1 header size.");

        writer.Write(new byte[SerializedSize - written]);
    }
}

internal readonly record struct QFrameIndexEntry(
    QFrameType Type,
    int NearestKeyFrame,
    long DataOffset,
    int CompressedLength,
    int UncompressedLength,
    double Timestamp)
{
    public const int SerializedSize = 32;

    public void Write(BinaryWriter writer)
    {
        writer.Write((byte)Type);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write(NearestKeyFrame);
        writer.Write(DataOffset);
        writer.Write(CompressedLength);
        writer.Write(UncompressedLength);
        writer.Write(Timestamp);
    }
}
