using System.Buffers.Binary;

namespace WowBot.Platform;

public static class FrameProtocol
{
    public const int HeaderBytes = 32;
    public const int MaxPayloadBytes = 128 * 1024 * 1024;
    public const int MaxDimension = 16384;

    // PXF1, uint32 width/height/stride/length, float32 scale, uint64 monotonic ns.
    // All numeric fields are little-endian. Rows are top-to-bottom BGRA8.
    public static async Task<PixelFrame> ReadAsync(Stream stream, CancellationToken token = default)
    {
        byte[] header = new byte[HeaderBytes];
        await stream.ReadExactlyAsync(header, token);
        if (!header.AsSpan(0, 4).SequenceEqual("PXF1"u8))
        {
            throw new InvalidDataException("Unsupported capture protocol; expected PXF1.");
        }

        uint width = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
        uint height = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8));
        uint stride = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12));
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(16));
        float scale = BinaryPrimitives.ReadSingleLittleEndian(header.AsSpan(20));
        ulong timestamp = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(24));

        if (width == 0 || height == 0 || width > MaxDimension || height > MaxDimension ||
            stride < width * 4 || length > MaxPayloadBytes ||
            (ulong)stride * height != length || !float.IsFinite(scale) || scale <= 0)
        {
            throw new InvalidDataException("Invalid capture dimensions, stride, size, or scale.");
        }

        byte[] pixels = new byte[(int)length];
        await stream.ReadExactlyAsync(pixels, token);
        return new PixelFrame((int)width, (int)height, (int)stride, scale, timestamp, pixels);
    }
}
