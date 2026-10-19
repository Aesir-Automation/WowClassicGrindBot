# MacWowBot — project handoff

Updated October 19, 2026 (America/Los_Angeles).

## Goal and source

Build a Mac-first, pixel-based World of Warcraft platform for rotation assistance,
grinding, eventual questing, and bounded MCP gameplay commands. The user previously
authored profiles for Glider, Honorbuddy, Tinkr and related systems. The older
Blazor Server/WASM/Aimsharp idea is background; Aimsharp is no longer a dependency.

Development continues in https://github.com/Aesir-Automation/WowClassicGrindBot/tree/macos/runtime-foundation
from Xian55/WowClassicGrindBot. MacWowBot is the Ninum project name, not a new code
repository. The fork's dev branch is stale and has intentionally been left alone.
Baseline upstream commit: c5bc4fb614b2054f10b1b377581d39fcb205c2fa.
Published checkpoint before this handoff: 769586f61cdbc64de1fafda92eee7b2882f47c72.
Review upstream licensing before distribution; no root license was identified.

## Architecture and evidence

Swift/macOS ScreenCaptureKit captures a selected window; a bounded PXF1 BGRA frame
protocol carries pixels to a portable .NET 10 runtime. Pixel decoding validates
layout and sentinels. A packaged native permission app and owner-only Unix socket
bridge exist; live socket integration is still unverified because the execution
environment rejected the socket test. Keep fast capture/decision/input loops local;
MCP should request bounded operations and inspect observations.

17 contract/native checks passed. All 16,777,216 opaque RGB values survived the
synthetic Core Graphics-to-.NET path exactly. Real Battle.net and WoW windows were
captured. Native Forever client: arm64, 1.60.1 build 70245, reached login only.
The real WoW PXF capture decoded as 2560x1468, stride 10240, scale 1, 15,032,320
payload bytes; SHA-256 41e5e6813876e052c52285288d46974c9c04651e0a7e16b2052160d062e64e89.
This proves real window capture, not exact addon colors, source freshness or gameplay.
PixelProof addon is installed under _forever_/Interface/AddOns/PixelProof, with
matching source hashes; actual in-world loading remains unverified.

Rebuilding the ad-hoc signed native app can invalidate effective macOS permissions.
Removing stale Settings entries and adding the current bundle restored permission.
CGEvent input is diagnostic only. Its one WoW Space attempt was rejected by the
foreground guard; do not claim successful input. Dummy login text was cleared.

## Hardware decision — paused until delivery

The user purchased a Raspberry Pi Pico and case on October 19. Recommended board:
original RP2040 Pico with headers and USB-A-to-micro-USB data cable, Waveshare
bundle https://www.amazon.com/dp/B08TVF499B ($11.99 observed). Recommended case:
C4Labs Zebra https://www.amazon.com/dp/B08X24QJZC ($10.99 observed).
Confirm actual delivered board and cable before flashing.

The existing Raspberry Pi 3 B+ cannot directly serve as a USB HID gadget through
its external ports and is unnecessary for the chosen design. The connected 256 GB
SanDisk microSD was identified but NEVER erased or flashed. Do not continue its
setup for this project. Imager download was blocked in Edge.

Planned device path: Mac runtime -> USB CDC command interface -> Pico firmware ->
physical USB HID keyboard/mouse -> focused macOS app. Use an honest device identity.
The user requires device-origin input, not CGEvent. This is not an undetectability
guarantee. No Pico firmware or USB transport has yet been implemented or validated.

Optional Xbox-style gamepad: A/B/X/Y, D-pad, sticks, triggers, shoulder/stick buttons,
View/Menu. Generic HID controls are not proof of Xbox identity or macOS/WoW support.
Keep gamepad acceptance separate from keyboard/mouse.

On arrival: identify board, flash firmware, verify enumeration, press/release in a
harmless local app, then watchdog/focus-loss/disconnect/emergency-stop neutralization.
Only then test one attended in-game action and measure latency under capture load.
Use USB HID usages, bounded holds, command IDs/ACKs, explicit release-all, and no
blind retry of input actions. Manual control must remain available.

## Next software milestones

1. Implemented changing PixelProof values and a verifier that rejects frozen source
   pixels even when helper timestamps advance. 24 contract/native checks and four
   synthetic CLI checks pass. Updated addon remains source-only, not in-world proof.
2. Verify exact addon colors and changing values in an authenticated world session.
3. Add persistent capture, measure frame age/latency, and integrate upstream contracts.
4. Implement Pico command transport/firmware and test the delivered hardware.
5. Prove one attended rotation; separately validate navigation/recovery, resumable
   quest state and MCP operations. None of these gameplay milestones is proven yet.

Ninum project: MacWowBot (proj_236518db). Its configured MCP proxy targets
knowledge.varitas.io but failed hostname resolution in this execution environment;
the open Ninum web app created the project instead. During documentation upload,
the web app changed to a sign-in screen. Upload success is NOT confirmed. Restore
Ninum authentication, then upload this handoff to the existing project (do not
create a duplicate). Documentation remains preserved in this repository meanwhile.
