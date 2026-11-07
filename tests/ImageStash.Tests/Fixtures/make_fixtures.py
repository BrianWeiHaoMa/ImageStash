"""Writes the pictures the Media checks decode, beside this script, and verifies them with Pillow.

Run `python make_fixtures.py` from this folder after changing it; the files it writes are committed: still.png,
transparent.png, translucent.png, invisible-blue.png, animated.png, photo.jpg, photo.jpeg, progressive.jpg, still.gif,
animated.gif, still.webp, transparent.webp, animated.webp, long.gif, restore-previous.gif, oriented-1.jpg to
oriented-8.jpg, oriented-6.webp, oriented-6.png and huge.png. Truncated and
garbage variants are made by the checks at run time, under %TEMP%.
"""
import struct
import zlib
from pathlib import Path

import PIL
from PIL import Image, ImageOps

root = Path(__file__).resolve().parent
red, green, blue = (255, 0, 0), (0, 255, 0), (0, 0, 255)
solid = Image.new("RGB", (8, 6), blue)
alpha = Image.new("RGBA", (8, 6), (0, 0, 255, 255))
alpha.putpixel((1, 1), (255, 0, 0, 128))
translucent = Image.new("RGBA", (8, 6), (255, 0, 0, 128))
# Opaque red beside a fully transparent pixel whose hidden colour is blue: scaled down, no blue may show.
invisible_blue = Image.new("RGBA", (2, 1), (255, 0, 0, 255))
invisible_blue.putpixel((1, 0), (0, 0, 255, 0))
frames = [Image.new("RGB", (8, 6), color) for color in [red, green, blue]]

solid.save(root / "still.png")
alpha.save(root / "transparent.png")
translucent.save(root / "translucent.png")
invisible_blue.save(root / "invisible-blue.png")
frames[0].save(root / "animated.png", save_all=True, append_images=frames[1:], duration=100, loop=0)
solid.save(root / "photo.jpg")
solid.save(root / "photo.jpeg")
# Busy enough that its scans hold escaped 0xFF bytes, and progressive, so it has several scans.
busy = Image.frombytes("RGB", (32, 32), bytes((index * 97 + 13) % 256 for index in range(32 * 32 * 3)))
busy.save(root / "progressive.jpg", progressive=True, quality=95)
solid.save(root / "still.gif")
frames[0].save(root / "animated.gif", save_all=True, append_images=frames[1:], duration=[100, 200, 300], loop=0)
solid.save(root / "still.webp", lossless=True)
alpha.save(root / "transparent.webp", lossless=True)
frames[0].save(root / "animated.webp", save_all=True, lossless=True, append_images=frames[1:],
               duration=[100, 200, 300], loop=0)

# Sixty frames, each a colour of its own at (0,0), lasting 10 to 50 ms in turn.
long_colors = [(index * 4, 255 - index * 4, (index * 37) % 256) for index in range(60)]
long_durations = [10 * (index % 5 + 1) for index in range(60)]
long_frames = [Image.new("RGB", (8, 6), color) for color in long_colors]
long_frames[0].save(root / "long.gif", save_all=True, append_images=long_frames[1:], duration=long_durations,
                    loop=0, optimize=False)

# A red picture where a blue square shows for one frame and is then put back (disposal 3, restore previous), before a
# green square appears elsewhere: the third frame is drawn over the first, not over the second.
marked = [Image.new("RGB", (8, 6), red) for _ in range(3)]
marked[1].paste(blue, (0, 0, 2, 2))
marked[2].paste(green, (6, 4, 8, 6))
marked[0].save(root / "restore-previous.gif", save_all=True, append_images=marked[1:], duration=100, loop=0,
               disposal=[1, 3, 1])

# Photos as a phone writes them: 40 x 24 pixels as stored, four quadrants of distinct colours, and an EXIF orientation
# that turns or mirrors them upright (24 x 40 for 5 to 8). Below, Pillow's exif_transpose says which stored quadrant each
# upright quadrant shows, the table the checks assert; a PNG's eXIf is written too, which the codec does not read.
quadrant_colors = {"A": (230, 30, 30), "B": (30, 200, 30), "C": (30, 30, 230), "D": (240, 220, 20)}
stored = Image.new("RGB", (40, 24))
for letter, box in zip("ABCD", [(0, 0, 20, 12), (20, 0, 40, 12), (0, 12, 20, 24), (20, 12, 40, 24)]):
    stored.paste(quadrant_colors[letter], box)
