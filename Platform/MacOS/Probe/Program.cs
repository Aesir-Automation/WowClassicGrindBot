using System.Globalization;
using System.Text.Json;
using WowBot.Platform;

try
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: Probe <native-helper-path> <window-id> [x y width height] [--layout frame_config.json]; or Probe --file capture.pxf [--layout frame_config.json]");
        return 2;
    }
    string? layoutPath = null;
    int layoutFlag = Array.IndexOf(args, "--layout");
    if (layoutFlag >= 0)
    {
        if (layoutFlag != args.Length - 2)
        {
            throw new ArgumentException("--layout must be the last option followed by a path.");
        }
        layoutPath = args[^1];
        args = args[..layoutFlag];
    }
    if (args.Length is not (2 or 6))
    {
        throw new ArgumentException("Supply either no region or exactly x y width height.");
    }
    PixelRegion? region = args.Length == 6
        ? new PixelRegion(int.Parse(args[2], CultureInfo.InvariantCulture), int.Parse(args[3], CultureInfo.InvariantCulture),
            int.Parse(args[4], CultureInfo.InvariantCulture), int.Parse(args[5], CultureInfo.InvariantCulture))
        : null;
    using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
    PixelFrame frame;
    if (args[0] == "--file")
    {
        if (region is not null) throw new ArgumentException("--file uses the complete saved frame; omit region arguments.");
        await using FileStream input = File.OpenRead(args[1]);
        frame = await FrameProtocol.ReadAsync(input, timeout.Token);
        if (input.Position != input.Length) throw new InvalidDataException("Unexpected bytes after saved frame.");
    }
    else
    {
        IWindowCapture bridge = args[0].EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? new MacAppBridgeClient(args[0]) : new MacBridgeClient(args[0]);
        frame = await bridge.CaptureAsync(new CaptureRequest(uint.Parse(args[1], CultureInfo.InvariantCulture), region), timeout.Token);
    }
    object? decoded = null;
    if (layoutPath is not null)
    {
        using JsonDocument layout = JsonDocument.Parse(await File.ReadAllTextAsync(layoutPath));
        PixelCell[] cells = layout.RootElement.GetProperty("Frames").EnumerateArray()
            .Select(cell => new PixelCell(cell.GetProperty("Index").GetInt32(), cell.GetProperty("X").GetInt32(), cell.GetProperty("Y").GetInt32()))
            .ToArray();
        // Existing frame configs use full-window pixel coordinates. Translate to a crop.
        if (region is PixelRegion crop)
        {
            cells = cells.Select(c => c with { X = c.X - crop.X, Y = c.Y - crop.Y }).ToArray();
        }
        int[] values = new int[cells.Length];
        if (!AddonPixelDecoder.TryDecode(frame, cells, values))
        {
            throw new InvalidDataException("Addon sentinels/layout did not match. Recalibrate for this capture size/scale; do not use stale values.");
        }
        decoded = values;
    }
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        frame.Width, frame.Height, frame.Stride, frame.PointPixelScale,
        frame.TimestampNanoseconds, FirstPixelRgb = frame.GetRgb(0, 0), AddonValues = decoded
    }));
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
