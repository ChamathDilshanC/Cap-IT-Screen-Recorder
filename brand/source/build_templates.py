"""Generates the marketing / social / install artwork templates (PNG, Midnight and Arctic) from HTML via headless Edge.
Every template is built from the same tokens and the existing logo artwork; real application screenshots are used where a product shot appears."""
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from lib import css  # noqa: E402

HERE = Path(__file__).resolve().parent
ASSETS = HERE.parent / 'Cap-IT-Brand-Assets'
OUT = ASSETS / 'templates'
EDGE = r'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'

# name: (w, h, layout, headline, sub, note)
SPECS = {
    'github-social-preview': (1280, 640, 'wide', 'Capture with intention.', 'A focused, GPU-assisted screen recorder for Windows.', 'GitHub social preview 1280x640'),
    'youtube-thumbnail': (1280, 720, 'thumb', 'Record it. Look good.', 'Smart Tracking zoom · Review & Export', 'YouTube thumbnail 1280x720'),
    'youtube-banner': (2560, 1440, 'banner', 'Capture with intention.', 'Tutorials, demos and bug reports for Windows.', 'YouTube channel banner 2560x1440, safe area 1546x423 centred'),
    'x-post': (1600, 900, 'wide', 'From screen to story.', 'Cap-IT Screen Recorder for Windows', 'X / Twitter image 16:9'),
    'linkedin-post': (1200, 627, 'wide', 'Make every recording count.', 'Native Windows screen recording with a camera that follows the action.', 'LinkedIn image 1.91:1'),
    'instagram-post': (1080, 1350, 'portrait', 'Record. Refine. Share.', 'One app, from capture to export.', 'Instagram portrait 4:5'),
    'product-hunt-gallery': (1270, 760, 'wide', 'Capture beautifully.', 'Smart Tracking · cursor effects · MP4 & GIF', 'Product Hunt gallery 1270x760'),
    'email-header': (1200, 400, 'strip', 'Cap-IT v3.13.1', 'Smoother Smart Tracking · sharper live preview', 'Email header 1200x400 (shown at 600x200)'),
    'release-announcement': (1920, 1080, 'release', 'Cap-IT 3.13.1', 'Smoother Smart Tracking motion and sharper live preview scaling.', 'Release announcement artwork'),
    'tutorial-title-card': (1920, 1080, 'title', 'Your tutorial title', 'Recorded with Cap-IT', 'Tutorial title card'),
    'video-outro': (1920, 1080, 'outro', 'Thanks for watching', 'Recorded and edited with Cap-IT Screen Recorder', 'Video outro card'),
    'desktop-wallpaper': (3840, 2160, 'wallpaper', '', '', 'Desktop wallpaper 3840x2160'),
    'download-banner': (1600, 400, 'download', 'Download Cap-IT for Windows', 'Windows 10 / 11 · x64 · self-contained installer', 'Download banner'),
}


def brackets(w, h, sw, arm, color):
    a = arm
    return (f'<svg viewBox="0 0 {w} {h}" width="{w}" height="{h}" fill="none" stroke="{color}" stroke-width="{sw}" stroke-linecap="round" stroke-linejoin="round">'
            f'<path d="M{sw} {a}V{sw+16}a16 16 0 0 1 16-16H{a}"/><path d="M{w-a} {sw}h{a-sw-16}a16 16 0 0 1 16 16V{a}"/>'
            f'<path d="M{w-sw} {h-a}v{a-sw-16}a16 16 0 0 1-16 16H{w-a}"/><path d="M{a} {h-sw}H{sw+16}a16 16 0 0 1-16-16V{h-a}"/></svg>')


def furl(rel):
    return 'file:///' + (ASSETS / rel).as_posix()


