"""Draws the Inlay icon, the "board" (bacheca), and writes it as a multi-size .ico.

A tall tile and two square ones on a graphite rounded square; the amber one is the selected tile.
Colors are Brace tokens: surface-raised, border-default, surface-active and accent.

    python scripts/genera-icona.py src/Inlay.App/Assets/inlay.ico [preview.png]
"""
import struct
import sys
import zlib

BACKGROUND = (0x1B, 0x1F, 0x24)
BORDER = (0x31, 0x37, 0x41)
TILE = (0x33, 0x39, 0x43)
ACCENT = (0xFF, 0xA1, 0x33)

# Shapes on a 128 x 128 grid, painted in order: (left, top, right, bottom, corner radius, color).
SHAPES = [
    (2, 2, 126, 126, 28, BORDER),
    (4, 4, 124, 124, 26, BACKGROUND),
    (24, 24, 62, 104, 8, TILE),
    (68, 24, 104, 60, 8, ACCENT),
    (68, 68, 104, 104, 8, TILE),
]


def inside(x, y, shape):
    left, top, right, bottom, radius, _ = shape
    if not (left <= x <= right and top <= y <= bottom):
        return False
    dx = max(left + radius - x, 0.0, x - (right - radius))
    dy = max(top + radius - y, 0.0, y - (bottom - radius))
    return dx * dx + dy * dy <= radius * radius


def render(size, samples):
    rows = []
    for py in range(size):
        row = []
        for px in range(size):
            r = g = b = a = 0.0
            for sy in range(samples):
                for sx in range(samples):
                    x = (px + (sx + 0.5) / samples) * 128 / size
                    y = (py + (sy + 0.5) / samples) * 128 / size
                    color = None
                    for shape in SHAPES:
                        if inside(x, y, shape):
                            color = shape[5]
                    if color is not None:
                        r, g, b, a = r + color[0], g + color[1], b + color[2], a + 1
            n = samples * samples
            if a == 0:
                row.append((0, 0, 0, 0))
            else:
                # Straight (not premultiplied) alpha: average the covered samples only.
                row.append((round(r / a), round(g / a), round(b / a), round(255 * a / n)))
        rows.append(row)
    return rows


def bmp_entry(rows):
    size = len(rows)
    header = struct.pack('<IiiHHIIiiII', 40, size, size * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    pixels = b''.join(bytes((b, g, r, a)) for row in reversed(rows) for (r, g, b, a) in row)
    mask = b'\x00' * (((size + 31) // 32) * 4 * size)
    return header + pixels + mask


def png_entry(rows):
    size = len(rows)
    raw = b''.join(b'\x00' + bytes(c for px in row for c in px) for row in rows)

    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data) & 0xFFFFFFFF)

    return (b'\x89PNG\r\n\x1a\n'
            + chunk(b'IHDR', struct.pack('>IIBBBBB', size, size, 8, 6, 0, 0, 0))
            + chunk(b'IDAT', zlib.compress(raw, 9))
            + chunk(b'IEND', b''))


def main(ico_path, preview_path=None):
    images = []
    for size in [16, 20, 24, 32, 40, 48, 64, 128, 256]:
        rows = render(size, samples=8 if size <= 64 else 3)
        images.append((size, png_entry(rows) if size == 256 else bmp_entry(rows)))
        if size == 256 and preview_path:
            with open(preview_path, 'wb') as preview:
                preview.write(png_entry(rows))
    offset = 6 + 16 * len(images)
    directory, data = b'', b''
    for size, blob in images:
        directory += struct.pack('<BBBBHHII', size % 256, size % 256, 0, 0, 1, 32, len(blob), offset + len(data))
        data += blob
    with open(ico_path, 'wb') as ico:
        ico.write(struct.pack('<HHH', 0, 1, len(images)) + directory + data)


if __name__ == '__main__':
    main(*sys.argv[1:3])
