"""Shared building blocks for the Cap-IT brand book: theme CSS (generated from the token JSON so the PDF and
the tokens cannot disagree), page chrome and the reusable specimens."""
import json
from html import escape
from pathlib import Path

HERE = Path(__file__).resolve().parent
BRAND = HERE.parent
ASSETS = '../Cap-IT-Brand-Assets'
TOK = json.loads((BRAND / 'Cap-IT-Brand-Tokens.json').read_text(encoding='utf-8'))
DARK = json.loads((BRAND / 'Cap-IT-Dark-Theme.json').read_text(encoding='utf-8'))
LIGHT = json.loads((BRAND / 'Cap-IT-Light-Theme.json').read_text(encoding='utf-8'))
ICONS = json.loads((HERE / 'icons.json').read_text(encoding='utf-8'))
THEMES = {'dark': DARK, 'light': LIGHT}


def tk(theme, name):
    return THEMES[theme]['proposed']['tokens'][name]


def hexv(theme, name):
    e = tk(theme, name)
    return e['hex']


def rgba(theme, name):
    e = tk(theme, name)
    r, g, b = [int(x) for x in e['rgb'].split(',')]
    return f'rgba({r},{g},{b},{e["alpha"]})'


def theme_css(theme):
    t = lambda n: hexv(theme, n)
    sel = f'.t-{theme}'
    e = TOK['elevation'][theme]
    g = TOK['gradients']
    spot = g['spotlightDark' if theme == 'dark' else 'spotlightLight']['css']
    surface = g['midnightSurface' if theme == 'dark' else 'arcticSurface']['css']
    return f'''
{sel}{{
  --bg:{t('bg.app')}; --bg2:{t('bg.secondary')}; --l1:{t('surface.layer1')}; --l2:{t('surface.layer2')}; --l3:{t('surface.layer3')};
  --fg:{t('fg.primary')}; --fg2:{t('fg.secondary')}; --fgm:{t('fg.muted')}; --fgd:{t('fg.disabled')};
  --brand:{t('brand.primary')}; --brand-h:{t('brand.primary.hover')}; --brand-p:{t('brand.primary.pressed')}; --on-brand:{t('brand.on-primary')};
  --sec:{t('brand.secondary')}; --sec-t:{t('brand.secondary.text')}; --sec-s:{t('brand.secondary.solid')}; --on-sec:{t('brand.secondary.on-solid')};
  --bsub:{t('border.subtle')}; --bctl:{t('border.control')}; --div:{t('divider')};
  --info:{t('status.info')}; --ok:{t('status.success')}; --warn:{t('status.warning')}; --err:{t('status.error')};
  --rec:{t('rec.active')}; --recp:{t('rec.paused')}; --recx:{t('rec.exporting')}; --recd:{t('rec.export-complete')};
  --hover:{rgba(theme,'state.hover')}; --press:{rgba(theme,'state.pressed')}; --sel:{rgba(theme,'state.selected')};
  --scrim:{rgba(theme,'surface.scrim')}; --overlay:{t('surface.overlay')};
  --e1:{e['e1']}; --e2:{e['e2']}; --eo:{e['overlay']};
  --spot:{spot}; --surf:{surface};
  background:var(--bg); color:var(--fg);
}}'''


