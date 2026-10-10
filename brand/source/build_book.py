"""Assembles brand/source/book.html from the page modules and prints it to PDF with headless Edge."""
import importlib
import subprocess
import sys
from html import escape
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from lib import Page, css, render_page, img  # noqa: E402

HERE = Path(__file__).parent
EDGE = r'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
MODULES = [m for m in ('pages_a', 'pages_b', 'pages_c', 'pages_d') if (HERE / f'{m}.py').exists()]


def toc_page(pages, total):
    entries = []
    for i, p in enumerate(pages, start=1):
        if p.toc:
            entries.append((i, p.toc))
    half = (len(entries) + 1) // 2
    def col(items):
        return ''.join(f'<a href="#p{n:02d}"><span><small>{c.split(" · ")[0]}</small>{escape(c.split(" · ")[1])}</span><span>{n:02d}</span></a>' for n, c in items)
    body = f'''<div class="g12" style="height:100%;align-content:start;row-gap:30px">
 <div style="grid-column:1/5"><div class="eyebrow">Contents</div><h1 class="display" style="margin:16px 0 22px">Twenty chapters,<br>two themes.</h1>
  <p class="p">Every chapter shows Midnight (dark) and Arctic (light) with equal care. Chapter 04 is the dedicated dual-theme chapter; chapters 10 and 13 present every component and every screen in both.</p></div>
 <div style="grid-column:6/13" class="toc"><div class="g2" style="gap:56px"><div>{col(entries[:half])}</div><div>{col(entries[half:])}</div></div></div></div>'''
    return Page('light', 'Front matter', 'Contents', body)


SCRIPT = '''
<script>
function fit(){
 function walk(el,acc){ for(const c of el.children){ const cs=getComputedStyle(c); if(cs.position==='absolute'||cs.position==='fixed'||cs.display==='none') continue; const r=c.getBoundingClientRect(); if(r.height>0 && c.style.height!=='100%') acc.max=Math.max(acc.max,r.bottom); if(c.tagName.toLowerCase()!=='svg') walk(c,acc);} }
 function used(f){ const acc={max:0}; walk(f,acc); return acc.max-f.getBoundingClientRect().top; }
 document.querySelectorAll('.page').forEach(pg=>{
  const body=pg.querySelector(':scope > .body'); const f=body && body.querySelector(':scope > .fit'); if(!f) return;
  const avail=body.clientHeight;
  function natural(z){ f.style.transform='none'; f.style.width=(100/z)+'%'; return used(f); }
  let u=natural(1), z=1;
  if(u>avail) z=Math.max(0.8,avail/u); else if(u<avail*0.86) z=Math.min(1.3,avail/u*0.97);
  let guard=0; while(guard<30){ u=natural(z); if(u*z<=avail || z<=0.8) break; z=Math.max(0.8,z-0.02); guard++; }
  if(Math.abs(z-1)<0.012){ f.style.width=''; f.style.transform=''; return; }
  f.style.width=(100/z)+'%'; f.style.transform='scale('+z+')';
 });
}
Promise.all([document.fonts.ready, new Promise(r=>{ if(document.readyState==='complete') r(); else window.addEventListener('load',r); })]).then(fit);
</script>'''


def main():
    pages = []
    for m in MODULES:
        pages += importlib.import_module(m).build()
    # insert contents after the cover + reading page (index 2)
    toc = toc_page([None] * 0 + pages[:], len(pages) + 1)
    pages.insert(1, toc)
    # recompute ToC with final numbering
    toc.body = toc_page(pages, len(pages)).body
    total = len(pages)
    sections = '\n'.join(render_page(p, i, total) for i, p in enumerate(pages, start=1))
    sections = sections.replace('__PAGES__', str(total))
    html = f'<!doctype html><html lang="en"><head><meta charset="utf-8"><title>Cap-IT Brand Book</title><style>{css()}</style></head><body>{sections}{SCRIPT}</body></html>'
    out = HERE / 'book.html'
    out.write_text(html, encoding='utf-8')
    pdf = HERE.parent / 'Cap-IT-Brand-Book.pdf'
    subprocess.run([EDGE, '--headless=new', '--disable-gpu', '--no-pdf-header-footer', '--generate-pdf-document-outline',
                    f'--print-to-pdf={pdf}', '--virtual-time-budget=20000', out.as_uri()], check=True, capture_output=True, timeout=300)
    print('pages', total, '->', pdf)


if __name__ == '__main__':
    main()
