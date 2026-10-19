import AppKit
import ApplicationServices
import Foundation
import ScreenCaptureKit

// Keep TCC permission requests and live probes in the app process that owns them.
@MainActor
final class PermissionWindow: NSObject, NSApplicationDelegate {
    private var window: NSWindow!
    private let status = NSTextField(wrappingLabelWithString: "")
    private let picker = NSPopUpButton()
    private var windows: [SCWindow] = []
    private var server: AppBridgeServer?
    private var selected: SCWindow? {
        let index = picker.indexOfSelectedItem
        return windows.indices.contains(index) ? windows[index] : nil
    }
    private let evidenceDirectory: URL

    override init() {
        // A stable, visible location alongside the generated app, inside .build.
        evidenceDirectory = Bundle.main.bundleURL.deletingLastPathComponent()
            .appendingPathComponent("live-evidence", isDirectory: true)
        super.init()
    }

    static func run() {
        let application = NSApplication.shared
        let delegate = PermissionWindow()
        application.delegate = delegate
        application.setActivationPolicy(.regular)
        withExtendedLifetime(delegate) { application.run() }
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 700, height: 410),
            styleMask: [.titled, .closable, .miniaturizable], backing: .buffered, defer: false)
        window.title = "WoW Mac Bridge — Live Probe"
        let stack = NSStackView()
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 12
        stack.translatesAutoresizingMaskIntoConstraints = false
        let title = NSTextField(labelWithString: "Native capture and input verification")
        title.font = .boldSystemFont(ofSize: 19)
        stack.addArrangedSubview(title)
        stack.addArrangedSubview(NSTextField(wrappingLabelWithString:
            "Choose only the intended test/game window. Capture saves pixels locally. Space sends one 50 ms press after a 5 second delay, only if the selected app is foreground."))
        let permissions = NSStackView(views: [button("Screen Recording…", #selector(requestScreen)),
            button("Accessibility…", #selector(requestAccessibility)), button("Check permissions", #selector(checkPermissions))])
        stack.addArrangedSubview(permissions)
        picker.translatesAutoresizingMaskIntoConstraints = false
        picker.widthAnchor.constraint(equalToConstant: 640).isActive = true
        stack.addArrangedSubview(picker)
        stack.addArrangedSubview(NSStackView(views: [button("Refresh windows", #selector(refreshWindows)),
            button("Capture selected", #selector(captureSelected)), button("Space in 5 seconds", #selector(spaceSelected))]))
        status.font = .monospacedSystemFont(ofSize: 11, weight: .regular)
        status.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        stack.addArrangedSubview(status)
        window.contentView!.addSubview(stack)
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: window.contentView!.leadingAnchor, constant: 24),
            stack.trailingAnchor.constraint(equalTo: window.contentView!.trailingAnchor, constant: -24),
            stack.topAnchor.constraint(equalTo: window.contentView!.topAnchor, constant: 24)
        ])
        do {
            try FileManager.default.createDirectory(at: evidenceDirectory, withIntermediateDirectories: true)
            server = try AppBridgeServer(endpointFile: evidenceDirectory.appendingPathComponent("endpoint.json"))
            server!.start()
        } catch { status.stringValue = "Local bridge failed: \(error)" }
        checkPermissions()
        window.center()
        window.makeKeyAndOrderFront(nil)
        NSApplication.shared.activate(ignoringOtherApps: true)
    }

    private func button(_ title: String, _ action: Selector) -> NSButton {
        NSButton(title: title, target: self, action: action)
    }

    @objc private func checkPermissions() {
        status.stringValue = "Screen Recording: \(CGPreflightScreenCaptureAccess())\nAccessibility: \(AXIsProcessTrusted())\nEvidence: \(evidenceDirectory.path)"
    }

    @objc private func requestScreen() {
        _ = CGRequestScreenCaptureAccess()
        checkPermissions()
    }

    @objc private func requestAccessibility() {
        let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
        _ = AXIsProcessTrustedWithOptions(options)
        checkPermissions()
    }

    @objc private func refreshWindows() {
        Task { @MainActor in
            do {
                let content = try await MacBridge.shareableContent()
                windows = content.windows.filter { $0.windowLayer == 0 && $0.owningApplication != nil && $0.owningApplication!.processID != ProcessInfo.processInfo.processIdentifier }
                picker.removeAllItems()
                for window in windows {
                    picker.addItem(withTitle: "\(window.owningApplication!.applicationName): \(window.title ?? "") [\(window.windowID)]")
                }
                status.stringValue = "Found \(windows.count) windows. Select the intended game/test window."
            } catch { status.stringValue = String(describing: error) }
        }
    }

    @objc private func captureSelected() {
        guard let target = selected else { status.stringValue = "Select a window first."; return }
        Task { @MainActor in
            do {
                try FileManager.default.createDirectory(at: evidenceDirectory, withIntermediateDirectories: true)
                let name = "capture-\(DispatchTime.now().uptimeNanoseconds).pxf"
                let destination = evidenceDirectory.appendingPathComponent(name)
                let args = try Arguments(["capture", "--window-id", String(target.windowID)])
                try await MacBridge.capture(args, destination: destination)
                status.stringValue = "Captured window \(target.windowID)\n\(destination.path)"
            } catch { status.stringValue = String(describing: error) }
        }
    }

    @objc private func spaceSelected() {
        guard let target = selected, let application = target.owningApplication else {
            status.stringValue = "Select a window first."; return
        }
        let pid = application.processID
        status.stringValue = "Bring \(application.applicationName) to the front now. One Space press in 5 seconds."
        Task { @MainActor in
            do {
                try await Task.sleep(for: .seconds(5))
                let args = try Arguments(["key", "--pid", String(pid), "--key-code", "49", "--milliseconds", "50", "--execute"])
                try MacBridge.pressKey(args)
                status.stringValue = "Space posted to PID \(pid). Verify its effect in the game; dispatch alone is not input proof."
            } catch { status.stringValue = String(describing: error) }
        }
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
}