CSS = '''
@font-face{font-family:Inter;font-weight:300;src:url(fonts/Inter-Light.ttf)}
@font-face{font-family:Inter;font-weight:400;src:url(fonts/Inter-Regular.ttf)}
@font-face{font-family:Inter;font-weight:500;src:url(fonts/Inter-Medium.ttf)}
@font-face{font-family:Inter;font-weight:600;src:url(fonts/Inter-SemiBold.ttf)}
@font-face{font-family:Inter;font-weight:700;src:url(fonts/Inter-Bold.ttf)}
@font-face{font-family:'Cascadia Mono';font-weight:400;src:url(fonts/CascadiaMono.ttf)}
@page{size:1920px 1080px;margin:0}
*{box-sizing:border-box;margin:0;padding:0}
html,body{background:#000}
body{font-family:Inter,'Segoe UI',sans-serif;-webkit-font-smoothing:antialiased;font-feature-settings:'cv11','ss03'}
.page{width:1920px;height:1080px;position:relative;overflow:hidden;page-break-after:always;break-after:page;
  background-image:var(--surf)}
.page::before{content:'';position:absolute;inset:0;background:var(--spot);opacity:.0;pointer-events:none}
.page.glow::before{opacity:1}
.page.grid::after{content:'';position:absolute;inset:0;pointer-events:none;
  background-image:linear-gradient(var(--div) 1px,transparent 1px),linear-gradient(90deg,var(--div) 1px,transparent 1px);
  background-size:96px 96px;background-position:-1px -1px;opacity:.55;-webkit-mask-image:radial-gradient(70% 70% at 80% 20%,#000,transparent 80%);mask-image:radial-gradient(70% 70% at 80% 20%,#000,transparent 80%)}
.hdr,.ftr{position:absolute;left:96px;right:96px;display:flex;justify-content:space-between;align-items:center;font-size:14px;font-weight:600;letter-spacing:.16em;text-transform:uppercase;color:var(--fgm);z-index:3}
.hdr{top:44px}.ftr{bottom:40px}
.hdr b{color:var(--brand);font-weight:700}
.hdr .logo{display:flex;align-items:center;gap:12px;color:var(--fg2)}
.hdr .logo img{height:24px;width:auto}
.body{position:absolute;left:96px;right:96px;top:112px;bottom:92px;z-index:2}
.fit{transform-origin:0 0;position:relative}
h1,h2,h3,h4{font-weight:700;letter-spacing:-.02em;line-height:1.1}
.hero{font-size:132px;font-weight:700;letter-spacing:-.045em;line-height:.98}
.display{font-size:84px;font-weight:700;letter-spacing:-.04em;line-height:1.02}
.h1{font-size:62px;font-weight:700;letter-spacing:-.035em;line-height:1.06}
.h2{font-size:42px;font-weight:650;letter-spacing:-.025em;line-height:1.15}
.h3{font-size:30px;font-weight:600;letter-spacing:-.015em;line-height:1.25}
.h4{font-size:23px;font-weight:600;letter-spacing:-.01em;line-height:1.3}
.lead{font-size:29px;line-height:1.5;color:var(--fg2);font-weight:400;letter-spacing:-.005em}
.p{font-size:22px;line-height:1.6;color:var(--fg2)}
.p b,.lead b{color:var(--fg);font-weight:600}
.small{font-size:18.5px;line-height:1.55;color:var(--fg2)}
.cap{font-size:15.5px;line-height:1.5;color:var(--fgm)}
.eyebrow{font-size:16.5px;font-weight:600;letter-spacing:.18em;text-transform:uppercase;color:var(--brand)}
.mono{font-family:'Cascadia Mono',Consolas,monospace}
.brandtext{color:var(--brand)}
.sectext{color:var(--sec-t)}
.row{display:flex;gap:32px}.col{display:flex;flex-direction:column}
.g2{display:grid;grid-template-columns:1fr 1fr;gap:32px}.g3{display:grid;grid-template-columns:repeat(3,1fr);gap:28px}.g4{display:grid;grid-template-columns:repeat(4,1fr);gap:24px}
.g12{display:grid;grid-template-columns:repeat(12,1fr);column-gap:24px}
.card{background:var(--l1);border:1px solid var(--bsub);border-radius:16px;padding:32px}
.card.l2{background:var(--l2)}
.card.flush{padding:0;overflow:hidden}
.rule{height:1px;background:var(--div);border:0}
.tag{display:inline-flex;align-items:center;gap:8px;font-size:13.5px;font-weight:700;letter-spacing:.12em;text-transform:uppercase;padding:5px 11px;border-radius:999px;border:1px solid currentColor;line-height:1}
.tag.existing{color:var(--ok)}.tag.proposed{color:var(--brand)}.tag.concept{color:var(--sec-t)}.tag.derived{color:var(--info)}
.tag::before{content:'';width:7px;height:7px;border-radius:50%;background:currentColor}
.tbl{width:100%;border-collapse:collapse;font-size:18.5px}
.tbl th{font-size:13.5px;letter-spacing:.14em;text-transform:uppercase;color:var(--fgm);font-weight:600;text-align:left;padding:10px 14px;border-bottom:1px solid var(--bsub)}
.tbl td{padding:14px 16px;border-bottom:1px solid var(--div);color:var(--fg2);vertical-align:middle}
.tbl td:first-child{color:var(--fg);font-weight:500}
.tbl.tight td{padding:4.5px 14px}.tbl.tight th{padding:6px 14px}
.tbl .num{font-variant-numeric:tabular-nums;text-align:right}
.sw{display:inline-block;width:22px;height:22px;border-radius:6px;border:1px solid rgba(128,128,128,.45);vertical-align:middle;margin-right:10px}
.pass{color:var(--ok);font-weight:700}.failt{color:var(--err);font-weight:700}
.shot{border-radius:12px;overflow:hidden;border:1px solid var(--bsub);box-shadow:var(--e2);background:var(--l1);line-height:0}
.shot img{width:100%;height:auto;display:block}
.list{list-style:none}.list li{position:relative;padding-left:28px;margin:12px 0;font-size:21px;line-height:1.5;color:var(--fg2)}
.list li::before{content:'';position:absolute;left:0;top:.62em;width:10px;height:2px;background:var(--brand)}
.list.sm li{font-size:18.5px;margin:9px 0}
.chip{display:inline-flex;align-items:center;gap:9px;height:40px;padding:0 18px;border-radius:999px;background:var(--l2);border:1px solid var(--bsub);font-size:17px;font-weight:500;color:var(--fg)}
.k{display:inline-block;font-family:'Cascadia Mono',monospace;font-size:14px;padding:2px 8px;border-radius:6px;background:var(--l2);border:1px solid var(--bctl);color:var(--fg)}
.icon{width:1em;height:1em;display:inline-block;vertical-align:middle}
.num-big{font-size:96px;font-weight:300;letter-spacing:-.05em;color:var(--brand);line-height:1}
.spectrum{height:6px;border-radius:6px;background:linear-gradient(90deg,#FFAA21,#EA0B7F,#971A8D,#1AABE3,#85C536)}
.split{position:absolute;inset:0;display:grid;grid-template-columns:1fr 1fr}
.half{position:relative;overflow:hidden}
.do{border-left:3px solid var(--ok);padding-left:16px}.dont{border-left:3px solid var(--err);padding-left:16px}
.big-quote{font-size:50px;line-height:1.2;letter-spacing:-.03em;font-weight:600}
a{color:inherit;text-decoration:none}
.toc a{display:flex;justify-content:space-between;align-items:baseline;padding:14px 0;border-bottom:1px solid var(--div);font-size:24px;font-weight:500}
.toc a span:last-child{color:var(--fgm);font-variant-numeric:tabular-nums;font-weight:400}
.toc small{color:var(--brand);font-weight:700;letter-spacing:.14em;margin-right:18px;font-size:14px}
'''

