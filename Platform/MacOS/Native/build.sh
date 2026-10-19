#!/bin/sh
set -eu
cd "$(dirname "$0")"
mkdir -p .build/module-cache .build/WowMacBridge.app/Contents/MacOS
xcrun swiftc -parse-as-library -O -warnings-as-errors \
  -target "$(uname -m)-apple-macosx14.0" \
  -module-cache-path .build/module-cache \
  -framework AppKit -framework CoreGraphics -framework ScreenCaptureKit \
  -framework ApplicationServices Bridge.swift PermissionWindow.swift AppBridgeServer.swift -o .build/WowMacBridge.app/Contents/MacOS/wow-mac-bridge
cat > .build/WowMacBridge.app/Contents/Info.plist <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleIdentifier</key><string>io.aesir.wow-mac-bridge</string>
<key>CFBundleName</key><string>WoW Mac Bridge</string>
<key>CFBundleExecutable</key><string>wow-mac-bridge</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleVersion</key><string>1</string>
<key>CFBundleShortVersionString</key><string>0.1.0</string>
<key>LSMinimumSystemVersion</key><string>14.0</string>
<key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST
codesign --force --sign - --identifier io.aesir.wow-mac-bridge --timestamp=none .build/WowMacBridge.app
ln -sf WowMacBridge.app/Contents/MacOS/wow-mac-bridge .build/wow-mac-bridge
printf '%s\n'  "Built $(pwd)/.build/wow-mac-bridge"
