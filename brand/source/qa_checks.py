"""Automated QA for the brand book and its package. Prints a report and exits non-zero on any failure.

Checks: layout overflow in the rendered pages (headless Edge), broken image/font references, selectable text and
embedded fonts in the PDF, internal links, outline, token <-> PDF/Markdown agreement, independent recomputation of the
contrast figures, and (optionally) that no recording-related source file changed since a saved hash snapshot."""
import json
import re
import subprocess
import sys
from pathlib import Path

import fitz

sys.path.insert(0, str(Path(__file__).parent))
from contrast import ratio  # noqa: E402

HERE = Path(__file__).resolve().parent
BRAND = HERE.parent
ROOT = BRAND.parent
EDGE = r'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
failures = []
report = []


def check(ok, msg):
    report.append(('PASS' if ok else 'FAIL') + '  ' + msg)
    if not ok:
        failures.append(msg)


# ------------------------------------------------------------------------------------- 1. layout overflow
QA_JS = """
<script>
Promise.all([document.fonts.ready, new Promise(r=>{ if(document.readyState==='complete') r(); else window.addEventListener('load',r); })]).then(()=>setTimeout(()=>{
 const out=[];
 document.querySelectorAll('.page').forEach(pg=>{
  const pr=pg.getBoundingClientRect(); const id=pg.id; const issues=[];
  const body=pg.querySelector(':scope > .body');
  const lim = 1080-86;
  pg.querySelectorAll('*').forEach(el=>{
    if(el.closest('.hdr')||el.closest('.ftr')||el.closest('svg')&&el.tagName.toLowerCase()!=='svg') return;
    const cs=getComputedStyle(el); if(cs.display==='none'||cs.visibility==='hidden') return;
    // skip decorative absolute layers with no text and the page-sized split halves
    const r=el.getBoundingClientRect(); if(r.width===0||r.height===0) return;
    const rel={r:r.right-pr.left,b:r.bottom-pr.top,l:r.left-pr.left,t:r.top-pr.top};
    const hasText=[...el.childNodes].some(n=>n.nodeType===3 && n.textContent.trim().length>0);
    const isMedia=el.tagName==='IMG'||el.tagName==='svg';
    if(!(hasText||isMedia)) return;
    if(rel.r>1920+1||rel.l<-1) issues.push('x-overflow:'+el.tagName+':'+(el.textContent||'').trim().slice(0,28));
    let clip=rel.b; let a=el.parentElement; while(a && a!==pg){ const ac=getComputedStyle(a); if(ac.overflow==='hidden'||ac.overflowY==='hidden'||ac.overflowY==='clip'){ clip=Math.min(clip,a.getBoundingClientRect().bottom-pr.top); } a=a.parentElement; }
    rel.b=clip;
    if(id==='p01') return;
    if(rel.b>lim+1 && !el.closest('.half') ) issues.push('bottom:'+el.tagName+':'+Math.round(rel.b)+':'+(el.textContent||'').trim().slice(0,28));
    if(el.closest('.half') && rel.b>1080-40) issues.push('half-bottom:'+el.tagName+':'+Math.round(rel.b)+':'+(el.textContent||'').trim().slice(0,28));
    if(hasText && el.scrollWidth>el.clientWidth+2 && cs.overflow==='hidden' && cs.textOverflow!=='ellipsis' && el.tagName!=='IMG') issues.push('clipped-text:'+(el.textContent||'').trim().slice(0,28));
  });
  if(issues.length) out.push(id+' '+issues.slice(0,4).join(' | '));
 });
 const pre=document.createElement('pre'); pre.id='qa-result'; pre.textContent=JSON.stringify(out); document.body.appendChild(pre);
},600));
</script>
"""


