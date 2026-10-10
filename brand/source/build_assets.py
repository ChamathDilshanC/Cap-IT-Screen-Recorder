"""Derives the Cap-IT logo variants and exports the icon set.

Nothing is redrawn: every logo file is the repository's own artwork (assets/Logo-CapIT.png) cropped,
recoloured or composed onto a plate. The horizontal lockup is the only composition that adds type, and it is
labelled PROPOSED because the repository defines no horizontal lockup.
"""
import re
import shutil
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
BRAND = ROOT / 'brand'
ASSETS = BRAND / 'Cap-IT-Brand-Assets'
FONTS = BRAND / 'source' / 'fonts'
SRC = Image.open(ROOT / 'assets' / 'Logo-CapIT.png').convert('RGBA')

MIDNIGHT, ARCTIC, ICE, INK = (9, 17, 29), (255, 255, 255), (245, 249, 252), (17, 24, 39)
CYAN_D, CYAN_L = (24, 220, 232), (8, 124, 136)


def trim(im, pad=0):
    a = np.array(im)[..., 3]
    ys, xs = np.where(a > 8)
    box = (max(xs.min() - pad, 0), max(ys.min() - pad, 0), min(xs.max() + 1 + pad, im.width), min(ys.max() + 1 + pad, im.height))
    return im.crop(box)


def split_rows():
    a = np.array(SRC)[..., 3]
    rows = (a > 8).any(axis=1)
    # the first empty band after the symbol separates it from the wordmark
    ys = np.where(rows)[0]
    gaps = [(ys[i], ys[i + 1]) for i in range(len(ys) - 1) if ys[i + 1] - ys[i] > 20]
    return gaps[0] if gaps else (None, None)


def recolor(im, rgb, only=None):
    a = np.array(im).copy()
    mask = a[..., 3] > 0
    if only is not None:
        mask &= only(a)
    a[..., :3][mask] = rgb
    return Image.fromarray(a)


def mono(im, rgb):
    a = np.array(im).copy()
    a[..., :3] = rgb
    return Image.fromarray(a)