# ---- component specimens (scale 1.0 = real application geometry: 36px controls, 8px radius) --------------
SPEC_CSS = '''
.spec{zoom:1.32;font-size:13px;color:var(--fg)}
.btn{display:inline-flex;align-items:center;justify-content:center;gap:8px;height:36px;padding:0 16px;border-radius:8px;font:600 13px Inter;border:1px solid transparent;white-space:nowrap}
.btn svg{width:16px;height:16px}
.btn.primary{background:var(--brand);color:var(--on-brand)}
.btn.primary.hv{background:var(--brand-h)}.btn.primary.ac{background:var(--brand-p)}
.btn.secondary{background:var(--l1);color:var(--fg);border-color:var(--bctl)}
.btn.secondary.hv{background:var(--l2)}.btn.secondary.ac{background:var(--l3)}
.btn.tertiary{background:transparent;color:var(--fg2)}.btn.tertiary.hv{background:var(--hover);color:var(--fg)}.btn.tertiary.ac{background:var(--press);color:var(--fg)}
.btn.danger{background:var(--err);color:var(--bg)}
.btn.dis{background:var(--l2);color:var(--fgd);border-color:transparent}
.btn.foc{outline:2px solid var(--brand);outline-offset:2px}
.btn.sm{height:30px;padding:0 12px;font-size:12.5px}.btn.lg{height:44px;padding:0 22px;font-size:14px}
.inp{height:36px;min-width:200px;display:inline-flex;align-items:center;padding:0 12px;border-radius:8px;background:var(--l2);border:1px solid var(--bctl);font:400 13px Inter;color:var(--fg)}
.inp.ph{color:var(--fgm)}.inp.hv{border-color:var(--fg2)}.inp.foc{border-color:var(--brand);outline:2px solid var(--brand);outline-offset:1px}
.inp.err{border-color:var(--err)}.inp.dis{background:var(--l2);color:var(--fgd);border-color:var(--bsub)}
.sw-t{width:40px;height:22px;border-radius:999px;background:var(--bctl);position:relative;display:inline-block}
.sw-t::after{content:'';position:absolute;top:3px;left:3px;width:16px;height:16px;border-radius:50%;background:var(--bg)}
.sw-t.on{background:var(--brand)}.sw-t.on::after{left:21px;background:var(--on-brand)}
.sw-t.dis{opacity:.45}
.sld{width:200px;height:4px;border-radius:2px;background:var(--l3);position:relative;display:inline-block;vertical-align:middle}
.sld i{position:absolute;left:0;top:0;bottom:0;border-radius:2px;background:var(--brand)}
.sld b{position:absolute;top:-6px;width:16px;height:16px;border-radius:50%;background:var(--fg);border:1px solid var(--bctl)}
.cb{width:18px;height:18px;border-radius:5px;border:1.5px solid var(--bctl);display:inline-flex;align-items:center;justify-content:center;vertical-align:middle;background:var(--l2)}
.cb.on{background:var(--brand);border-color:var(--brand);color:var(--on-brand)}.cb svg{width:12px;height:12px}
.seg{display:inline-flex;padding:3px;border-radius:9px;background:var(--l2);border:1px solid var(--bsub);gap:2px}
.seg span{padding:0 14px;height:28px;display:inline-flex;align-items:center;border-radius:6px;font-weight:500;color:var(--fg2);font-size:12.5px}
.seg span.on{background:var(--l1);color:var(--fg);box-shadow:var(--e1)}
.nav{width:232px;padding:10px;display:flex;flex-direction:column;gap:2px;background:var(--bg);border-radius:12px;border:1px solid var(--bsub)}
.nav .ovl{font-size:10.5px;font-weight:600;letter-spacing:.8px;text-transform:uppercase;color:var(--fgm);padding:10px 12px 6px}
.nav .it{height:36px;border-radius:8px;display:flex;align-items:center;gap:12px;padding:0 12px;color:var(--fg2);font-weight:500;position:relative}
.nav .it svg{width:18px;height:18px}.nav .it.hv{background:var(--hover);color:var(--fg)}
.nav .it.on{background:var(--sel);color:var(--fg)}.nav .it.on::before{content:'';position:absolute;left:-10px;top:8px;bottom:8px;width:3px;border-radius:3px;background:var(--brand)}
.nav .it.on svg{color:var(--brand)}
.badge{display:inline-flex;align-items:center;gap:7px;height:24px;padding:0 10px;border-radius:999px;font-size:12px;font-weight:600;background:var(--l2)}
.badge i{width:7px;height:7px;border-radius:50%;background:currentColor}
.rec-btn{display:inline-flex;align-items:center;gap:8px;height:30px;padding:0 14px;border-radius:8px;background:var(--brand);color:var(--on-brand);font-weight:600;font-size:12.5px}
.rec-btn i{width:7px;height:7px;border-radius:50%;background:currentColor}
.meter{display:inline-flex;gap:3px;vertical-align:middle}.meter i{width:7px;height:14px;border-radius:2px;background:var(--l3)}
.meter i.g{background:var(--ok)}.meter i.y{background:var(--warn)}.meter i.r{background:var(--err)}
.src{width:200px;border-radius:12px;background:var(--l1);border:1px solid var(--bsub);padding:12px}
.src.on{border-color:var(--brand);box-shadow:0 0 0 1px var(--brand)}
.src .th{height:84px;border-radius:8px;background:var(--l2);margin-bottom:10px;display:flex;align-items:center;justify-content:center;color:var(--fgm)}
.src .th svg{width:26px;height:26px}
.tip{display:inline-block;padding:6px 10px 7px;border-radius:8px;background:var(--overlay);border:1px solid var(--bctl);box-shadow:var(--eo);font-size:12px;color:var(--fg)}
.dlg{width:380px;border-radius:14px;background:var(--overlay);border:1px solid var(--bsub);box-shadow:var(--eo);padding:24px}
.dlg h4{font-size:16px;font-weight:600;margin-bottom:6px;letter-spacing:-.01em}.dlg p{font-size:13px;color:var(--fg2);line-height:1.5}
.dlg .acts{display:flex;justify-content:flex-end;gap:10px;margin-top:20px}
.toast{display:inline-flex;align-items:center;gap:12px;padding:12px 16px;border-radius:12px;background:var(--overlay);border:1px solid var(--bsub);box-shadow:var(--eo);font-size:13px;min-width:320px}
.toast svg{width:18px;height:18px}
.prog{height:6px;border-radius:3px;background:var(--l3);width:260px;position:relative;overflow:hidden;display:inline-block;vertical-align:middle}
.prog i{position:absolute;left:0;top:0;bottom:0;background:var(--recx);border-radius:3px}
.tl{width:520px;height:64px;border-radius:10px;background:var(--l2);border:1px solid var(--bsub);position:relative;overflow:hidden}
.tl .sel{position:absolute;top:0;bottom:0;left:60px;right:90px;background:var(--sel);border-left:4px solid var(--brand);border-right:4px solid var(--brand)}
.tl .ph{position:absolute;top:-2px;bottom:-2px;left:180px;width:2px;background:var(--fg)}
.tl .th{position:absolute;top:8px;bottom:8px;width:44px;border-radius:4px;background:var(--l3)}
.skel{border-radius:6px;background:linear-gradient(90deg,var(--l2),var(--l3),var(--l2));height:12px}
.empty{width:340px;padding:26px;text-align:center;border-radius:12px;background:var(--l1);border:1px solid var(--bsub)}
.empty .ic{width:48px;height:48px;border-radius:12px;background:var(--l2);margin:0 auto 12px;display:flex;align-items:center;justify-content:center;color:var(--fg2)}
.empty .ic svg{width:22px;height:22px}.empty b{display:block;font-size:14px;font-weight:600;margin-bottom:4px}.empty span{font-size:12.5px;color:var(--fg2)}
.lbl{font-size:12.5px;font-weight:500;color:var(--fg2);margin-bottom:6px}
.cell{display:flex;flex-direction:column;gap:6px;align-items:flex-start}
.cell small{font-size:10.5px;font-weight:600;letter-spacing:.08em;text-transform:uppercase;color:var(--fgm)}
'''


