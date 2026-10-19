import AppKit
import ApplicationServices
import CoreGraphics
import Foundation
import ScreenCaptureKit

struct BridgeFailure: Error, CustomStringConvertible {
    let description: String
    init(_ description: String) { self.description = description }
}

struct Arguments {
    let command: String
    var values: [String: String] = [:]
    var execute = false

    init(_ input: [String]) throws {
        guard let first = input.first else { throw BridgeFailure("Expected doctor, windows, capture, key, or request-screen-permission.") }
        command = first
        var index = 1
        while index < input.count {
            let flag = input[index]
            if flag == "--execute" {
                guard !execute else { throw BridgeFailure("Duplicate --execute.") }
                execute = true
                index += 1
            } else {
                guard flag.hasPrefix("--"), index + 1 < input.count, values[flag] == nil else {
                    throw BridgeFailure("Invalid or duplicate argument: \(flag)")
                }
                values[flag] = input[index + 1]
                index += 2
            }
        }
    }

    func validate(allowed: Set<String>, allowExecute: Bool = false) throws {
        guard Set(values.keys).isSubset(of: allowed), allowExecute || !execute else {
            throw BridgeFailure("Unsupported option for \(command).")
        }
    }

    func integer(_ name: String) throws -> Int {
        guard let raw = values[name], let value = Int(raw) else { throw BridgeFailure("Expected integer \(name).") }
        return value
    }
}

@main
struct MacBridge {
    @MainActor
    static func main() async {
        do {
            let args = try Arguments(Array(CommandLine.arguments.dropFirst()))
            switch args.command {
            case "doctor":
                try args.validate(allowed: [])
                try emitJSON([
                    "protocol": "PXF1",
                    "screenCaptureGranted": CGPreflightScreenCaptureAccess(),
                    "accessibilityGranted": AXIsProcessTrusted(),
                    "minimumMacOS": "14.0",
                    "captureMode": "single-window-snapshot",
                    "inputMode": "foreground-only-explicit-execute"
                ])
            case "request-screen-permission":
                try args.validate(allowed: [])
                try emitJSON(["granted": CGRequestScreenCaptureAccess()])
            case "windows":
                try args.validate(allowed: [])
                let content = try await shareableContent()
                let windows: [[String: Any]] = content.windows
                    .filter { $0.windowLayer == 0 && $0.owningApplication != nil }
                    .map { window in
                        ["windowId": window.windowID,
                         "processId": window.owningApplication!.processID,
                         "application": window.owningApplication!.applicationName,
                         "title": window.title ?? "",
                         "widthPoints": window.frame.width,
                         "heightPoints": window.frame.height]
                    }
                try emitJSON(windows)
            case "capture":
                try args.validate(allowed: ["--window-id", "--region"])
                try await capture(args)
            case "fixture":
                try args.validate(allowed: [])
                try fixture()
            case "key":
                try args.validate(allowed: ["--pid", "--key-code", "--milliseconds"], allowExecute: true)
                try pressKey(args)
            default:
                throw BridgeFailure("Unknown command: \(args.command)")
            }
        } catch {
            FileHandle.standardError.write(Data("\(error)\n".utf8))
            exit(1)
        }
    }

    static func emitJSON(_ value: Any) throws {
        let data = try JSONSerialization.data(withJSONObject: value, options: [.sortedKeys])
        FileHandle.standardOutput.write(data)
        FileHandle.standardOutput.write(Data([10]))
    }

