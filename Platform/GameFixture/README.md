# Independent game pixel fixture

PixelProof is a minimal original test addon. It paints 20 opaque color cells and
counts only Space key-down events in the penultimate cell, while propagating keys
to the client. It performs no movement, targeting, casting, or saved keystroke logging.
This is a test instrument, not the upstream DataToColor addon or a rotation.

The initial target is the installed Forever 1.60.1 client (TOC interface 16001).
Actual addon loading and API compatibility still require in-game validation. If the
client reports an interface mismatch, verify its interface number via GetBuildInfo
before changing the TOC. Do not claim other client compatibility from this fixture.

Copy the PixelProof folder into the client's Interface/AddOns before launching.
At a logged-in character, run `/pixelproof reset` to establish the independent static
palette from expected-values.json. Save a capture via the bridge app, then run:

```sh
python3 GameFixture/calibrate.py /absolute/path/to/capture.pxf /absolute/path/to/layout.json
dotnet run --project MacOS/Probe/Probe.csproj -- \
  --file /absolute/path/to/capture.pxf --layout /absolute/path/to/layout.json
```

Calibration requires exact colors and writes nothing if the known pattern is absent.
The known values come from addon source; they are not learned from the screenshot.
The output layout follows the upstream Frames/Index/X/Y shape. Capture another frame
with the same layout to check consistency. Capture before and after one native Space
press; the penultimate decoded value should increase by one. Also observe normal game
behavior, since a fixture counter alone does not establish gameplay automation.

Use `/pixelproof hide` when finished. This fixture is intentionally independent of
DataToColor, so a pass does not validate that addon's version-specific game APIs.

## Source freshness check

First calibrate in static mode (`/pixelproof reset`). Then run `/pixelproof animate`.
Cell index 17 (zero-based) becomes a 24-bit heartbeat updated about every 100 ms;
the separate Space counter remains at index 18. Do not press Space during this check.

```sh
dotnet run --project MacOS/Verify/Verify.csproj -- \
  /absolute/path/to/endpoint.json WINDOW_ID /absolute/path/to/layout.json \
  GameFixture/expected-values.json 30 --heartbeat-index 17
```

The verifier checks every other cell against its independent static value, requires
at least two heartbeat advances, rejects backward/reset values, and fails if local
observation time exceeds two seconds without an advance (including capture gaps).
Counter wrap is supported. This proves observed changing source pixels within that
bound, not absolute frame age or acceptable combat latency. A capture timeout also
fails. Use `/pixelproof reset` to stop animation and restore the calibration palette.
The updated addon must be installed and reloaded before using this mode.
