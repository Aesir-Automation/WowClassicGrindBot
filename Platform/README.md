# Mac runtime foundation

Development branch: `macos/runtime-foundation` in
`Aesir-Automation/WowClassicGrindBot`.

Baseline: upstream `Xian55/WowClassicGrindBot` `dev` at
`c5bc4fb614b2054f10b1b377581d39fcb205c2fa`.
The fork's existing `dev` branch is deliberately not moved.

This milestone is a standalone macOS capture/input probe and native app. It is **not yet
wired into Core, BlazorServer, the combat goals, or an MCP server**. It adds no
dependencies to the Windows solution and does not change upstream runtime behavior.

## Contents

- `Runtime`: OS-neutral capture/input contracts, bounded BGRA frame protocol,
  and DataToColor normal-mode decoding with exact sentinel validation.
- `MacOS/Native`: Swift helper and app using ScreenCaptureKit and Core Graphics,
  with owner-only Unix-domain IPC for clients of the authorized app.
- `MacOS/Probe`: .NET CLI that captures one selected window and optionally decodes
  an existing `frame_config.json` layout.
- `ContractTests`: dependency-free checks for framing, partial reads, pixel order,
  row padding, malformed input, layout validation, native conversion of every
  possible 24-bit RGB value, and input dry-run.
- `GameFixture`: independent WoW color palette/Space-counter addon and exact calibration tool.
- `MacOS/Verify`: repeated read-only capture acceptance against independently
  known static pixel values, with machine-readable pass/fail and snapshot timings.

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

## Native app and process permissions

The build produces `MacOS/Native/.build/WowMacBridge.app` and a compatible CLI
symlink at `.build/wow-mac-bridge`. Open the app in Finder. Its window provides
explicit permission requests, window selection, saved-frame capture, and a single
foreground-only Space press after a five-second focus delay. Capture files remain
local in `.build/live-evidence`. Do not select unrelated/private windows.

**Observed on the development Mac:** the launched app could capture Battle.net
with Screen Recording authorized, while a CLI subprocess launched by the coding
workspace still reported no access. A parent's authorization is not portable across
launch contexts. Treat the app process as the permission owner.

The app publishes `.build/live-evidence/endpoint.json` describing a Unix socket in
a random private directory (mode 0700, socket mode 0600). `MacAppBridgeClient`
implements the same runtime contracts by asking that app to perform operations.
No TCP port is opened. Requests are newline-terminated JSON argument arrays, capped
at 4096 bytes. The server serializes requests; replies start with a JSON status line,
followed by PXF1 bytes for capture. There are no automatic input retries.

Probe and Verify accept the endpoint JSON path in place of the helper executable:

```sh
dotnet run --project MacOS/Probe/Probe.csproj -- \
  MacOS/Native/.build/live-evidence/endpoint.json 12345

# Read a frame already captured through the GUI, without socket access:
dotnet run --project MacOS/Probe/Probe.csproj -- \
  --file /absolute/path/to/capture.pxf --layout /absolute/path/to/frame_config.json
```

The IPC client/server compile, but live IPC remains unverified: this coding execution
environment rejected the Unix socket test with `Operation not permitted`. The GUI
capture and saved-file .NET path were tested successfully against Battle.net and the
native WoW 1.60.1 game window. Do not interpret those
as proof that the socket integration works yet.

Development builds use ad-hoc signing. Rebuilding changes the code identity and may
invalidate existing TCC grants even when Settings still shows a switch enabled.
Re-authorize the rebuilt app through macOS when needed. Distribution needs a stable
signing identity; this build does not manufacture or weaken a designated requirement.

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

## Repeated pixel acceptance

Display an independently known, static addon test pattern in the game, calibrate
its cell coordinates, and create `expected-values.json` with a JSON integer array
in cell-index order (including first/last sentinels). Do not generate the expected
values from captured pixels: that would make this test circular. A normal changing
DataToColor state stream is unsuitable for this static-pattern test.

```sh
dotnet run --project MacOS/Verify/Verify.csproj -- \
  MacOS/Native/.build/wow-mac-bridge 12345 \
  /absolute/path/to/frame_config.json /absolute/path/to/expected-values.json 30
```

The verifier captures the selected window repeatedly, checks every cell exactly,
requires stable dimensions/scale and increasing helper timestamps, and emits JSON
with exit code 0 on pass or 1 on failure. Samples are bounded to 2..1000. This is a
read-only test: no keys are posted. Timing includes process startup and a full-window
snapshot; it is not streaming throughput. Helper timestamps reflect conversion time,
so increasing timestamps cannot prove that the game supplied fresh frames. The caller
must verify the selected window identity. A pass establishes only static pixel
round-tripping, not game input, rotation correctness, or combat latency.

See [VALIDATION.md](VALIDATION.md) for observed results and the remaining proof gates.

## Device-origin input target

The production requirement is device-origin input. Physical USB HID is the preferred
candidate, pending hardware availability, with keyboard/mouse and an optional
Xbox-style gamepad backend.
The CGEvent bridge below is a diagnostic backend and does not meet that requirement.
See [INPUT-DESIGN.md](INPUT-DESIGN.md) for the device boundary, required contract
changes and proof gates. No physical HID adapter has been implemented yet.

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
