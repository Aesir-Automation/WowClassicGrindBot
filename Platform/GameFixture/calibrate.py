"""Locate PixelProof's exact static palette in a saved PXF1 capture.

No image dependencies; expected values are authored independently in the addon.
Writes a layout only when every expected pixel matches. Does not estimate colors.
"""
import argparse
import json
from pathlib import Path
import struct

parser = argparse.ArgumentParser()
parser.add_argument("capture", type=Path)
parser.add_argument("layout", type=Path)
parser.add_argument("--expected", type=Path, default=Path(__file__).with_name("expected-values.json"))
args = parser.parse_args()
data = args.capture.read_bytes()
if len(data) < 32:
    raise SystemExit("Truncated PXF1 header")
magic, width, height, stride, length, scale, timestamp = struct.unpack("<4sIIIIfQ", data[:32])
if (magic != b"PXF1" or not 0 < width <= 16384 or not 0 < height <= 16384
        or stride < width * 4 or length != stride * height
        or length > 128 * 1024 * 1024 or len(data) != length + 32):
    raise SystemExit("Invalid frame")
expected = json.loads(args.expected.read_text())
if len(expected) < 3 or expected[0] != 0 or expected[-1] != 2000001:
    raise SystemExit("Invalid independent palette")

def bgra(value):
    return bytes((value & 255, value >> 8 & 255, value >> 16 & 255, 255))

colors = [bgra(value) for value in expected]
pixels = memoryview(data)[32:]
for y in range(height):
    row = bytes(pixels[y * stride:y * stride + width * 4])
    start = 0
    while True:
        offset = row.find(colors[1], start)
        if offset < 0:
            break
        start = offset + 4
        if offset % 4:
            continue
        # Match a run of the distinctive second cell, then test equal-width
        # cell centers. Pixel centers tolerate rounding at fractional UI scales.
        end = offset
        while end < width * 4 and row[end:end + 4] == colors[1]:
            end += 4
        start = end
        run = (end - offset) // 4
        if not 4 <= run <= 128:
            continue
        for cell_width in (run - 1, run, run + 1):
            centers = [round(offset / 4 + (i - 1) * cell_width + cell_width / 2)
                       for i in range(len(colors))]
            if all(0 <= x < width and row[x * 4:x * 4 + 4] == color
                   for x, color in zip(centers, colors)):
                layout = {"Frames": [{"Index": i, "X": x, "Y": y}
                                     for i, x in enumerate(centers)]}
                args.layout.write_text(json.dumps(layout, indent=2) + "\n")
                print(json.dumps({"Status": "passed", "Width": width, "Height": height,
                                  "Scale": scale, "CellSpacing": cell_width,
                                  "Layout": str(args.layout), "Source": "independent-static-palette"}))
                raise SystemExit(0)
raise SystemExit("Exact PixelProof palette not found. Check addon loading, counter reset, visibility, and renderer/color settings; no layout was generated.")
