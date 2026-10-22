"""Create original Switchit pixel artwork using only the Python standard library."""
from pathlib import Path
import struct
import zlib

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "assets"
OUT.mkdir(exist_ok=True)
SIZE = 32
pixels = [[(0, 0, 0, 0) for _ in range(SIZE)] for _ in range(SIZE)]


def color(value):
    value = value.lstrip("#")
    return tuple(int(value[i:i + 2], 16) for i in (0, 2, 4)) + (255,)


def rect(x0, y0, x1, y1, fill):
    rgba = color(fill)
    for y in range(y0, y1):
        for x in range(x0, x1):
            pixels[y][x] = rgba


ink = "#332D29"
cream = "#E8DBB9"
shade = "#B9A587"
paper = "#F5E9CA"
coral = "#D77D5B"
red = "#A54B40"
screen = "#344A40"
mint = "#C8D5A3"

# The footprint, stepped lid, warm metal housing, and two little feet.
rect(5, 29, 10, 31, ink)
rect(22, 29, 27, 31, ink)
rect(2, 15, 30, 29, ink)
rect(4, 12, 28, 15, ink)
rect(6, 10, 26, 12, ink)
rect(4, 15, 28, 27, cream)
rect(5, 13, 27, 15, paper)
rect(7, 11, 25, 13, paper)
rect(4, 15, 28, 16, shade)
rect(26, 16, 28, 27, shade)
rect(4, 27, 28, 28, shade)

# A big tactile red button, built from coarse, perfectly aligned pixels.
rect(11, 1, 21, 2, ink)
rect(8, 2, 24, 4, ink)
rect(7, 4, 25, 10, ink)
rect(9, 10, 23, 12, ink)
rect(10, 12, 22, 13, ink)
rect(9, 4, 23, 10, red)
rect(11, 10, 21, 12, red)
rect(10, 3, 22, 4, coral)
rect(8, 4, 24, 7, coral)
rect(10, 7, 22, 8, coral)
rect(12, 2, 20, 3, "#EDAC78")
rect(10, 3, 14, 4, "#EDAC78")
rect(9, 4, 12, 5, "#EDAC78")
rect(19, 8, 21, 11, "#BF5F49")

# A small green counter, warm cream digits and two mounting screws.
rect(6, 18, 26, 26, ink)
rect(7, 19, 25, 25, screen)
glyphs = {
    "0": ["111", "101", "101", "101", "111"],
    "1": ["010", "110", "010", "010", "111"],
    "2": ["111", "001", "111", "100", "111"],
    "3": ["111", "001", "111", "001", "111"],
}
for char, x in [("0", 8), ("1", 12), ("2", 18), ("3", 22)]:
    for y, row in enumerate(glyphs[char]):
        for dx, lit in enumerate(row):
            if lit == "1":
                rect(x + dx, 19 + y, x + dx + 1, 20 + y, mint)
rect(16, 20, 17, 21, mint)
rect(16, 22, 17, 23, mint)
rect(4, 17, 5, 18, ink)
rect(27, 17, 28, 18, ink)


def png(size):
    rows = bytearray()
    for y in range(size):
        rows.append(0)
        for x in range(size):
            rows.extend(pixels[min(SIZE - 1, y * SIZE // size)][min(SIZE - 1, x * SIZE // size)])

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))

    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(rows, 9)) + chunk(b"IEND", b"")


sizes = [16, 32, 48, 64, 128, 256]
frames = [png(size) for size in sizes]
header = struct.pack("<HHH", 0, 1, len(sizes))
offset = 6 + 16 * len(sizes)
directory = bytearray()
for size, frame in zip(sizes, frames):
    directory.extend(struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(frame), offset))
    offset += len(frame)
(OUT / "Switchit.ico").write_bytes(header + directory + b"".join(frames))
(OUT / "Switchit.png").write_bytes(png(256))
print(f"Created {OUT / 'Switchit.ico'} and Switchit.png ({len(sizes)} icon sizes).")
