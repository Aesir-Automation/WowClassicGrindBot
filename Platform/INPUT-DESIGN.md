# Device-origin input requirement

The user requires input to originate through a device input path. The existing
CGEvent implementation is diagnostic software injection and does not satisfy this
requirement. No gameplay or undetectability claim follows from its tests.

## Hardware direction accepted; Pico purchased

On October 19, the user purchased a Pico and case; hardware work is paused until
delivery. The identified existing Pi 3 B+ cannot serve as the USB HID gadget through
its external ports. Its 256 GB SanDisk card was not erased and is not needed.
Confirm the delivered Pico model before selecting/flashing firmware. See
PROJECT-STATUS.md for purchase links, handoff, and remaining proof gates.

The preferred candidate is a physical USB HID keyboard/mouse controller driven by
the local Mac runtime. The user has confirmed that an external device is acceptable.
The controller presents its own honest device identity and standard HID reports.
The runtime sends commands over a separate control interface (for example USB CDC),
while macOS receives keyboard/mouse reports through its ordinary device path.
Hardware/firmware selection remains open until available equipment is identified.
No controller has been programmed or validated in this milestone.

Mac runtime -> controller command channel -> physical USB HID keyboard/mouse -> macOS -> game.

A virtual HID DriverKit device is an alternative software implementation, but is
still a virtual device and introduces system-extension/signing/entitlement work.
It does not establish that input is indistinguishable from physical hardware.
Physical HID also cannot establish that an automated gameplay pattern is undetectable.
The engineering acceptance criterion is correct device-origin reports and observed
client behavior, not a claim about a third party's detection.

## Required contract changes before integration

The current IKeyboardInput API uses process IDs and Mac virtual key codes; it is
specific to the diagnostic bridge. A physical HID adapter needs USB HID usages and
modifier/button state, not PID-targeted injection. Device input routes to the focused
application, so foreground checks and input ownership remain necessary.

- Maintain an explicit state of held keys/buttons and separate down/up transitions.
- Give commands sequence IDs and acknowledgments; never blindly retry actions.
- Bound holds in controller firmware. Release all keys/buttons on watchdog expiry,
  disconnect, session end, or an explicit emergency stop.
- Keep manual keyboard/mouse control available. Record only automation-owned state.
- Keep the control channel owner-only and local; never expose raw input on a network.
- Put fast decisions and device timing in the local runtime, with MCP issuing bounded
  task commands and reading observations.

## Proof gates

1. Identify a controller with a real USB device controller and the required control
   channel; confirm Mac enumeration of its keyboard/mouse interfaces.
2. Capture reports and validate down/up, modifiers, mouse axes/buttons and release-all.
3. Verify one local text-field key action from hardware, then one in-game action.
4. Verify focus loss, timeout, disconnect and emergency release while a key is held.
5. Measure command-to-visible-state latency and jitter under capture load.
6. Validate an attended rotation independently from the HID transport.

References:
- TinyUSB HID report APIs: https://docs.tinyusb.org/en/latest/reference/class/hid.html
- Apple's virtual HID entitlement: https://developer.apple.com/documentation/bundleresources/entitlements/com.apple.developer.driverkit.family.hid.virtual.device

## Optional Xbox-style controller backend

User-requested optional mode: Xbox-style face buttons A/B/X/Y, D-pad, two analog
sticks, independent analog triggers, shoulder buttons, stick clicks, View and Menu.
Represent a full controller state with explicit neutral/reset behavior, not keyboard
keys renamed as controller buttons. Keep this separate from the keyboard/mouse backend.

The candidate hardware route is a physical USB HID gamepad implemented with a device
stack such as TinyUSB, controlled through the local command channel. TinyUSB exposes
keyboard, mouse and gamepad report helpers. A generic HID gamepad with Xbox-style
controls is not proof of an exact Xbox controller identity/protocol, nor proof that
Apple GameController or the Forever client will recognize that custom descriptor.
Both layers need enumeration and live input tests.

Apple documents physical Xbox controller support. Microsoft's XInput documentation
is for Windows/Xbox; it is not the native macOS injection API. Do not carry over a
Windows virtual-Xbox driver design and assume it will run on Mac.

Additional acceptance gates:
- macOS enumerates the device as a game controller, with the expected element ranges.
- WoW Forever sees the controller and its native gamepad mode accepts it.
- Verify each axis, trigger, D-pad direction and button independently.
- Verify neutral sticks/triggers and released buttons on timeout/disconnect.
- Measure dead zones and command-to-visible-state latency before navigation work.

No physical or virtual gamepad backend is implemented or proven yet.

Additional references:
- https://support.apple.com/en-us/111101
- https://developer.apple.com/documentation/gamecontroller/discovering-game-controllers
- https://learn.microsoft.com/en-us/windows/win32/xinput/

## Initial hardware recommendation

Start with an original Raspberry Pi Pico (RP2040) and a USB data cable. TinyUSB lists
this board and provides a composite HID keyboard/mouse/gamepad example. The board
has USB device support and USB drag-and-drop programming. This is a starting-board
recommendation, not a claim that its stock firmware implements our command channel.
If an appropriate programmable board is already available, identify its exact model
before buying another or committing to its firmware target.

The intended prototype combines a local control interface with keyboard/mouse HID
and an optional gamepad profile. No soldering is expected for the initial USB-only
prototype. A normal retail Xbox controller can help establish baseline game mappings,
but merely connecting it does not supply a programmable device-output bridge.

The first hardware milestone is enumeration, a press/release, and verified watchdog
neutralization. Exact Xbox-compatible recognition remains a separate proof gate.

Board references:
- https://www.raspberrypi.com/products/raspberry-pi-pico/
- https://docs.tinyusb.org/en/latest/reference/boards.html
- https://docs.tinyusb.org/en/latest/examples/device/hid_composite.html
