using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
using WowBot.Platform;

int checks = 0;
void Check(bool condition, string name)
{
    if (!condition)
    {
        throw new Exception($"FAIL: {name}");
    }
    checks++;
    Console.WriteLine($"PASS: {name}");
}

async Task Reject(byte[] bytes, string name)
{
    try
    {
        await FrameProtocol.ReadAsync(new MemoryStream(bytes));
    }
    catch (Exception e) when (e is InvalidDataException or EndOfStreamException)
    {
        Check(true, name);
        return;
    }
    throw new Exception($"FAIL: {name}");
}

byte[] MakeFrame()
{
    byte[] bytes = new byte[FrameProtocol.HeaderBytes + 32]; // padded rows
    "PXF1"u8.CopyTo(bytes);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 3);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 2);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 16);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 32);
    BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(20), 2);
    BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(24), 42);
    byte[] row = [0, 0, 0, 255, 0x56, 0x34, 0x12, 255, 0x81, 0x84, 0x1e, 255];
    row.CopyTo(bytes, 32);
    new byte[] { 0xef, 0xcd, 0xab, 255 }.CopyTo(bytes, 48);
    return bytes;
}

byte[] valid = MakeFrame();
PixelFrame frame = await FrameProtocol.ReadAsync(new FragmentedStream(valid));
Check(frame.Width == 3 && frame.Height == 2 && frame.Stride == 16 && frame.PointPixelScale == 2 && frame.TimestampNanoseconds == 42,
    "partial pipe reads preserve dimensions, padding, scale, timestamp");
Check(frame.GetRgb(1, 0) == 0x123456 && frame.GetRgb(0, 1) == 0xabcdef, "BGRA channel order and row stride");
PixelCell[] cells = [new(0, 0, 0), new(1, 1, 0), new(2, 2, 0)];
int[] output = [-1, -1, -1];
Check(AddonPixelDecoder.TryDecode(frame, cells, output) && output.SequenceEqual([0, 0x123456, 0x1e8481]),
    "DataToColor sentinels and 24-bit integer decoding");

byte[] badSentinel = MakeFrame();
badSentinel[40] = 0;
PixelFrame corrupt = await FrameProtocol.ReadAsync(new MemoryStream(badSentinel));
output = [-1, -1, -1];
Check(!AddonPixelDecoder.TryDecode(corrupt, cells, output) && output.All(n => n == -1), "bad sentinel never publishes partial/stale values");
Check(!AddonPixelDecoder.TryDecode(frame, [new(0, 0, 0), new(1, -1, 0), new(2, 2, 0)], output), "crop/layout coordinate mismatch rejected");
Check(!AddonPixelDecoder.TryDecode(frame, [new(0, 0, 0), new(0, 1, 0), new(2, 2, 0)], output), "duplicate cell indices rejected");

byte[] malformed = MakeFrame();
malformed[0] = 0;
await Reject(malformed, "bad protocol version rejected");
malformed = MakeFrame();
BinaryPrimitives.WriteUInt32LittleEndian(malformed.AsSpan(16), uint.MaxValue);
await Reject(malformed, "oversized payload rejected before allocation");
malformed = MakeFrame();
BinaryPrimitives.WriteUInt32LittleEndian(malformed.AsSpan(12), 1);
await Reject(malformed, "invalid stride rejected");
malformed = MakeFrame();
BinaryPrimitives.WriteSingleLittleEndian(malformed.AsSpan(20), float.NaN);
await Reject(malformed, "invalid scale rejected");
await Reject(valid[..^1], "truncated payload rejected");

void RejectHeartbeat(Action action, string name)
{
    try { action(); }
    catch (Exception error) when (error is InvalidDataException or ArgumentException)
    { Check(true, name); return; }
    throw new Exception($"FAIL: {name}");
}
PixelHeartbeat changing = new(TimeSpan.FromSeconds(2));
changing.Observe(0xfffffe, TimeSpan.Zero);
changing.Observe(0xffffff, TimeSpan.FromMilliseconds(100));
changing.Observe(0, TimeSpan.FromMilliseconds(200));
changing.RequireProgress();
Check(changing.Advances == 2, "rendered heartbeat accepts forward progress across 24-bit wrap");
changing.Observe(0, TimeSpan.FromMilliseconds(300));
Check(changing.Advances == 2, "duplicate source frame does not count as heartbeat progress");
RejectHeartbeat(() => changing.Observe(0xffffff, TimeSpan.FromMilliseconds(400)), "backward/replayed heartbeat rejected");
PixelHeartbeat frozen = new(TimeSpan.FromSeconds(2));
frozen.Observe(123, TimeSpan.Zero);
RejectHeartbeat(() => frozen.Observe(123, TimeSpan.FromSeconds(3)), "frozen pixels rejected regardless of helper timestamp");
RejectHeartbeat(() => frozen.Observe(124, TimeSpan.FromSeconds(3)), "late source change cannot hide excessive observation gap");
RejectHeartbeat(() => new PixelHeartbeat(TimeSpan.FromSeconds(2)).RequireProgress(), "short run cannot claim source progress");
RejectHeartbeat(() => changing.Observe(1, TimeSpan.FromMilliseconds(1)), "nonmonotonic observation clock rejected");

