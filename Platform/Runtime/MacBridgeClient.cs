using System.Diagnostics;
using System.Globalization;

namespace WowBot.Platform;

public sealed class MacBridgeClient(string executablePath) : IWindowCapture, IKeyboardInput
{
    public async Task<PixelFrame> CaptureAsync(CaptureRequest request, CancellationToken token = default)
    {
        List<string> args = ["capture", "--window-id", request.WindowId.ToString(CultureInfo.InvariantCulture)];
        if (request.Region is PixelRegion region)
        {
            region.Validate();
            args.AddRange(["--region", FormattableString.Invariant($"{region.X},{region.Y},{region.Width},{region.Height}")]);
        }

        using Process process = Start(args);
        using CancellationTokenRegistration cancel = token.Register(() => Kill(process));
        Task<string> error = process.StandardError.ReadToEndAsync(token);
        try
        {
            PixelFrame frame = await FrameProtocol.ReadAsync(process.StandardOutput.BaseStream, token);
            byte[] extra = new byte[1];
            if (await process.StandardOutput.BaseStream.ReadAsync(extra, token) != 0)
            {
                throw new InvalidDataException("Unexpected bytes after capture frame.");
            }
            await process.WaitForExitAsync(token);
            await CheckExit(process, error);
            return frame;
        }
        catch (EndOfStreamException)
        {
            await process.WaitForExitAsync(token);
            await CheckExit(process, error);
            throw;
        }
        finally
        {
            Kill(process);
        }
    }

    public async Task PressKeyAsync(int processId, ushort platformKeyCode, int durationMs,
        bool execute = false, CancellationToken token = default)
    {
        if (processId <= 0 || platformKeyCode > 127 || durationMs is < 1 or > 2000)
        {
            throw new ArgumentOutOfRangeException(nameof(durationMs));
        }
        // The helper completes each bounded key press and posts key-up itself.
        // Cancellation is checked before starting; do not kill a process holding a key.
        token.ThrowIfCancellationRequested();
        List<string> args = ["key", "--pid", processId.ToString(CultureInfo.InvariantCulture),
            "--key-code", platformKeyCode.ToString(CultureInfo.InvariantCulture),
            "--milliseconds", durationMs.ToString(CultureInfo.InvariantCulture)];
        if (execute)
        {
            args.Add("--execute");
        }
        using Process process = Start(args);
        Task<string> error = process.StandardError.ReadToEndAsync();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        await output;
        await CheckExit(process, error);
    }

    private Process Start(IEnumerable<string> args)
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("The native bridge requires macOS.");
        }
        ProcessStartInfo start = new(Path.GetFullPath(executablePath))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (string arg in args)
        {
            start.ArgumentList.Add(arg);
        }
        return Process.Start(start) ?? throw new IOException("Could not start native bridge.");
    }

    private static async Task CheckExit(Process process, Task<string> error)
    {
        string message = await error;
        if (process.ExitCode != 0)
        {
            throw new IOException($"Native bridge exited {process.ExitCode}: {message.Trim()}");
        }
    }

    private static void Kill(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }
    }
}
