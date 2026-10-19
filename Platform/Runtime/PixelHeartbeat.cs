namespace WowBot.Platform;

/// <summary>Tracks an addon-rendered 24-bit counter using local observation time.</summary>
public sealed class PixelHeartbeat(TimeSpan maximumStall)
{
    private int? previous;
    private TimeSpan lastChange;
    private TimeSpan lastObservation;
    public int Advances { get; private set; }

    public void Observe(int value, TimeSpan elapsed)
    {
        if (maximumStall <= TimeSpan.Zero || value is < 0 or > 0xffffff ||
            elapsed < TimeSpan.Zero || elapsed < lastObservation)
            throw new ArgumentException("Invalid heartbeat value, time, or stall bound.");
        if (previous is null)
        {
            previous = value;
            lastChange = lastObservation = elapsed;
            return;
        }
        // Check the gap before accepting a new value: a late update must not hide
        // a stalled observer. Half-range ordering permits the 24-bit wrap.
        if (elapsed - lastChange > maximumStall)
            throw new InvalidDataException("Addon heartbeat stalled or observation gap exceeded the bound.");
        int delta = (value - previous.Value) & 0xffffff;
        if (delta >= 0x800000)
            throw new InvalidDataException("Addon heartbeat moved backwards or reset; restart verification.");
        if (delta != 0)
        {
            Advances++;
            previous = value;
            lastChange = elapsed;
        }
        lastObservation = elapsed;
    }

    public void RequireProgress()
    {
        if (Advances < 2)
            throw new InvalidDataException("At least two observed addon heartbeat advances are required.");
    }
}
