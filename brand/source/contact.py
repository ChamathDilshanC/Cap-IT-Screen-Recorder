"""contact.py <qa_dir> <out.png> <pages like 3,4,5-8> [cols]"""
import sys
from pathlib import Path
from PIL import Image
qa = Path(sys.argv[1]); sel = []
for part in sys.argv[3].split(','):
    a, _, b = part.partition('-'); sel += list(range(int(a), int(b or a) + 1))
cols = int(sys.argv[4]) if len(sys.argv) > 4 else 2
tw = 900 if cols == 2 else 640
ims = [Image.open(qa / f'p{i:02d}.png').convert('RGB') for i in sel]
th = round(tw * ims[0].height / ims[0].width)
rows = (len(ims) + cols - 1) // cols
sheet = Image.new('RGB', (cols * (tw + 8) + 8, rows * (th + 8) + 8), (90, 90, 90))
for k, im in enumerate(ims):
    im = im.resize((tw, th), Image.LANCZOS)
    sheet.paste(im, (8 + (k % cols) * (tw + 8), 8 + (k // cols) * (th + 8)))
sheet.save(sys.argv[2])
