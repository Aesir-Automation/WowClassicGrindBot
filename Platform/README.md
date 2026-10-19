# Mac runtime foundation

Development branch: `macos/runtime-foundation` in
`Aesir-Automation/WowClassicGrindBot`.

Baseline: upstream `Xian55/WowClassicGrindBot` `dev` at
`c5bc4fb614b2054f10b1b377581d39fcb205c2fa`.
The fork's existing `dev` branch is deliberately not moved.

This first milestone is a standalone macOS capture/input probe. It is **not yet
wired into Core, BlazorServer, the combat goals, or an MCP server**. It adds no
dependencies to the Windows solution and does not change upstream runtime behavior.

## Contents

- `Runtime`: OS-neutral capture/input contracts, bounded BGRA frame protocol,
  and DataToColor normal-mode decoding with exact sentinel validation.
- `MacOS/Native`: Swift helper using ScreenCaptureKit and Core Graphics.
- `MacOS/Probe`: .NET CLI that captures one selected window and optionally decodes
  an existing `frame_config.json` layout.
- `ContractTests`: dependency-free checks for framing, partial reads, pixel order,
  row padding, malformed input, layout validation, native conversion, and input dry-run.

The .NET projects explicitly target `net10.0`. `Platform/global.json` permits newer
.NET 10 feature bands while leaving the upstream SDK policy untouched.

## Build and test on Mac

Requires macOS 14+, Apple Command Line Tools, and .NET 10 SDK. From the repository root:

```sh
cd Platform
sh MacOS/Native/build.sh
dotnet build MacOS/Probe/Probe.csproj
dotnet run --project ContractTests/ContractTests.csproj -- \
  --native MacOS/Native/.build/wow-mac-bridge
MacOS/Native/.build/wow-mac-bridge doctor
```

The test suite's native fixture uses generated pixels and does not read the screen,
request permissions, or send keyboard input. Tests without `--native` run on any
.NET 10 platform.

## Live capture validation

Permission checks never prompt implicitly. `doctor` reports current Screen Recording
and Accessibility status. Grant Screen Recording to the helper or launching terminal
in macOS Settings and restart it. To explicitly request the screen permission prompt:

```sh
MacOS/Native/.build/wow-mac-bridge request-screen-permission
MacOS/Native/.build/wow-mac-bridge windows
```

Pick the WoW window ID from that list; IDs change when windows are recreated.
Replace `12345` below with that ID. Captures target only that window, exclude the
cursor/shadow, and report physical pixel dimensions and point-to-pixel scale.

```sh
dotnet run --project MacOS/Probe/Probe.csproj -- \
  MacOS/Native/.build/wow-mac-bridge 12345

# Optional crop in physical pixels of the full captured window:
dotnet run --project MacOS/Probe/Probe.csproj -- \
  MacOS/Native/.build/wow-mac-bridge 12345 0 0 600 100 \
  --layout /absolute/path/to/frame_config.json
```

The layout JSON must have upstream's `Frames` array with `Index`, `X`, and `Y`.
Indices must form a complete unique 0..N-1 set, with first/last entries the sentinel
cells. The probe translates full-window layout coordinates into crop coordinates.
Windows-generated layouts may not match Mac title bars, scaling, or resolution;
calibration is still manual. A sentinel mismatch is a failure, not a new state update.
Metadata/config-mode pixel detection is not implemented in this milestone.

## Input probe

Mac hardware virtual key codes are **not Windows virtual keys or ConsoleKey values**.
A cross-platform semantic key mapping is still needed before replacing upstream input.

```sh
# Dry-run: prints a plan, posts no event, requires no permission.
MacOS/Native/.build/wow-mac-bridge key \
  --pid 12345 --key-code 0 --milliseconds 50
```

Adding `--execute` explicitly posts a key-down/up pair to the selected PID, only if
Accessibility is granted and that application is foreground at the time of dispatch.
The helper never focuses applications. Holds are bounded to 1..2000 ms, with key-up
in a `defer` block. This is not a claim of WoW input compatibility or background input
support. Killing the helper during a hold is not supported; the .NET wrapper lets an
already-started bounded press finish before returning.

## Wire format

Capture writes only one binary frame to stdout; errors go to stderr with exit code 1.
Other commands produce JSON. All numeric fields are little-endian:

| Byte offset | Field |
| --- | --- |
| 0 | ASCII `PXF1` |
| 4 | uint32 width |
| 8 | uint32 height |
| 12 | uint32 row stride |
| 16 | uint32 payload length |
| 20 | float32 point-to-pixel scale |
| 24 | uint64 monotonic capture/conversion timestamp, nanoseconds |
| 32 | Top-to-bottom premultiplied BGRA8 pixels in sRGB |

The maximum dimension is 16384, maximum payload 128 MiB, and payload length must equal
stride times height. Decoder integer encoding is `B | (G << 8) | (R << 16)`, with
opaque black first sentinel and RGB `(30,132,129)` last sentinel, matching upstream's
normal-mode `IAddonDataProvider` contract. Sentinel checks do not prove all pixels
survive color management: live calibration must validate a broader color test pattern.

## Integration sequence

1. Verify exact game pixels, point/pixel scale, crop origin, title bar behavior, and
   display/color-profile changes on the native Mac WoW client.
2. Replace per-snapshot helper startup with one persistent ScreenCaptureKit stream;
   measure frame age, decoding latency, CPU usage, and dropped/stale frames.
3. Add Mac window/process discovery, semantic keyboard and mouse mappings, input
   ownership, cancellation, and an emergency release-all control.
4. Extract upstream capture/input/process contracts from Windows assembly dependencies.
   Adapt this probe to `IWowScreen` / `IAddonDataProvider`; replace direct
   `WowProcessInput` dependencies via DI. Avoid adding WinAPI references to this layer.
5. Validate one attended rotation, including manual movement/targeting boundaries.
6. Expose runtime observations and task commands through MCP. The local runtime owns
   time-critical actions; the dashboard/MCP clients do not own combat timing.
7. Reuse portable pathfinding for grinding, then add resumable quest workflows.

## Upstream and distribution

No top-level license was found in the inspected upstream tree. Preserve inherited
notices and history; this branch does not assert or add a license for upstream code.
Clarify distribution rights before releasing a derivative product. Do not copy third-party
license text into the root as if it covered the entire repository.

The repository contains useful Windows runtime and pathfinding work. Keep platform
changes additive and make the permanent fork/extraction decision after live Mac tests.