def layout_check():
    html = (HERE / 'book.html').read_text(encoding='utf-8')
    qa = HERE / '_qa.html'
    qa.write_text(html.replace('</body>', QA_JS + '</body>'), encoding='utf-8')
    r = subprocess.run([EDGE, '--headless=new', '--disable-gpu', '--window-size=1920,1080', '--virtual-time-budget=20000', '--dump-dom', qa.as_uri()],
                       capture_output=True, timeout=300)
    qa.unlink()
    dom = r.stdout.decode('utf-8', 'replace')
    m = re.search(r'<pre id="qa-result">(.*?)</pre>', dom, re.S)
    if not m:
        check(False, 'layout probe produced no result')
        return
    import html as H
    issues = json.loads(H.unescape(m.group(1)))
    check(not issues, f'no text/media overflow beyond page or into the footer zone ({len(issues)} pages flagged)')
    for i in issues:
        report.append('      ' + i)


# ------------------------------------------------------------------------------------- 2. references
def reference_check():
    html = (HERE / 'book.html').read_text(encoding='utf-8')
    srcs = set(re.findall(r'src="([^"]+)"', html)) | set(re.findall(r'url\(([^)]+)\)', html))
    missing = []
    for s in srcs:
        s = s.strip('"\'')
        if s.startswith(('data:', 'http', '#')):
            continue
        if not (HERE / s).resolve().exists():
            missing.append(s)
    check(not missing, f'all {len(srcs)} image/font references resolve' + (f' — missing: {missing[:5]}' if missing else ''))


# ------------------------------------------------------------------------------------- 3. PDF
def pdf_check():
    pdf = fitz.open(BRAND / 'Cap-IT-Brand-Book.pdf')
    n = len(pdf)
    check(35 <= n <= 90, f'PDF has {n} pages (target 35–50+)')
    check(all(abs(p.rect.width / p.rect.height - 16 / 9) < 0.01 for p in pdf), 'every page is 16:9')
    thin = [i + 1 for i, p in enumerate(pdf) if len(p.get_text().strip()) < 40]
    check(not thin, 'every page has selectable text' + (f' — thin pages {thin}' if thin else ''))
    fonts = {f[3] for p in pdf for f in p.get_fonts()}
    check(any('Inter' in f for f in fonts), f'Inter embedded ({len(fonts)} font faces)')
    toc = pdf.get_toc()
    check(len(toc) >= 20, f'PDF outline has {len(toc)} entries')
    links = [l for l in pdf[1].get_links() if l.get('kind') in (fitz.LINK_GOTO, fitz.LINK_NAMED)]
    names = pdf.resolve_names()
    check(len(links) >= 20, f'contents page has {len(links)} internal links')
    bad = [l for l in links if l.get('nameddest') not in names and not (0 <= l.get('page', -1) < n)]
    check(not bad, 'every contents link resolves to a page')
    mism = []
    withfooter = 0
    for i, p in enumerate(pdf, start=1):
        t = re.sub(r'\s+', '', p.get_text())
        m = re.findall(r'(\d\d)/(\d\d)', t)
        hit = [a for a, b in m if b == f'{n:02d}']
        if hit:
            withfooter += 1
            if f'{i:02d}' not in hit:
                mism.append(i)
    check(not mism and withfooter >= n - 14, f'footer page numbers match the page index on {withfooter} pages' + (f' — mismatches {mism}' if mism else ''))
    # effective resolution of embedded raster images
    low = []
    for i, p in enumerate(pdf):
        for img in p.get_images(full=True):
            xref = img[0]
            w, h = img[2], img[3]
            for rect in p.get_image_rects(xref):
                if rect.width > 0 and w / (rect.width / 0.75) < 0.5 and rect.width > 60:  # less than 50 % of a CSS px of source detail
                    low.append((i + 1, w, round(rect.width / 0.75)))
    check(not low, 'no embedded raster is shown at more than 2× its pixel size' + (f' — {low[:4]}' if low else ''))
    return pdf


