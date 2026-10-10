"""Renders PDF pages to PNG (QA) — usage: render_pages.py <outdir> [scale] [pages like 1,2,5-7]"""
import sys
from pathlib import Path
import fitz
pdf = Path(__file__).resolve().parents[1] / 'Cap-IT-Brand-Book.pdf'
out = Path(sys.argv[1]); out.mkdir(parents=True, exist_ok=True)
scale = float(sys.argv[2]) if len(sys.argv) > 2 else 0.9
sel = None
if len(sys.argv) > 3:
    sel = set()
    for part in sys.argv[3].split(','):
        a, _, b = part.partition('-'); sel.update(range(int(a), int(b or a) + 1))
d = fitz.open(pdf)
for i, p in enumerate(d, start=1):
    if sel and i not in sel: continue
    p.get_pixmap(matrix=fitz.Matrix(scale, scale)).save(out / f'p{i:02d}.png')
print('rendered', len(d), 'pages')
