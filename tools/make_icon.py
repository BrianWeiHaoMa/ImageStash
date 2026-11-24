"""Writes src/ImageStash.Windows/ImageStash.ico, the executable's and the tray's icon, from the logo below.
The recording, paused and unavailable variants carry a single badge at the top right. A transparent ring separates
the badge from the logo so it reads on light and dark taskbars.

Each size is drawn by the installed Microsoft Edge and stored as a PNG entry of the .ico; it needs Python 3 and the
playwright package.

    python tools/make_icon.py
    python tools/make_icon.py --shortcut-marks  # refresh only the paused and unavailable variants
"""
import argparse
import struct
from pathlib import Path

from playwright.sync_api import sync_playwright

ROOT = Path(__file__).resolve().parent.parent
ICON = ROOT / "src" / "ImageStash.Windows" / "ImageStash.ico"
RECORDING_ICON = ROOT / "src" / "ImageStash.Windows" / "ImageStash-recording.ico"
PAUSED_ICON = ROOT / "src" / "ImageStash.Windows" / "ImageStash-paused.ico"
UNAVAILABLE_ICON = ROOT / "src" / "ImageStash.Windows" / "ImageStash-unavailable.ico"
SIZES = [16, 20, 24, 32, 40, 48, 64, 256]
REC = "#E5484D"
# The logo's one home, drawn at {size} pixels square.
LOGO = """
  <svg width="{size}" height="{size}" viewBox="0 0 32 32" style="flex:none">
    <defs><linearGradient id="lg-stash" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#5A8BFF"/><stop offset="1" stop-color="#2A55E8"/></linearGradient></defs>
    <rect x="1.5" y="1.5" width="29" height="29" rx="7.5" fill="url(#lg-stash)"/>
    <rect x="8.5" y="6.5" width="15" height="12.5" rx="2.5" fill="#fff" opacity=".5" transform="rotate(-11 16 12.75)"/>
    <rect x="7.5" y="11" width="17" height="14.5" rx="2.6" fill="#fff"/>
    <path d="M10 23 l4.2-5.2 3.1 3.6 2.1-2.3 3.1 3.9z" fill="#2F6BFF"/>
    <circle cx="20" cy="15.4" r="1.7" fill="#2F6BFF"/>
  </svg>"""


def logo_svg(size):
    return LOGO.replace("{size}", str(size))


def recording_html(size):
    return badge_html(size, REC)


def paused_html(size):
    # These edges land on whole pixels at 16 px, so the bars read separately at tray size.
    bars = '<path fill="white" d="M2 1.6h1.6V8H2zM6.8 1.6h1.6V8H6.8z"/>'
    return badge_html(size, "#252D45", bars)


def unavailable_html(size):
    warning = '<path fill="#342303" d="M3.6 1.6h1.6v4.8H3.6zM3.6 8h1.6v1.6H3.6z"/>'
    return badge_html(size, "#FFCA59", warning)


def badge_html(size, color, mark=""):
    """The logo with a badge at its top right, the ring around it cleared out of the logo."""
    dot = size * 7 / 18
    ring = size * 2 / 18
    cx, cy = size - dot / 2, dot / 2
    clear = dot / 2 + ring
    mask = f"radial-gradient(circle at {cx}px {cy}px, transparent {clear}px, black {clear + 0.5}px)"
    glyph = f'<svg width="{dot}" height="{dot}" viewBox="0 0 10 10" style="display:block">{mark}</svg>' if mark else ""
    return (
        f'<div style="position:relative;width:{size}px;height:{size}px">'
        f'<div style="width:{size}px;height:{size}px;-webkit-mask-image:{mask};mask-image:{mask}">{logo_svg(size)}</div>'
        f'<div style="position:absolute;left:{cx - dot / 2}px;top:{cy - dot / 2}px;width:{dot}px;height:{dot}px;'
        f'border-radius:50%;background:{color}">{glyph}</div></div>'
    )


def ico(pngs):
    header = struct.pack("<HHH", 0, 1, len(pngs))
    offset = len(header) + 16 * len(pngs)
    entries, data = b"", b""
    for size, png in pngs:
        side = 0 if size >= 256 else size
        entries += struct.pack("<BBBBHHII", side, side, 0, 0, 1, 32, len(png), offset + len(data))
        data += png
    return header + entries + data


def drawn(browser, html):
    pngs = []
    for size in SIZES:
        page = browser.new_page(viewport={"width": size, "height": size}, device_scale_factor=1)
        page.set_content(f'<body style="margin:0;background:transparent">{html(size)}</body>')
        pngs.append((size, page.screenshot(omit_background=True, clip={"x": 0, "y": 0, "width": size, "height": size})))
        page.close()
    return pngs


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--shortcut-marks", action="store_true", help="write only the paused and unavailable icons")
    marks_only = parser.parse_args().shortcut_marks
    variants = [] if marks_only else [(ICON, logo_svg), (RECORDING_ICON, recording_html)]
    variants += [
        (PAUSED_ICON, paused_html),
        (UNAVAILABLE_ICON, unavailable_html),
    ]
    with sync_playwright() as p:
        browser = p.chromium.launch(channel="msedge")
        pictures = [(path, ico(drawn(browser, html))) for path, html in variants]
        browser.close()
    for path, picture in pictures:
        path.write_bytes(picture)
        print(f"wrote {path.relative_to(ROOT)} with sizes {SIZES}")


if __name__ == "__main__":
    main()
