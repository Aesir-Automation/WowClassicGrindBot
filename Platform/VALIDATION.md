# Validation evidence

Run on an Apple Silicon Mac, October 19, 2026 UTC (October 18 local time).
Swift 6.4, macOS SDK from Apple Command Line Tools, .NET SDK 10.0.401.

## Observed

- Swift helper builds with warnings treated as errors.
- .NET contract suite and repeated-capture verifier build with zero warnings/errors.
- 17 contract/native checks pass.
- Exhaustive synthetic native fixture: 4096 by 4096 opaque sRGB pixels containing
  every 24-bit RGB value. All 16,777,216 values survive the actual Core Graphics
  conversion, PXF1 pipe, and .NET decoder exactly. Mismatches: zero.
- A separate two-row fixture verifies orientation, stride, channel order, scale,
  and upstream-compatible sentinels.
- Repeated-capture CLI smoke checks with synthetic helper wrappers: accepts known
  values across two native processes; rejects a one-unit color error; rejects a
  replayed frame with a frozen helper timestamp. These are not live captures.
- Live verifier with the real helper fails with JSON status `failed`, exit 1,
  and `screen_capture_permission_required`. Invalid 25-bit expectations are
  rejected before capture.
- `doctor` reports `screenCaptureGranted: false`, `accessibilityGranted: false`.
- Battle.net exists in Applications. No WoW app was found in the inspected usual
  application/shared locations or by Spotlight's WoW bundle-identifier query.
  This does not establish that no copy exists elsewhere.
- The computer-control service returned `Transport closed` when opening Battle.net
  and again on an inventory request. No successful game launch was observed.

## What this establishes

The portable frame protocol and native opaque sRGB conversion work together on this
Mac. Pixel encoding has no inherent precision loss in the tested conversion path.
This is useful evidence for feasibility, but does not test the game renderer,
ScreenCaptureKit capture, display profile conversion, HDR, or game key acceptance.
The exhaustive fixture starts with sRGB pixels, not pixels rendered by WoW.

## Remaining proof gates

1. Restore computer control and grant the actual launching process/helper Screen
   Recording and Accessibility through macOS. Confirm identity/attribution rather
   than assuming a permission for another app applies to this helper.
2. Locate/install and launch the native Mac client; record its exact version.
3. Capture an independently known broad color pattern rendered by an addon. Verify
   exact values over repeated captures at the intended resolution, UI scale, display
   profile, and window mode. Test changing values separately to detect stale frames.
4. Post one bounded key to a foreground game window and observe the corresponding
   game/addon state change. An `executed: true` helper response alone is insufficient.
5. Add persistent capture and integrate the upstream game-state/input contracts;
   measure frame age and input-to-observed-state latency under load.
6. Demonstrate one attended rotation before claiming rotation support; validate
   navigation, recovery, and resumable quest state independently afterward.

No live capture, live key response, combat, navigation, questing, or MCP gameplay
success is claimed by this milestone.

## Subsequent live session

- The first packaged native app showed both Screen Recording and Accessibility true.
- ScreenCaptureKit captured the actual Battle.net window twice, including download
  progress advancing from 59% to 80%. The saved PXF1 image was visually inspected.
- The .NET saved-frame probe read the first real capture: width 1600, height 1000,
  stride 6400, point/pixel scale 1, timestamp 57912174171000. This establishes live
  window capture plus file-based transport/decoding; it is not game-pixel proof.
- Added owner-only Unix socket app/server and .NET client. Both compile, but the
  coding environment rejected the socket test with `Operation not permitted`.
  This integration remains unverified.
- A development rebuild invalidated effective app permissions. Toggling Settings
  did not immediately restore the effective grant; the app must be re-authorized.
  The first capture success must not be conflated with current authorization status.
- PixelProof was copied into `_forever_/Interface/AddOns/PixelProof`; SHA-256 hashes
  of both installed files matched the source. Actual in-game addon loading is unverified.
- Synthetic PixelProof calibration produced coordinates whose .NET-decoded values
  matched the independent palette. Removing the distinctive exact color caused
  rejection with no layout output. This does not test the game's renderer.
- Launched the native arm64 Forever stress-test client, version 1.60.1 build 70245.
  Reached the login screen. No authenticated character/world session was observed.
- System disk temporarily reached 100% with 130 MiB free. Removed only this task's
  248 MiB Swift module cache and duplicate PNG previews. Later observed 34 GiB free;
  no unrelated files were deleted by this task. Installer pause was attempted but
  not verified because launcher window control failed.

Still unproven: exact in-game palette capture, native key response, live IPC,
rotation timing, upstream Core integration, navigation, questing, and MCP gameplay.

### Native game-window capture

After removing the stale Screen Recording entry and adding the current app bundle,
the app reported effective Screen Recording true again. Captured the actual WoW
window (ID 1062) through ScreenCaptureKit and visually verified the rendered UI.
The .NET saved-frame probe read width 2560, height 1468, stride 10240, scale 1,
and timestamp 59478222854833. Payload: 15,032,320 bytes.
PXF SHA-256: `41e5e6813876e052c52285288d46974c9c04651e0a7e16b2052160d062e64e89`.
This establishes capture of the real game renderer and saved-frame .NET transport.
It does not establish exact addon colors, source-frame freshness, or input acceptance.
Accessibility for the rebuilt app was still false at the last app status check.
The game remains at account login; no credentials were entered by this task.

### Input requirement update

Removed the stale Accessibility entry and added the current app bundle; the app
then reported both permissions true. A single delayed native Space attempt targeting
WoW was rejected by the foreground guard (`target_not_foreground`). No native key
acceptance was demonstrated. CUA prepared dummy text `pixelproof` in an empty login
field; no account credentials or login submission were made by this task.

The user subsequently required device-origin input. CGEvent is retained only as a
diagnostic backend. INPUT-DESIGN.md records the physical USB HID direction; it is a
design decision, not a completed hardware implementation or a detection guarantee.

The dummy login text was subsequently cleared and the empty field visually verified.
The optional Xbox-style gamepad backend is recorded as a requirement/design only.
No controller hardware or virtual gamepad implementation has been validated.