def html_for(name, theme):
    w, h, layout, head, sub, note = SPECS[name]
    if layout in ('strip', 'download'):
        u = h / 400
    elif layout == 'portrait':
        u = w / 1080
    else:
        u = min(w / 1920, h / 1080)
    px = lambda v: f'{round(v * u, 1)}px'
    shot = furl(f'screenshots/{theme}/smart-tracking.png')
    shot_home = furl(f'screenshots/{theme}/home.png')
    sym = furl('logo/existing/capit-symbol.png')
    col = 'var(--brand)'
    glow_a = 'rgba(24,220,232,.20)' if theme == 'dark' else 'rgba(8,124,136,.12)'
    glow_b = 'rgba(124,92,255,.20)' if theme == 'dark' else 'rgba(105,65,198,.10)'
    glow = f'<div style="position:absolute;inset:0;background:radial-gradient(60% 80% at 85% 10%,{glow_a},transparent 65%),radial-gradient(40% 50% at 100% 100%,{glow_b},transparent 70%)"></div>'
    brand = (f'<div style="position:absolute;left:{px(72)};top:{px(60)};display:flex;align-items:center;gap:{px(14)}">'
             f'<img src="{sym}" style="height:{px(46)}"><span style="font-size:{px(26)};font-weight:600;letter-spacing:-.01em">Cap-IT</span></div>')
    frame = (lambda x, y, ww, hh: f'<div style="position:absolute;left:{x};top:{y};width:{ww};height:{hh};border-radius:{px(18)};overflow:hidden;'
             f'border:1px solid var(--bsub);box-shadow:var(--e2)"><img src="{shot}" style="width:100%;display:block"></div>')
    base = (f'html,body{{margin:0;width:{w}px;height:{h}px;overflow:hidden}} '
            f'.t{{position:relative;width:{w}px;height:{h}px;background-image:var(--surf);color:var(--fg);font-family:Inter,sans-serif;overflow:hidden}}')
    hl = head.replace('intention.', f'<span style="color:{col}">intention.</span>')
    inner = ''
    if layout in ('wide', 'thumb', 'release'):
        big = 118 if layout != 'release' else 132
        inner = (f'{glow}{brand}'
                 f'<div style="position:absolute;left:{px(72)};top:{px(250)};width:{px(920)}"><div style="font-size:{px(big)};font-weight:700;letter-spacing:-.04em;line-height:1">{hl}</div>'
                 f'<div style="font-size:{px(34)};line-height:1.4;color:var(--fg2);margin-top:{px(26)}">{sub}</div></div>'
                 f'{frame(px(1010), px(200), px(840), px(470))}'
                 f'<div style="position:absolute;right:{px(60)};top:{px(150)};color:{col}">{brackets(round(940 * u), round(560 * u), 4, round(80 * u), "currentColor")}</div>'
                 f'<div style="position:absolute;left:{px(72)};bottom:{px(56)}"><span style="font-size:{px(18)};letter-spacing:.16em;font-weight:600;color:var(--fgm);text-transform:uppercase">Windows 10 / 11 · x64</span></div>')
    elif layout == 'banner':
        inner = (f'{glow}<div style="position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);width:1546px;height:423px;display:flex;align-items:center;gap:60px">'
                 f'<img src="{sym}" style="height:300px"><div><div style="font-size:120px;font-weight:700;letter-spacing:-.04em;line-height:1">Capture with <span style="color:{col}">intention.</span></div>'
                 f'<div style="font-size:38px;color:var(--fg2);margin-top:18px">{sub}</div></div></div>')
    elif layout == 'portrait':
        inner = (f'{glow}{brand}<div style="position:absolute;left:{px(72)};top:{px(210)};width:{px(930)}">'
                 f'<div style="font-size:{px(150)};font-weight:700;letter-spacing:-.045em;line-height:.98">Record.<br>Refine.<br><span style="color:{col}">Share.</span></div>'
                 f'<div style="font-size:{px(38)};color:var(--fg2);margin-top:{px(36)}">{sub}</div></div>'
                 f'<div style="position:absolute;left:{px(72)};right:{px(72)};bottom:{px(80)};border-radius:{px(22)};overflow:hidden;border:1px solid var(--bsub);box-shadow:var(--e2)"><img src="{shot_home}" style="width:100%;display:block"></div>')
    elif layout == 'strip':
        inner = (f'{glow}<div style="position:absolute;left:{px(48)};top:50%;transform:translateY(-50%);display:flex;align-items:center;gap:{px(30)}"><img src="{sym}" style="height:{px(190)}">'
                 f'<div><div style="font-size:{px(86)};font-weight:700;letter-spacing:-.04em">{head}</div><div style="font-size:{px(34)};color:var(--fg2);margin-top:{px(10)}">{sub}</div></div></div>'
                 f'<div style="position:absolute;right:{px(40)};top:50%;transform:translateY(-50%);color:{col}">{brackets(round(300 * u), round(220 * u), 3, round(40 * u), "currentColor")}</div>')
    elif layout == 'download':
        inner = (f'{glow}<div style="position:absolute;left:{px(60)};top:50%;transform:translateY(-50%);display:flex;align-items:center;gap:{px(40)}"><img src="{sym}" style="height:{px(220)}">'
                 f'<div style="max-width:{px(860)}"><div style="font-size:{px(60)};font-weight:700;letter-spacing:-.035em;line-height:1.05">{head}</div><div style="font-size:{px(26)};color:var(--fg2);margin-top:{px(10)}">{sub}</div></div></div>'
                 f'<div style="position:absolute;right:{px(60)};top:50%;transform:translateY(-50%);height:{px(88)};padding:0 {px(40)};border-radius:{px(16)};background:var(--brand);color:var(--on-brand);font-size:{px(34)};font-weight:600;display:flex;align-items:center">Download for Windows</div>')
    elif layout == 'title':
        inner = (f'{glow}{brand}<div style="position:absolute;left:{px(160)};bottom:{px(220)};right:{px(160)}"><div style="font-size:{px(36)};letter-spacing:.18em;text-transform:uppercase;color:{col};font-weight:600">Tutorial · Part 1</div>'
                 f'<div style="font-size:{px(150)};font-weight:700;letter-spacing:-.045em;line-height:1;margin-top:{px(20)}">{head}</div><div style="font-size:{px(40)};color:var(--fg2);margin-top:{px(26)}">{sub}</div></div>'
                 f'<div style="position:absolute;right:{px(120)};top:{px(110)};color:{col}">{brackets(round(520 * u), round(340 * u), 4, round(70 * u), "currentColor")}</div>')
    elif layout == 'outro':
        inner = (f'{glow}<div style="position:absolute;inset:0;display:flex;flex-direction:column;align-items:center;justify-content:center;text-align:center"><img src="{sym}" style="height:{px(250)}">'
                 f'<div style="font-size:{px(110)};font-weight:700;letter-spacing:-.04em;margin-top:{px(40)}">{head}</div><div style="font-size:{px(38)};color:var(--fg2);margin-top:{px(16)}">{sub}</div>'
                 f'<div style="font-size:{px(26)};letter-spacing:.14em;color:var(--fgm);margin-top:{px(60)};text-transform:uppercase">Capture with intention.</div></div>')
    elif layout == 'wallpaper':
        wa = 'rgba(24,220,232,.22)' if theme == 'dark' else 'rgba(8,124,136,.14)'
        wb = 'rgba(124,92,255,.22)' if theme == 'dark' else 'rgba(105,65,198,.12)'
        inner = (f'<div style="position:absolute;inset:0;background:radial-gradient(55% 70% at 78% 30%,{wa},transparent 65%),radial-gradient(45% 55% at 20% 90%,{wb},transparent 70%)"></div>'
                 f'<div style="position:absolute;inset:0;background-image:linear-gradient(var(--div) 1px,transparent 1px),linear-gradient(90deg,var(--div) 1px,transparent 1px);background-size:160px 160px;opacity:.5;-webkit-mask-image:radial-gradient(60% 60% at 70% 40%,#000,transparent 80%)"></div>'
                 f'<div style="position:absolute;right:560px;top:760px;color:{col}">{brackets(1100, 700, 8, 150, "currentColor")}</div><img src="{sym}" style="position:absolute;right:790px;top:900px;height:420px">'
                 f'<div style="position:absolute;left:200px;bottom:200px;font-size:60px;letter-spacing:.2em;color:var(--fgm);text-transform:uppercase;font-weight:600">Capture with intention.</div>')
    fonts = css().replace('url(fonts/', f'url(file:///{(HERE / "fonts").as_posix()}/')
    return f'<!doctype html><html><head><meta charset="utf-8"><style>{fonts}{base}</style></head><body><div class="t t-{theme}">{inner}</div></body></html>'


def shoot(html, png, w, h):
    tmp = png.with_suffix('.html')
    tmp.write_text(html, encoding='utf-8')
    subprocess.run([EDGE, '--headless=new', '--disable-gpu', '--hide-scrollbars', '--force-device-scale-factor=1', f'--window-size={w},{h}',
                    f'--screenshot={png}', tmp.resolve().as_uri(), '--virtual-time-budget=8000'], check=True, capture_output=True, timeout=120)
    tmp.unlink()


def main():
    for theme in ('dark', 'light'):
        (OUT / theme).mkdir(parents=True, exist_ok=True)
        for name, (w, h, *_rest) in SPECS.items():
            shoot(html_for(name, theme), OUT / theme / f'{name}.png', w, h)
    print('templates:', len(SPECS) * 2)


if __name__ == '__main__':
    main()