def css():
    return CSS + SPEC_CSS + theme_css('dark') + theme_css('light')


# ---- helpers ----------------------------------------------------------------------------------------------
def icon(name, size=None, color=None, stroke=1.75):
    d = ICONS[name]
    style = ''
    if size:
        style += f'width:{size}px;height:{size}px;'
    if color:
        style += f'color:{color};'
    if name.startswith('Fill.'):
        return f'<svg class="icon" style="{style}" viewBox="0 0 24 24" fill="currentColor"><path d="{d}"/></svg>'
    return (f'<svg class="icon" style="{style}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="{stroke}" '
            f'stroke-linecap="round" stroke-linejoin="round"><path d="{d}"/></svg>')


def tag(kind):
    return f'<span class="tag {kind.lower()}">{kind.upper()}</span>'


def img(path, cls='', style='', alt=''):
    return f'<img src="{ASSETS}/{path}" class="{cls}" style="{style}" alt="{escape(alt)}">'


def shot(theme, name, cap=None, style=''):
    html = f'<div class="shot" style="{style}">{img(f"screenshots/{theme}/{name}.png", alt=name)}</div>'
    if cap:
        html += f'<div class="cap" style="margin-top:10px">{cap}</div>'
    return html


def swatch_card(theme, name, big=False, label=None):
    e = tk(theme, name)
    text_on = hexv(theme, 'brand.on-primary') if name.startswith('brand.primary') else None
    return f'''<div style="border-radius:12px;overflow:hidden;border:1px solid var(--bsub);background:var(--l1)">
 <div style="height:{120 if big else 76}px;background:{e['hex']}"></div>
 <div style="padding:12px 14px"><div style="font-size:15px;font-weight:600;color:var(--fg)">{label or name}</div>
 <div class="mono" style="font-size:13px;color:var(--fg2);margin-top:3px">{e['hex']} · {e['rgb']}</div></div></div>'''