# The upright picture's top-left, top-right, bottom-left and bottom-right quadrants, as stored quadrants.
upright_quadrants = {1: "ABCD", 2: "BADC", 3: "DCBA", 4: "CDAB", 5: "ACBD", 6: "CADB", 7: "DBCA", 8: "BDAC"}


def orientation(value):
    exif = Image.Exif()
    exif[0x0112] = value
    return exif.tobytes()


for value in upright_quadrants:
    stored.save(root / f"oriented-{value}.jpg", exif=orientation(value), quality=95, subsampling=0)
stored.save(root / "oriented-6.webp", exif=orientation(6), lossless=True)
stored.save(root / "oriented-6.png", exif=orientation(6))


def chunk(kind, data):
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))


# A whole PNG, IEND included, whose header declares 60,000 x 60,000 RGBA pixels and whose data holds one row.
row = b"\x00" + bytes(60000 * 4)
(root / "huge.png").write_bytes(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", 60000, 60000, 8, 6, 0, 0, 0))
                               + chunk(b"IDAT", zlib.compress(row)) + chunk(b"IEND", b""))

print(f"Fixtures written with Pillow {PIL.__version__}")
for name in ["still.png", "transparent.png", "translucent.png", "animated.png", "photo.jpg", "photo.jpeg", "still.gif",
             "animated.gif", "still.webp", "transparent.webp", "animated.webp", "long.gif", "restore-previous.gif"]:
    with Image.open(root / name) as image:
        assert image.size == (8, 6), name
for name, durations, colors in [("animated.png", [100] * 3, [red, green, blue]),
                                ("animated.gif", [100, 200, 300], [red, green, blue]),
                                ("animated.webp", [100, 200, 300], [red, green, blue]),
                                ("long.gif", long_durations, long_colors)]:
    with Image.open(root / name) as image:
        assert image.n_frames == len(durations), name
        for index, duration in enumerate(durations):
            image.seek(index)
            image.load()
            assert image.info["duration"] == duration, (name, index)
            assert image.convert("RGB").getpixel((0, 0)) == colors[index], (name, index)
with Image.open(root / "restore-previous.gif") as image:
    for index, expected in enumerate(marked):
        image.seek(index)
        assert image.convert("RGB").tobytes() == expected.tobytes(), ("restore-previous.gif", index)
for name in ["transparent.png", "transparent.webp"]:
    with Image.open(root / name) as image:
        assert image.convert("RGBA").tobytes() == alpha.tobytes(), name
with Image.open(root / "translucent.png") as image:
    assert image.convert("RGBA").tobytes() == translucent.tobytes()
with Image.open(root / "invisible-blue.png") as image:
    assert image.convert("RGBA").tobytes() == invisible_blue.tobytes()
with Image.open(root / "progressive.jpg") as image:
    assert image.size == (32, 32) and image.info.get("progressive") == 1
for name, value in [*((f"oriented-{value}.jpg", value) for value in upright_quadrants), ("oriented-6.webp", 6)]:
    with Image.open(root / name) as image:
        assert image.size == (40, 24) and image.getexif()[0x0112] == value, name
        upright = ImageOps.exif_transpose(image).convert("RGB")
        assert upright.size == ((24, 40) if value >= 5 else (40, 24)), name
        width, height = upright.size
        centres = [(width // 4, height // 4), (3 * width // 4, height // 4), (width // 4, 3 * height // 4),
                   (3 * width // 4, 3 * height // 4)]
        for centre, letter in zip(centres, upright_quadrants[value]):
            shown = upright.getpixel(centre)
            assert all(abs(a - b) <= 8 for a, b in zip(shown, quadrant_colors[letter])), (name, centre, shown, letter)
with Image.open(root / "oriented-6.png") as image:
    assert image.size == (40, 24) and image.getexif()[0x0112] == 6 and image.convert("RGB").tobytes() == stored.tobytes()
progressive = (root / "progressive.jpg").read_bytes()
assert progressive.count(b"\xff\xda") > 1 and b"\xff\x00" in progressive
assert struct.unpack(">II", (root / "huge.png").read_bytes()[16:24]) == (60000, 60000)
print("Verified: sizes, frame counts, durations and colours as Pillow reads them; the four alpha fixtures pixel for"
      " pixel; the progressive JPEG's several scans and escaped bytes; the large PNG's declared size; each oriented"
      " photo's EXIF orientation and the stored quadrant each upright quadrant shows.")