# ------------------------------------------------------------------------------------- 4. tokens
def token_checks(pdf):
    text = ' '.join(p.get_text() for p in pdf).upper()
    md = (BRAND / 'Cap-IT-Brand-Guidelines.md').read_text(encoding='utf-8').upper()
    for name in ('Dark', 'Light'):
        d = json.loads((BRAND / f'Cap-IT-{name}-Theme.json').read_text(encoding='utf-8'))
        toks = d['proposed']['tokens']
        miss_pdf = [t['hex'] for t in toks.values() if t['hex'].upper() not in text]
        miss_md = [t['hex'] for t in toks.values() if t['hex'].upper() not in md]
        check(not miss_pdf, f'{name}: all {len(toks)} proposed token hex values appear in the PDF' + (f' — missing {miss_pdf[:4]}' if miss_pdf else ''))
        check(not miss_md, f'{name}: all proposed token hex values appear in the Markdown')
        impl = d['implemented']
        src = (ROOT / 'src' / 'CapIT.Desktop' / 'Styles' / 'Colors.axaml').read_text(encoding='utf-8').upper()
        miss_src = [v for v in impl.values() if v.upper() not in src]
        check(not miss_src, f'{name}: all {len(impl)} implemented values exist in Colors.axaml')
        # independent recomputation of the claimed contrast
        bad = []
        for n, t in toks.items():
            for bg, claimed in t.get('contrast', {}).items():
                real = ratio(t['hex'], bg)
                if abs(real - claimed) > 0.011:
                    bad.append((n, bg, claimed, round(real, 2)))
            if 'onColor' in t:
                real = ratio(t['onColor']['hex'], t['hex'])
                if abs(real - t['onColor']['contrast']) > 0.011:
                    bad.append((n, 'on', t['onColor']['contrast'], round(real, 2)))
        check(not bad, f'{name}: contrast figures recompute identically' + (f' — {bad[:3]}' if bad else ''))
        floor = [(n, min(t['contrast'].values())) for n, t in toks.items() if 'contrast' in t and n != 'border.control' and min(t['contrast'].values()) < 4.5]
        check(not floor, f'{name}: every text token ≥ 4.5 : 1 on every text layer')
        ctl = toks['border.control']
        check(min(ctl['contrast'].values()) >= 3.0, f'{name}: border.control ≥ 3 : 1 ({min(ctl["contrast"].values()):.2f})')
    # the Avalonia dictionary agrees with the JSON
    ax = (BRAND / 'avalonia' / 'CapIT.Brand.Proposed.axaml').read_text(encoding='utf-8').upper()
    d = json.loads((BRAND / 'Cap-IT-Dark-Theme.json').read_text(encoding='utf-8'))
    check(all(t['hex'].upper().lstrip('#') in ax for t in d['proposed']['tokens'].values()), 'Avalonia dictionary contains every dark token value')


# ------------------------------------------------------------------------------------- 5. worktree
def worktree_check(snapshot):
    if not snapshot:
        return
    import hashlib
    bad = []
    n = 0
    for line in Path(snapshot).read_text(encoding='utf-8').splitlines():
        digest, _, rel = line.partition(' ')
        rel = rel.strip().lstrip('*')
        p = ROOT / rel
        n += 1
        if not p.exists() or hashlib.sha256(p.read_bytes()).hexdigest() != digest:
            bad.append(rel)
    check(not bad, f'{n} files from the pre-brand snapshot (Smart Tracking changes + harness) are byte-identical' + (f' — changed: {bad}' if bad else ''))


def main():
    snapshot = sys.argv[1] if len(sys.argv) > 1 else None
    reference_check()
    pdf = pdf_check()
    token_checks(pdf)
    layout_check()
    worktree_check(snapshot)
    print('\n'.join(report))
    print(f'\n{len(report) - len(failures)} passed, {len(failures)} failed')
    sys.exit(1 if failures else 0)


if __name__ == '__main__':
    main()