def on_plate(sym, size, plate_rgb, radius_frac=0.224, scale=0.64, gradient=None):
    plate = Image.new('RGBA', (size, size), (0, 0, 0, 0))
    base = Image.new('RGBA', (size, size), plate_rgb + (255,))
    if gradient:
        top, bottom = gradient
        arr = np.zeros((size, size, 4), dtype=np.uint8)
        for y in range(size):
            t = y / (size - 1)
            arr[y, :, :3] = [round(top[i] * (1 - t) + bottom[i] * t) for i in range(3)]
            arr[y, :, 3] = 255
        base = Image.fromarray(arr)
    mask = Image.new('L', (size * 4, size * 4), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, size * 4 - 1, size * 4 - 1), radius=int(size * 4 * radius_frac), fill=255)
    mask = mask.resize((size, size), Image.LANCZOS)
    plate.paste(base, (0, 0), mask)
    s = sym.copy()
    target = int(size * scale)
    s.thumbnail((target, target), Image.LANCZOS)
    plate.alpha_composite(s, ((size - s.width) // 2, (size - s.height) // 2))
    return plate


def main():
    (ASSETS / 'logo' / 'existing').mkdir(parents=True, exist_ok=True)
    (ASSETS / 'logo' / 'derived').mkdir(parents=True, exist_ok=True)
    (ASSETS / 'logo' / 'proposed').mkdir(parents=True, exist_ok=True)
    (ASSETS / 'app-icon').mkdir(parents=True, exist_ok=True)
    (ASSETS / 'icons').mkdir(parents=True, exist_ok=True)

    g0, g1 = split_rows()
    top = SRC.crop((0, 0, SRC.width, g0 + 1))
    sym = trim(top)
    stacked = trim(SRC)
    word = trim(SRC.crop((0, g1, SRC.width, SRC.height)))

    # ---- EXISTING (cropped, untouched pixels)
    stacked.save(ASSETS / 'logo' / 'existing' / 'capit-lockup-stacked.png')
    sym.save(ASSETS / 'logo' / 'existing' / 'capit-symbol.png')
    shutil.copy(ROOT / 'assets' / 'Logo-Mark.png', ASSETS / 'logo' / 'existing' / 'capit-symbol-256-app.png')
    shutil.copy(ROOT / 'assets' / 'AppIcon.ico', ASSETS / 'logo' / 'existing' / 'AppIcon.ico')

    # ---- DERIVED (recolour only)
    dark_text = lambda a: (a[..., :3].astype(int).sum(-1) < 300)  # the #3A3939 wordmark pixels
    reversed_ = recolor(stacked, ICE, only=dark_text)
    reversed_.save(ASSETS / 'logo' / 'derived' / 'capit-lockup-stacked-reversed.png')
    for name, rgb in (('black', INK), ('white', ARCTIC), ('cyan-midnight', CYAN_D), ('cyan-arctic', CYAN_L)):
        mono(sym, rgb).save(ASSETS / 'logo' / 'derived' / f'capit-symbol-mono-{name}.png')
        mono(stacked, rgb).save(ASSETS / 'logo' / 'derived' / f'capit-lockup-mono-{name}.png')

    # ---- PROPOSED horizontal lockup: the existing symbol + "Cap-IT" in Inter SemiBold
    for theme, ink, bg in (('midnight', ICE, MIDNIGHT), ('arctic', INK, ARCTIC)):
        H = 360
        s = sym.copy(); s.thumbnail((H, H), Image.LANCZOS)
        font = ImageFont.truetype(str(FONTS / 'Inter-SemiBold.ttf'), int(H * 0.62))
        text = 'Cap-IT'
        tb = font.getbbox(text)
        gap = int(H * 0.22)
        W = s.width + gap + (tb[2] - tb[0]) + 8
        canvas = Image.new('RGBA', (W, H), (0, 0, 0, 0))
        canvas.alpha_composite(s, (0, (H - s.height) // 2))
        d = ImageDraw.Draw(canvas)
        d.text((s.width + gap - tb[0], (H - (tb[3] - tb[1])) // 2 - tb[1]), text, font=font, fill=ink + (255,))
        trim(canvas).save(ASSETS / 'logo' / 'proposed' / f'capit-lockup-horizontal-on-{theme}.png')

    # ---- app icon plates (PROPOSED treatment of the existing symbol)
    for size in (1024, 512, 256, 128, 64, 48, 32, 16):
        on_plate(sym, size, MIDNIGHT, gradient=((16, 28, 43), (9, 17, 29))).save(ASSETS / 'app-icon' / f'capit-icon-midnight-{size}.png')
        on_plate(sym, size, ARCTIC, gradient=((255, 255, 255), (241, 245, 249))).save(ASSETS / 'app-icon' / f'capit-icon-arctic-{size}.png')
    on_plate(sym, 512, MIDNIGHT, radius_frac=0.5, scale=0.62, gradient=((16, 28, 43), (9, 17, 29))).save(ASSETS / 'app-icon' / 'capit-avatar-midnight-512.png')
    on_plate(sym, 512, ARCTIC, radius_frac=0.5, scale=0.62, gradient=((255, 255, 255), (241, 245, 249))).save(ASSETS / 'app-icon' / 'capit-avatar-arctic-512.png')
    ico_sizes = [(s, s) for s in (16, 32, 48, 64, 128, 256)]
    on_plate(sym, 256, MIDNIGHT, gradient=((16, 28, 43), (9, 17, 29))).save(ASSETS / 'app-icon' / 'capit-icon-midnight.ico', sizes=ico_sizes)

    # ---- icons: the application's own geometry, exported 1:1 as SVG
    xaml = (ROOT / 'src' / 'CapIT.Desktop' / 'Styles' / 'Icons.axaml').read_text(encoding='utf-8')
    icons = {}
    for m in re.finditer(r'<StreamGeometry x:Key="Icon\.([A-Za-z.]+)">([^<]+)</StreamGeometry>', xaml):
        icons[m.group(1)] = m.group(2).strip()
    for name, d in icons.items():
        filled = name.startswith('Fill.') or name == 'RecordCircle' and False
        if filled:
            svg = (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor"><path d="{d}"/></svg>')
        else:
            svg = (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" '
                   f'stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round"><path d="{d}"/></svg>')
        (ASSETS / 'icons' / f'{name.lower().replace(".", "-")}.svg').write_text(svg, encoding='utf-8')
    import json
    (BRAND / 'source' / 'icons.json').write_text(json.dumps(icons, indent=1), encoding='utf-8')
    print('logo, app-icon and', len(icons), 'icons exported; symbol', sym.size, 'stacked', stacked.size, 'wordmark', word.size)


if __name__ == '__main__':
    main()
