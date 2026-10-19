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
