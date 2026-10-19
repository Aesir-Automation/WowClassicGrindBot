using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using WowBot.Platform;

// Read-only acceptance test. Expected values must come from an independently
// known, static in-game pattern, never from the captures being tested.
try
{
    if (args.Length is not (4 or 5))
    {
        throw new ArgumentException("Usage: Verify <helper> <window-id> <layout.json> <expected-values.json> [sample-count=30]");
    }
    uint windowId = uint.Parse(args[1], CultureInfo.InvariantCulture);
    int count = args.Length == 5 ? int.Parse(args[4], CultureInfo.InvariantCulture) : 30;
    if (windowId == 0 || count is < 2 or > 1000)
    {
        throw new ArgumentException("Window ID must be positive; sample count must be 2..1000.");
    }
    using JsonDocument layout = JsonDocument.Parse(await File.ReadAllTextAsync(args[2]));
    PixelCell[] cells = layout.RootElement.GetProperty("Frames").EnumerateArray()
        .Select(c => new PixelCell(c.GetProperty("Index").GetInt32(), c.GetProperty("X").GetInt32(), c.GetProperty("Y").GetInt32()))
        .OrderBy(c => c.Index).ToArray();
    int[] expected = JsonSerializer.Deserialize<int[]>(await File.ReadAllTextAsync(args[3]))
        ?? throw new InvalidDataException("Expected a JSON array of integers.");
    if (expected.Length != cells.Length || cells.Length < 3 ||
        expected[0] != AddonPixelDecoder.FirstRgb || expected[^1] != AddonPixelDecoder.LastRgb ||
        expected.Any(n => n is < 0 or > 0xffffff))
    {
        throw new InvalidDataException("Expected values must match the layout, include both sentinels and at least one data cell, and be 24-bit integers.");
    }
    IWindowCapture bridge = args[0].EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? new MacAppBridgeClient(args[0]) : new MacBridgeClient(args[0]);
    using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(Math.Min(1800, count * 15)));
    List<double> durations = [];
    PixelFrame? previous = null;
    for (int sample = 0; sample < count; sample++)
    {
        long started = Stopwatch.GetTimestamp();
        PixelFrame frame = await bridge.CaptureAsync(new CaptureRequest(windowId), timeout.Token);
        durations.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        if (previous is not null && (frame.Width != previous.Width || frame.Height != previous.Height ||
            frame.PointPixelScale != previous.PointPixelScale || frame.TimestampNanoseconds <= previous.TimestampNanoseconds))
        {
            throw new InvalidDataException($"Sample {sample}: geometry/scale changed or timestamp did not advance.");
        }
        int[] actual = new int[cells.Length];
        if (!AddonPixelDecoder.TryDecode(frame, cells, actual))
        {
            throw new InvalidDataException($"Sample {sample}: invalid addon sentinels or layout.");
        }
        if (!actual.SequenceEqual(expected))
        {
            int index = Enumerable.Range(0, actual.Length).First(i => actual[i] != expected[i]);
            throw new InvalidDataException($"Sample {sample}, cell {index}: expected {expected[index]}, received {actual[index]}.");
        }
        previous = frame;
    }
    durations.Sort();
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        Status = "passed", Scope = "selected-window-static-pixel-roundtrip",
        WindowId = windowId, Samples = count, Cells = cells.Length,
        previous!.Width, previous.Height, previous.PointPixelScale,
        MedianSnapshotMilliseconds = durations[count / 2],
        P95SnapshotMilliseconds = durations[(int)Math.Ceiling(count * 0.95) - 1],
        ProvesGameInput = false, ProvesCombatTiming = false,
        Caveat = "Caller must verify this is the game window and supply independently known static values. Timestamps measure helper conversion, not source-frame freshness."
    }));
    return 0;
}
catch (Exception error)
{
    Console.WriteLine(JsonSerializer.Serialize(new { Status = "failed", Error = error.Message }));
    return 1;
}
