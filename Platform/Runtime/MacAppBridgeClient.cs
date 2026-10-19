using System.Globalization;
using System.Net.Sockets;
using System.Text.Json;

namespace WowBot.Platform;

// Connect to the launched macOS app that owns TCC authorization. The endpoint file
// is explicitly selected by the caller; this opens no TCP port or remote service.
public sealed class MacAppBridgeClient(string endpointFile) : IWindowCapture, IKeyboardInput
{
    public async Task<PixelFrame> CaptureAsync(CaptureRequest request, CancellationToken token = default)
    {
        List<string> args = ["capture", "--window-id", request.WindowId.ToString(CultureInfo.InvariantCulture)];
        if (request.Region is PixelRegion region)
        {
            region.Validate();
            args.AddRange(["--region", FormattableString.Invariant($"{region.X},{region.Y},{region.Width},{region.Height}")]);
        }
        using NetworkStream stream = await Connect(args, token);
        using JsonDocument header = await ReadHeader(stream, token);
        if (header.RootElement.GetProperty("format").GetString() != "PXF1")
            throw new InvalidDataException("Expected a PXF1 local capture response.");
        PixelFrame frame = await FrameProtocol.ReadAsync(stream, token);
        byte[] extra = new byte[1];
        if (await stream.ReadAsync(extra, token) != 0) throw new InvalidDataException("Unexpected bytes after frame.");
        return frame;
    }

    public async Task PressKeyAsync(int processId, ushort platformKeyCode, int durationMs,
        bool execute = false, CancellationToken token = default)
    {
        if (processId <= 0 || platformKeyCode > 127 || durationMs is < 1 or > 2000)
            throw new ArgumentOutOfRangeException(nameof(durationMs));
        token.ThrowIfCancellationRequested();
        List<string> args = ["key", "--pid", processId.ToString(CultureInfo.InvariantCulture),
            "--key-code", platformKeyCode.ToString(CultureInfo.InvariantCulture),
            "--milliseconds", durationMs.ToString(CultureInfo.InvariantCulture)];
        if (execute) args.Add("--execute");
        // Once dispatched, allow the bounded key-up to complete. A transport
        // failure never triggers a retry, which could duplicate game actions.
        using CancellationTokenSource completion = new(TimeSpan.FromSeconds(15));
        using NetworkStream stream = await Connect(args, completion.Token);
        using JsonDocument response = await ReadHeader(stream, completion.Token);
    }

    private async Task<NetworkStream> Connect(List<string> args, CancellationToken token)
    {
        using JsonDocument endpoint = JsonDocument.Parse(await File.ReadAllTextAsync(endpointFile, token));
        if (endpoint.RootElement.GetProperty("protocol").GetString() != "WMB1")
            throw new InvalidDataException("Unsupported Mac app bridge endpoint.");
        string path = endpoint.RootElement.GetProperty("socketPath").GetString()
            ?? throw new InvalidDataException("Missing socket path.");
        Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), token);
            NetworkStream stream = new(socket, ownsSocket: true);
            try
            {
                byte[] line = JsonSerializer.SerializeToUtf8Bytes(args);
                await stream.WriteAsync(line, token);
                await stream.WriteAsync(new byte[] { 10 }, token);
                return stream;
            }
            catch { stream.Dispose(); throw; }
        }
        catch { socket.Dispose(); throw; }
    }

    private static async Task<JsonDocument> ReadHeader(Stream stream, CancellationToken token)
    {
        using MemoryStream line = new();
        byte[] one = new byte[1];
        while (true)
        {
            await stream.ReadExactlyAsync(one, token);
            if (one[0] == 10) break;
            line.WriteByte(one[0]);
            if (line.Length > 65536) throw new InvalidDataException("Local response header exceeds limit.");
        }
        JsonDocument header = JsonDocument.Parse(line.ToArray());
        if (!header.RootElement.GetProperty("ok").GetBoolean())
        {
            string error = header.RootElement.GetProperty("error").GetString() ?? "Mac app bridge failed.";
            header.Dispose();
            throw new IOException(error);
        }
        return header;
    }
}
