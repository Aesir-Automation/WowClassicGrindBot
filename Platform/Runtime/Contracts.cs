namespace WowBot.Platform;

// Capture coordinates are physical pixels relative to the captured window image.
// Input coordinates/key codes must be translated by the platform implementation.
public readonly record struct PixelRegion(int X, int Y, int Width, int Height)
{
    public void Validate()
    {
        if (X < 0 || Y < 0 || Width <= 0 || Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(PixelRegion));
        }
    }
}

public readonly record struct CaptureRequest(uint WindowId, PixelRegion? Region = null);
public readonly record struct PixelCell(int Index, int X, int Y);

public interface IWindowCapture
{
    Task<PixelFrame> CaptureAsync(CaptureRequest request, CancellationToken token = default);
}

public interface IKeyboardInput
{
    Task PressKeyAsync(int processId, ushort platformKeyCode, int durationMs,
        bool execute = false, CancellationToken token = default);
}

public sealed record PixelFrame(int Width, int Height, int Stride, float PointPixelScale,
    ulong TimestampNanoseconds, ReadOnlyMemory<byte> Bgra)
{
    public int GetRgb(int x, int y)
    {
        if ((uint)x >= Width || (uint)y >= Height)
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Pixel lies outside the capture.");
        }

        int offset = checked(y * Stride + x * 4);
        ReadOnlySpan<byte> pixel = Bgra.Span.Slice(offset, 4);
        return pixel[0] | (pixel[1] << 8) | (pixel[2] << 16);
    }

    public byte GetAlpha(int x, int y)
    {
        _ = GetRgb(x, y);
        return Bgra.Span[checked(y * Stride + x * 4 + 3)];
    }
}