# page model --------------------------------------------------------------------------------------------------
class Page:
    def __init__(self, theme, chapter, title, body, glow=False, grid=False, chrome=True, key=None, toc=None):
        self.theme, self.chapter, self.title, self.body = theme, chapter, title, body
        self.glow, self.grid, self.chrome, self.key, self.toc = glow, grid, chrome, key, toc


def render_page(p, idx, total):
    logo = ('logo/derived/capit-lockup-stacked-reversed.png' if p.theme == 'dark' else 'logo/existing/capit-lockup-stacked.png')
    sym = 'logo/existing/capit-symbol.png'
    chrome = ''
    if p.chrome:
        chrome = f'''<div class="hdr"><span class="logo">{img(sym)}<span>Cap-IT Brand Book</span></span><span><b>{escape(p.chapter)}</b></span></div>
<div class="ftr"><span>Midnight &amp; Arctic Precision · v1.0 · 2026-10-10</span><span>{idx:02d} / {total:02d}</span></div>'''
    cls = f'page t-{p.theme}' + (' glow' if p.glow else '') + (' grid' if p.grid else '')
    inner = p.body if (p.key == 'full' or not p.chrome) else f'<div class="body"><div class="fit">{p.body}</div></div>'
    return f'<section class="{cls}" id="p{idx:02d}">{chrome}{inner}</section>'
