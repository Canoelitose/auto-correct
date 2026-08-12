#!/usr/bin/env python3
"""Generates src/AutoCorrect.App/Assets/tray.ico without any image library.

The icon is a rounded blue square with a white check mark, rendered with 4x
supersampling. Sizes up to 64 px are written as classic BMP entries so that
LoadImage and ExtractIconEx can read them everywhere; 128 and 256 px are stored
as PNG, which is the usual encoding for large icon entries and keeps the file small.

Usage: python3 build/make-icon.py
"""

from __future__ import annotations

import struct
import zlib
from pathlib import Path

SIZES = (16, 20, 24, 32, 48, 64, 128, 256)
SUPERSAMPLE = 4

BACKGROUND = (0x1F, 0x6F, 0xEB)  # RGB, matches the accent colour of the popup
FOREGROUND = (0xFF, 0xFF, 0xFF)


def rounded_rect_alpha(x: float, y: float, size: float, radius: float) -> float:
    """1.0 inside the rounded rectangle, 0.0 outside."""
    half = size / 2.0
    dx = abs(x - half) - (half - radius)
    dy = abs(y - half) - (half - radius)
    dx = max(dx, 0.0)
    dy = max(dy, 0.0)
    return 1.0 if (dx * dx + dy * dy) <= radius * radius else 0.0


def distance_to_segment(px: float, py: float, ax: float, ay: float, bx: float, by: float) -> float:
    vx, vy = bx - ax, by - ay
    wx, wy = px - ax, py - ay
    length_squared = vx * vx + vy * vy
    t = 0.0 if length_squared == 0 else max(0.0, min(1.0, (wx * vx + wy * vy) / length_squared))
    cx, cy = ax + t * vx, ay + t * vy
    return ((px - cx) ** 2 + (py - cy) ** 2) ** 0.5


def render(size: int) -> bytes:
    """Returns BGRA rows, top row first."""
    hi = size * SUPERSAMPLE
    radius = hi * 0.22
    stroke = hi * 0.10

    # Check mark, in units of the icon size.
    ax, ay = 0.26 * hi, 0.53 * hi
    bx, by = 0.43 * hi, 0.70 * hi
    cx, cy = 0.76 * hi, 0.32 * hi

    samples = []
    for y in range(hi):
        row = []
        for x in range(hi):
            px, py = x + 0.5, y + 0.5
            background = rounded_rect_alpha(px, py, hi, radius)
            if background == 0.0:
                row.append(None)
                continue

            on_stroke = (
                distance_to_segment(px, py, ax, ay, bx, by) <= stroke / 2.0
                or distance_to_segment(px, py, bx, by, cx, cy) <= stroke / 2.0
            )
            row.append(FOREGROUND if on_stroke else BACKGROUND)
        samples.append(row)

    # Box downsample to the target size, averaging colour and coverage.
    pixels = bytearray()
    for y in range(size):
        for x in range(size):
            r = g = b = 0
            covered = 0
            for sy in range(SUPERSAMPLE):
                for sx in range(SUPERSAMPLE):
                    sample = samples[y * SUPERSAMPLE + sy][x * SUPERSAMPLE + sx]
                    if sample is None:
                        continue
                    covered += 1
                    r += sample[0]
                    g += sample[1]
                    b += sample[2]

            total = SUPERSAMPLE * SUPERSAMPLE
            if covered == 0:
                pixels += bytes((0, 0, 0, 0))
                continue

            alpha = round(255 * covered / total)
            pixels += bytes((b // covered, g // covered, r // covered, alpha))

    return bytes(pixels)


PNG_FROM_SIZE = 128


def build_png(size: int, top_down: bytes) -> bytes:
    """Minimal RGBA PNG encoder; icon entries of 128 px and larger use this."""
    raw = bytearray()
    for y in range(size):
        raw.append(0)  # filter type "none"
        row = top_down[y * size * 4:(y + 1) * size * 4]
        for x in range(0, len(row), 4):
            b, g, r, a = row[x:x + 4]
            raw += bytes((r, g, b, a))

    def chunk(tag: bytes, payload: bytes) -> bytes:
        return (struct.pack(">I", len(payload)) + tag + payload
                + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF))

    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(bytes(raw), 9))
            + chunk(b"IEND", b""))


def build_image(size: int) -> bytes:
    """BITMAPINFOHEADER + bottom-up BGRA bitmap + AND mask."""
    top_down = render(size)
    if size >= PNG_FROM_SIZE:
        return build_png(size, top_down)

    row_bytes = size * 4

    bottom_up = bytearray()
    for y in range(size - 1, -1, -1):
        bottom_up += top_down[y * row_bytes:(y + 1) * row_bytes]

    # AND mask: one bit per pixel, rows padded to 4 bytes. Fully transparent with a
    # 32 bpp image, but the structure is still required.
    mask_row = ((size + 31) // 32) * 4
    and_mask = bytes(mask_row * size)

    header = struct.pack(
        "<IiiHHIIiiII",
        40,           # biSize
        size,         # biWidth
        size * 2,     # biHeight, image plus mask
        1,            # biPlanes
        32,           # biBitCount
        0,            # biCompression = BI_RGB
        len(bottom_up) + len(and_mask),
        0, 0, 0, 0,
    )

    return header + bytes(bottom_up) + and_mask


def main() -> None:
    images = [(size, build_image(size)) for size in SIZES]

    header = struct.pack("<HHH", 0, 1, len(images))
    offset = len(header) + 16 * len(images)

    directory = b""
    payload = b""
    for size, image in images:
        directory += struct.pack(
            "<BBBBHHII",
            0 if size >= 256 else size,
            0 if size >= 256 else size,
            0,      # palette colours
            0,      # reserved
            1,      # colour planes
            32,     # bits per pixel
            len(image),
            offset,
        )
        payload += image
        offset += len(image)

    target = Path(__file__).resolve().parents[1] / "src" / "AutoCorrect.App" / "Assets" / "tray.ico"
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(header + directory + payload)
    print(f"wrote {target} ({len(header + directory + payload)} bytes, sizes: {', '.join(str(s) for s in SIZES)})")


if __name__ == "__main__":
    main()