    static func shareableContent() async throws -> SCShareableContent {
        guard CGPreflightScreenCaptureAccess() else {
            throw BridgeFailure("screen_capture_permission_required: grant Screen Recording to the helper/launching terminal, then restart. Permission is never requested implicitly.")
        }
        return try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)
    }

    static func capture(_ args: Arguments) async throws {
        let id = try args.integer("--window-id")
        guard id > 0, id <= Int(UInt32.max) else { throw BridgeFailure("Invalid window ID.") }
        let content = try await shareableContent()
        guard let window = content.windows.first(where: { $0.windowID == UInt32(id) }) else {
            throw BridgeFailure("window_not_found: refresh the window list; minimized/offscreen windows are excluded.")
        }

        let filter = SCContentFilter(desktopIndependentWindow: window)
        let configuration = SCStreamConfiguration()
        let scale = filter.pointPixelScale
        let width = Int((filter.contentRect.width * CGFloat(scale)).rounded())
        let height = Int((filter.contentRect.height * CGFloat(scale)).rounded())
        guard width > 0, height > 0, width <= 16384, height <= 16384,
              width * height * 4 <= 128 * 1024 * 1024 else {
            throw BridgeFailure("Window exceeds the 128 MiB capture limit.")
        }
        configuration.width = width
        configuration.height = height
        configuration.showsCursor = false
        configuration.ignoreShadowsSingleWindow = true
        configuration.captureResolution = .best

        let full = try await SCScreenshotManager.captureImage(contentFilter: filter, configuration: configuration)
        let image: CGImage
        if let raw = args.values["--region"] {
            let parts = raw.split(separator: ",", omittingEmptySubsequences: false)
            let numbers = parts.compactMap { Int($0) }
            guard parts.count == 4, numbers.count == 4,
                  numbers[0] >= 0, numbers[1] >= 0, numbers[2] > 0, numbers[3] > 0,
                  numbers[2] <= full.width, numbers[3] <= full.height,
                  numbers[0] <= full.width - numbers[2], numbers[1] <= full.height - numbers[3] else {
                throw BridgeFailure("Invalid --region. Use x,y,width,height in physical pixels of the full captured window.")
            }
            guard let cropped = full.cropping(to: CGRect(x: numbers[0], y: numbers[1], width: numbers[2], height: numbers[3])) else {
                throw BridgeFailure("Could not crop capture.")
            }
            image = cropped
        } else {
            image = full
        }

        try emitFrame(image, scale: scale)
    }

    // Runs the real BGRA conversion/framing path without screen or input access.
    static func fixture() throws {
        let bytes = Data([
            0, 0, 0, 255, 0x56, 0x34, 0x12, 255, 0x81, 0x84, 0x1e, 255,
            0xef, 0xcd, 0xab, 255, 0, 0, 0, 255, 0, 0, 0, 255
        ])
        guard let provider = CGDataProvider(data: bytes as CFData),
              let space = CGColorSpace(name: CGColorSpace.sRGB),
              let image = CGImage(width: 3, height: 2, bitsPerComponent: 8, bitsPerPixel: 32,
                bytesPerRow: 12, space: space,
                bitmapInfo: CGBitmapInfo(rawValue: CGBitmapInfo.byteOrder32Little.rawValue | CGImageAlphaInfo.premultipliedFirst.rawValue),
                provider: provider, decode: nil, shouldInterpolate: false, intent: .defaultIntent) else {
            throw BridgeFailure("Could not create fixture.")
        }
        try emitFrame(image, scale: 2)
    }

    static func emitFrame(_ image: CGImage, scale: Float) throws {
        let stride = image.width * 4
        var pixels = Data(count: stride * image.height)
        let converted = pixels.withUnsafeMutableBytes { buffer -> Bool in
            guard let address = buffer.baseAddress,
                  let colorSpace = CGColorSpace(name: CGColorSpace.sRGB),
                  let context = CGContext(data: address, width: image.width, height: image.height,
                    bitsPerComponent: 8, bytesPerRow: stride, space: colorSpace,
                    bitmapInfo: CGBitmapInfo.byteOrder32Little.rawValue | CGImageAlphaInfo.premultipliedFirst.rawValue) else {
                return false
            }
            context.interpolationQuality = .none
            context.draw(image, in: CGRect(x: 0, y: 0, width: image.width, height: image.height))
            return true
        }
        guard converted else { throw BridgeFailure("Could not convert capture to BGRA8.") }

        var header = Data("PXF1".utf8)
        append(UInt32(image.width), to: &header)
        append(UInt32(image.height), to: &header)
        append(UInt32(stride), to: &header)
        append(UInt32(pixels.count), to: &header)
        append(scale.bitPattern, to: &header)
        append(DispatchTime.now().uptimeNanoseconds, to: &header)
        FileHandle.standardOutput.write(header)
        FileHandle.standardOutput.write(pixels)
    }

    static func append<T: FixedWidthInteger>(_ value: T, to data: inout Data) {
        var littleEndian = value.littleEndian
        withUnsafeBytes(of: &littleEndian) { data.append(contentsOf: $0) }
    }

    @MainActor
    static func pressKey(_ args: Arguments) throws {
        let pid = try args.integer("--pid")
        let code = try args.integer("--key-code")
        let duration = try args.integer("--milliseconds")
        guard pid > 0, pid <= Int(Int32.max), (0...127).contains(code), (1...2000).contains(duration) else {
            throw BridgeFailure("Invalid PID, Mac virtual key code, or hold duration (1...2000 ms).")
        }
        if !args.execute {
            try emitJSON(["executed": false, "processId": pid, "keyCode": code, "durationMs": duration])
            return
        }
        guard AXIsProcessTrusted() else { throw BridgeFailure("accessibility_permission_required") }
        guard NSWorkspace.shared.frontmostApplication?.processIdentifier == pid_t(pid) else {
            throw BridgeFailure("target_not_foreground: bring the selected application to the front.")
        }
        guard let down = CGEvent(keyboardEventSource: nil, virtualKey: CGKeyCode(code), keyDown: true),
              let up = CGEvent(keyboardEventSource: nil, virtualKey: CGKeyCode(code), keyDown: false) else {
            throw BridgeFailure("Could not construct keyboard events.")
        }
        down.postToPid(pid_t(pid))
        defer { up.postToPid(pid_t(pid)) }
        Thread.sleep(forTimeInterval: Double(duration) / 1000.0)
        try emitJSON(["executed": true, "processId": pid, "keyCode": code, "durationMs": duration])
    }
}
