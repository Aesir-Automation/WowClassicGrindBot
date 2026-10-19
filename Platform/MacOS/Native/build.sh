#!/bin/sh
set -eu
cd "$(dirname "$0")"
mkdir -p .build/module-cache
xcrun swiftc -parse-as-library -O -warnings-as-errors \
  -target "$(uname -m)-apple-macosx14.0" \
  -module-cache-path .build/module-cache \
  -framework AppKit -framework CoreGraphics -framework ScreenCaptureKit \
  -framework ApplicationServices Bridge.swift -o .build/wow-mac-bridge
printf '%s\n' "Built $(pwd)/.build/wow-mac-bridge"