if (args.Length == 2 && args[0] == "--native")
{
    string helper = Path.GetFullPath(args[1]);
    using Process fixture = Process.Start(new ProcessStartInfo(helper)
    {
        ArgumentList = { "fixture" }, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
    }) ?? throw new Exception("Native fixture failed to start.");
    Task<string> errors = fixture.StandardError.ReadToEndAsync();
    PixelFrame native = await FrameProtocol.ReadAsync(fixture.StandardOutput.BaseStream);
    await fixture.WaitForExitAsync();
    Check(fixture.ExitCode == 0, $"native fixture exits cleanly: {await errors}");
    Check(native.Width == 3 && native.Height == 2 && native.PointPixelScale == 2 &&
        native.GetRgb(1, 0) == 0x123456 && native.GetRgb(0, 1) == 0xabcdef,
        "Swift CoreGraphics to .NET preserves pixel channels, orientation, and scale");
    Check(AddonPixelDecoder.TryDecode(native, cells, new int[3]), "native pixels satisfy upstream addon sentinels");

    using Process colors = Process.Start(new ProcessStartInfo(helper)
    {
        ArgumentList = { "color-fixture" }, RedirectStandardOutput = true,
        RedirectStandardError = true, UseShellExecute = false
    }) ?? throw new Exception("Color fixture failed to start.");
    Task<string> colorErrors = colors.StandardError.ReadToEndAsync();
    using CancellationTokenSource colorTimeout = new(TimeSpan.FromSeconds(30));
    using CancellationTokenRegistration colorCancel = colorTimeout.Token.Register(() =>
    {
        if (!colors.HasExited) colors.Kill(entireProcessTree: true);
    });
    PixelFrame exhaustive = await FrameProtocol.ReadAsync(colors.StandardOutput.BaseStream, colorTimeout.Token);
    await colors.WaitForExitAsync(colorTimeout.Token);
    Check(colors.ExitCode == 0 && exhaustive.Width == 4096 && exhaustive.Height == 4096,
        $"exhaustive native fixture has all 16,777,216 colors: {await colorErrors}");
    int mismatches = 0;
    for (int y = 0; y < exhaustive.Height; y++)
    {
        for (int x = 0; x < exhaustive.Width; x++)
        {
            if (exhaustive.GetRgb(x, y) != y * 4096 + x || exhaustive.GetAlpha(x, y) != 255)
            {
                mismatches++;
            }
        }
    }
    Check(mismatches == 0, $"all 24-bit values survive native conversion and .NET decoding; mismatches={mismatches}");

    using Process dryRun = Process.Start(new ProcessStartInfo(helper)
    {
        ArgumentList = { "key", "--pid", "2147483647", "--key-code", "0", "--milliseconds", "1" },
        RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
    }) ?? throw new Exception("Dry-run failed to start.");
    Task<string> dryError = dryRun.StandardError.ReadToEndAsync();
    string plan = await dryRun.StandardOutput.ReadToEndAsync();
    await dryRun.WaitForExitAsync();
    using JsonDocument result = JsonDocument.Parse(plan);
    Check(dryRun.ExitCode == 0 && !result.RootElement.GetProperty("executed").GetBoolean(),
        $"keyboard defaults to dry-run without permissions or live target: {await dryError}");
}
else if (args.Length != 0)
{
    throw new ArgumentException("Usage: ContractTests [--native path-to-helper]");
}
Console.WriteLine($"{checks} checks passed.");

sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
{
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return base.ReadAsync(buffer[..Math.Min(buffer.Length, 3)], cancellationToken);
    }
}
