"""Exports the brand graphic-language devices as standalone SVG files (theme-neutral via currentColor / CSS variables)."""
from pathlib import Path

OUT = Path(__file__).resolve().parents[1] / 'Cap-IT-Brand-Assets' / 'graphics'
OUT.mkdir(parents=True, exist_ok=True)


def svg(w, h, body, extra=''):
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {w} {h}" width="{w}" height="{h}" fill="none" stroke="currentColor" stroke-linecap="round" stroke-linejoin="round" {extra}>{body}</svg>\n'


FILES = {
    'capture-brackets.svg': svg(520, 340, '<g stroke-width="4"><path d="M2 64V12a10 10 0 0 1 10-10H64"/><path d="M456 2h52a10 10 0 0 1 10 10V64"/><path d="M518 276v52a10 10 0 0 1-10 10H456"/><path d="M64 338H12a10 10 0 0 1-10-10V276"/></g>'),
    'focus-target.svg': svg(220, 160, '<g stroke-width="3"><circle cx="110" cy="80" r="14"/><circle cx="110" cy="80" r="40" opacity=".5" stroke-dasharray="3 9"/><path d="M110 20v22M110 118v22M50 80h22M148 80h22"/></g>'),
    'viewport-frame.svg': svg(260, 170, '<rect x="6" y="6" width="248" height="158" rx="10" stroke-width="2.5" opacity=".5"/><rect x="70" y="40" width="120" height="76" rx="6" stroke-width="3.5"/><path d="M70 40 6 6M190 40l64-34M70 116 6 164M190 116l64 48" stroke-width="2.5" opacity=".35" stroke-dasharray="4 6"/>'),
    'camera-path.svg': svg(260, 160, '<path d="M14 130C60 130 70 40 120 60S190 120 240 34" stroke-width="3" opacity=".25" stroke-dasharray="2 9"/><path d="M150 96C170 112 200 96 240 34" stroke-width="3.5"/><circle cx="240" cy="34" r="9" fill="currentColor" stroke="none"/>'),
    'timeline-geometry.svg': svg(260, 120, '<g stroke-width="2" opacity=".6">' + ''.join(f'<path d="M{20+i*16} {70 if i%4 else 58}v{14 if i%4 else 26}"/>' for i in range(15)) + '</g><rect x="64" y="40" width="112" height="60" rx="6" fill="currentColor" fill-opacity=".14" stroke-width="3"/><path d="M110 24v90" stroke-width="2.5"/>'),
    'precision-crosshair.svg': svg(160, 160, '<g stroke-width="2.5"><path d="M80 8v52M80 100v52M8 80h52M100 80h52"/><circle cx="80" cy="80" r="6" fill="currentColor"/></g>'),
    'layered-frames.svg': svg(240, 170, '<g stroke-width="2.5"><rect x="64" y="8" width="168" height="108" rx="12" opacity=".4"/><rect x="38" y="30" width="168" height="108" rx="12" opacity=".7"/><rect x="12" y="52" width="168" height="108" rx="12" stroke-width="3.5"/></g>'),
    'technical-grid.svg': svg(960, 540, '<defs><pattern id="g" width="48" height="48" patternUnits="userSpaceOnUse"><path d="M48 0H0V48" stroke-width="1" opacity=".35"/></pattern><radialGradient id="m" cx="75%" cy="30%" r="70%"><stop offset="0" stop-color="#fff"/><stop offset="1" stop-color="#000"/></radialGradient><mask id="k"><rect width="960" height="540" fill="url(#m)"/></mask></defs><rect width="960" height="540" fill="url(#g)" mask="url(#k)"/>'),
    'spectrum-strip.svg': '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 600 6" width="600" height="6"><defs><linearGradient id="s"><stop offset="0" stop-color="#FFAA21"/><stop offset=".25" stop-color="#EA0B7F"/><stop offset=".5" stop-color="#971A8D"/><stop offset=".75" stop-color="#1AABE3"/><stop offset="1" stop-color="#85C536"/></linearGradient></defs><rect width="600" height="6" rx="3" fill="url(#s)"/></svg>\n',
}

for name, text in FILES.items():
    (OUT / name).write_text(text, encoding='utf-8')
(OUT / 'README.md').write_text('# Graphic language SVGs\n\nPROPOSED devices (see the brand book, chapter 08). Strokes use `currentColor`: set `color` to `brand.primary` (Midnight `#18DCE8`, Arctic `#087C88`). `spectrum-strip.svg` is derived from the five existing logo hues and must stay 6 px or thinner.\n', encoding='utf-8')
print(len(FILES), 'graphics exported')
