import AppKit
import ApplicationServices
import Darwin
import Foundation
import ScreenCaptureKit

// Owner-only local IPC. The authorized app performs capture/input itself; clients
// do not inherit or bypass its macOS permissions. One bounded request per connection.
final class AppBridgeServer {
    private let listener: Int32
    private let directory: URL

    init(endpointFile: URL) throws {
        directory = URL(fileURLWithPath: "/tmp/wmb-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: false,
            attributes: [.posixPermissions: 0o700])
        let path = directory.appendingPathComponent("bridge.sock").path
        listener = Darwin.socket(AF_UNIX, SOCK_STREAM, 0)
        guard listener >= 0 else { throw BridgeFailure("socket creation failed") }
        var address = sockaddr_un()
        address.sun_family = sa_family_t(AF_UNIX)
        address.sun_len = UInt8(MemoryLayout<sockaddr_un>.size)
        guard path.utf8.count < MemoryLayout.size(ofValue: address.sun_path) else {
            Darwin.close(listener)
            throw BridgeFailure("socket path too long")
        }
        _ = withUnsafeMutablePointer(to: &address.sun_path) { pointer in
            pointer.withMemoryRebound(to: CChar.self, capacity: 104) { buffer in path.withCString { source in strcpy(buffer, source) } }
        }
        let bound = withUnsafePointer(to: &address) { pointer in
            pointer.withMemoryRebound(to: sockaddr.self, capacity: 1) {
                Darwin.bind(listener, $0, socklen_t(MemoryLayout<sockaddr_un>.size))
            }
        }
        guard bound == 0, Darwin.listen(listener, 4) == 0 else {
            Darwin.close(listener)
            throw BridgeFailure("socket bind/listen failed")
        }
        chmod(path, 0o600)
        let info: [String: Any] = ["protocol": "WMB1", "socketPath": path,
            "processId": ProcessInfo.processInfo.processIdentifier]
        try JSONSerialization.data(withJSONObject: info, options: [.sortedKeys])
            .write(to: endpointFile, options: .atomic)
    }

    func start() {
        DispatchQueue.global(qos: .userInitiated).async { [self] in
            while true {
                let client = Darwin.accept(listener, nil, nil)
                if client < 0 { return }
                let handle = FileHandle(fileDescriptor: client, closeOnDealloc: true)
                var duration = timeval(tv_sec: 15, tv_usec: 0)
                var noPipe: Int32 = 1
                setsockopt(client, SOL_SOCKET, SO_RCVTIMEO, &duration, socklen_t(MemoryLayout<timeval>.size))
                setsockopt(client, SOL_SOCKET, SO_SNDTIMEO, &duration, socklen_t(MemoryLayout<timeval>.size))
                setsockopt(client, SOL_SOCKET, SO_NOSIGPIPE, &noPipe, socklen_t(MemoryLayout<Int32>.size))
                do {
                    var line = Data()
                    while true {
                        guard let byte = try handle.read(upToCount: 1), !byte.isEmpty else {
                            throw BridgeFailure("request ended before newline")
                        }
                        if byte[0] == 10 { break }
                        line.append(byte)
                        if line.count > 4096 { throw BridgeFailure("request exceeds 4096 bytes") }
                    }
                    guard let args = try JSONSerialization.jsonObject(with: line) as? [String],
                          let command = args.first, ["doctor", "windows", "capture", "key"].contains(command) else {
                        throw BridgeFailure("expected argument array for doctor/windows/capture/key")
                    }
                    let completed = DispatchSemaphore(value: 0)
                    Task { @MainActor in
                        let response: Data
                        do { response = try await Self.dispatch(args) }
                        catch { response = Self.errorResponse(error) }
                        DispatchQueue.global(qos: .userInitiated).async {
                            try? handle.write(contentsOf: response)
                            try? handle.close()
                            completed.signal()
                        }
                    }
                    completed.wait()
                } catch {
                    try? handle.write(contentsOf: Self.errorResponse(error))
                    try? handle.close()
                }
            }
        }
    }

    private static func header(_ object: [String: Any]) throws -> Data {
        var data = try JSONSerialization.data(withJSONObject: object, options: [.sortedKeys])
        data.append(10)
        return data
    }

    private static func errorResponse(_ error: Error) -> Data {
        (try? header(["ok": false, "error": String(describing: error)])) ?? Data()
    }

    @MainActor
    private static func dispatch(_ input: [String]) async throws -> Data {
        let args = try Arguments(input)
        switch args.command {
        case "capture":
            try args.validate(allowed: ["--window-id", "--region"])
            let frame = try await MacBridge.captureData(args)
            var response = try header(["ok": true, "format": "PXF1"])
            response.append(frame)
            return response
        case "key":
            try args.validate(allowed: ["--pid", "--key-code", "--milliseconds"], allowExecute: true)
            try MacBridge.pressKey(args)
            return try header(["ok": true, "executed": args.execute])
        case "doctor":
            try args.validate(allowed: [])
            return try header(["ok": true, "screenCaptureGranted": CGPreflightScreenCaptureAccess(),
                "accessibilityGranted": AXIsProcessTrusted()])
        case "windows":
            try args.validate(allowed: [])
            let content = try await MacBridge.shareableContent()
            let windows: [[String: Any]] = content.windows.filter { $0.windowLayer == 0 && $0.owningApplication != nil }.map {
                ["windowId": $0.windowID, "processId": $0.owningApplication!.processID,
                 "application": $0.owningApplication!.applicationName, "title": $0.title ?? ""]
            }
            return try header(["ok": true, "windows": windows])
        default:
            throw BridgeFailure("unsupported local command")
        }
    }
}
