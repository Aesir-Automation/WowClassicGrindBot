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
