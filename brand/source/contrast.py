"""WCAG 2.x contrast helpers shared by the token build and the book build."""


def _lin(c: float) -> float:
    c /= 255
    return c / 12.92 if c <= 0.03928 else ((c + 0.055) / 1.055) ** 2.4


def parse(hex_: str):
    h = hex_.lstrip('#')
    if len(h) == 8:  # AARRGGBB (Avalonia order)
        a = int(h[0:2], 16) / 255
        return int(h[2:4], 16), int(h[4:6], 16), int(h[6:8], 16), a
    return int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), 1.0


def luminance(rgb) -> float:
    r, g, b = rgb[:3]
    return 0.2126 * _lin(r) + 0.7152 * _lin(g) + 0.0722 * _lin(b)


def blend(fg_hex: str, bg_hex: str) -> str:
    """Composite a (possibly translucent AARRGGBB) colour over an opaque background."""
    r, g, b, a = parse(fg_hex)
    br, bg_, bb, _ = parse(bg_hex)
    return '#%02X%02X%02X' % (round(r * a + br * (1 - a)), round(g * a + bg_ * (1 - a)), round(b * a + bb * (1 - a)))


def ratio(fg_hex: str, bg_hex: str) -> float:
    bg = blend(bg_hex, '#FFFFFF') if len(bg_hex.lstrip('#')) == 8 else bg_hex
    fg = blend(fg_hex, bg) if len(fg_hex.lstrip('#')) == 8 else fg_hex
    l1, l2 = luminance(parse(fg)), luminance(parse(bg))
    hi, lo = max(l1, l2), min(l1, l2)
    return (hi + 0.05) / (lo + 0.05)


def grade(r: float, large: bool = False, ui: bool = False) -> str:
    """AA verdict. ui=True applies the 3:1 non-text threshold (SC 1.4.11)."""
    need = 3.0 if (ui or large) else 4.5
    if not ui and not large and r >= 7:
        return 'AAA'
    return 'AA' if r >= need else 'FAIL'


def rgb_str(hex_: str) -> str:
    r, g, b, _ = parse(hex_)
    return f'{r}, {g}, {b}'


def hsl_str(hex_: str) -> str:
    import colorsys
    r, g, b, _ = parse(hex_)
    h, l, s = colorsys.rgb_to_hls(r / 255, g / 255, b / 255)
    return f'{round(h * 360)}, {round(s * 100)}%, {round(l * 100)}%'


def cmyk_str(hex_: str) -> str:
    r, g, b, _ = [x / 255 if i < 3 else x for i, x in enumerate(parse(hex_))]
    k = 1 - max(r, g, b)
    if k >= 1:
        return '0, 0, 0, 100'
    c, m, y = [(1 - v - k) / (1 - k) for v in (r, g, b)]
    return f'{round(c * 100)}, {round(m * 100)}, {round(y * 100)}, {round(k * 100)}'
