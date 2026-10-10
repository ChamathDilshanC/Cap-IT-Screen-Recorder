"""Chapters 10-13: dual-theme UI system, feature identity, Smart Tracking, product screens."""
from lib import *
from pages_a import bracket_svg

CH10, CH11, CH12, CH13 = '10 · Product UI system', '11 · Feature identity', '12 · Smart Tracking', '13 · Product screens'


def dual(chapter, eyebrow, heading, fn, note=None, key_label='Specimens are drawn from the proposed tokens on the existing geometry (36 px controls, 8 px radius). Shown at 132 %.'):
    def half(theme):
        pad = 'padding:112px 56px 96px 96px' if theme == 'dark' else 'padding:112px 96px 96px 56px'
        return f'''<div class="half t-{theme}" style="background:var(--bg);color:var(--fg);{pad}">
<div class="row" style="align-items:center;justify-content:space-between"><span class="eyebrow">{'Midnight' if theme=='dark' else 'Arctic'}</span>{tag('proposed')}</div>
<h2 class="h2" style="margin:8px 0 18px;font-size:38px">{heading if theme=='dark' else '&nbsp;'}</h2>
{fn(theme)}
{f'<p class="cap" style="margin-top:14px;font-size:14.5px">{note}</p>' if note and theme=='dark' else ''}
{f'<p class="cap" style="margin-top:14px;font-size:14.5px">{key_label}</p>' if theme=='light' else ''}</div>'''
    body = f'<div class="split">{half("dark")}{half("light")}</div><div class="hdr"><span class="logo">{img("logo/existing/capit-symbol.png")}<span>Cap-IT Brand Book</span></span><b>{chapter}</b></div>'
    return Page('dark', chapter, heading, body, chrome=False)


def ic(n, s=16):
    return icon(n, s)


# ------------------------------------------------------------------------------------------------ buttons
def buttons_fn(theme):
    def row(label, cls, content, extra=''):
        cells = ''
        for st, c in (('Default', ''), ('Hover', ' hv'), ('Active', ' ac'), ('Focus', ' foc'), ('Disabled', ' dis')):
            if cls == 'tertiary' and st == 'Disabled':
                c = ' dis'
            cells += f'<div class="cell"><small>{st}</small><span class="btn {cls}{c}">{content}</span></div>'
        return f'<div style="display:grid;grid-template-columns:90px 1fr;align-items:center;margin:11px 0"><div class="cap" style="font-size:13px">{label}</div><div style="display:flex;gap:18px;flex-wrap:wrap">{cells}</div></div>'
    return f'''<div class="spec">
{row('Primary','primary',ic('Fill.Dot',8)+' Start recording')}
{row('Secondary','secondary','Choose source')}
{row('Tertiary','tertiary','Cancel')}
{row('Danger','danger','Delete')}
<div style="display:grid;grid-template-columns:90px 1fr;align-items:center;margin:14px 0"><div class="cap" style="font-size:13px">Sizes</div><div style="display:flex;gap:16px;align-items:center"><span class="btn primary sm">Small 30</span><span class="btn primary">Default 36</span><span class="btn primary lg">Large 44</span></div></div>
<div style="display:grid;grid-template-columns:90px 1fr;align-items:center;margin:14px 0"><div class="cap" style="font-size:13px">Icon · record</div><div style="display:flex;gap:14px;align-items:center"><span class="btn secondary" style="width:36px;padding:0">{ic('Settings',18)}</span><span class="btn tertiary" style="width:36px;padding:0">{ic('Fill.Pause',18)}</span><span class="rec-btn"><i></i>Record</span><span class="btn secondary">{ic('Export',16)} Export video</span></div></div>
</div>'''


def inputs_fn(theme):
    return f'''<div class="spec">
<div style="display:grid;grid-template-columns:repeat(2,1fr);gap:14px 18px">
 <div class="cell"><small>Default</small><span class="inp ph">Search recordings…</span></div>
 <div class="cell"><small>Hover</small><span class="inp hv">Recording_2026</span></div>
 <div class="cell"><small>Focus</small><span class="inp foc">Recording_2026</span></div>
 <div class="cell"><small>Error</small><span class="inp err">Name already used</span></div>
 <div class="cell"><small>Disabled</small><span class="inp dis">Locked</span></div>
 <div class="cell"><small>Numeric</small><span class="inp" style="justify-content:space-between;min-width:150px">1.50 <span style="color:var(--fg2)">{ic('ChevronUp',14)}{ic('ChevronDown',14)}</span></span></div>
 <div class="cell"><small>Dropdown</small><span class="inp" style="justify-content:space-between">Automatic <span style="color:var(--fg2)">{ic('ChevronDown',14)}</span></span></div>
 <div class="cell"><small>Segmented</small><span class="seg"><span>15</span><span>24</span><span class="on">30 FPS</span><span>60</span></span></div>
 <div class="cell"><small>Slider</small><span class="sld"><i style="width:55%"></i><b style="left:calc(55% - 8px)"></b></span></div>
 <div class="cell"><small>Switch</small><div style="display:flex;gap:12px"><span class="sw-t"></span><span class="sw-t on"></span><span class="sw-t on dis"></span></div></div>
 <div class="cell"><small>Checkbox</small><div style="display:flex;gap:12px"><span class="cb"></span><span class="cb on">{ic('Check',12)}</span></div></div>
 <div class="cell"><small>Label + value</small><div class="lbl" style="margin:0">Frame rate</div><b style="font-size:13px">30 FPS</b></div>
</div>
<div style="margin-top:22px"><small class="cap" style="font-size:11px;letter-spacing:.1em;text-transform:uppercase;font-weight:600">Dropdown open</small>
<div style="margin-top:8px;display:inline-block;width:230px;padding:4px;border-radius:12px;background:var(--overlay);border:1px solid var(--bsub);box-shadow:var(--eo)">
 <div style="padding:7px 10px;border-radius:8px;color:var(--fg2)">Automatic (best available)</div><div style="padding:7px 10px;border-radius:8px;background:var(--sel);color:var(--fg)">NVIDIA NVENC</div><div style="padding:7px 10px;border-radius:8px;background:var(--hover);color:var(--fg)">AMD AMF</div><div style="padding:7px 10px;border-radius:8px;color:var(--fg2)">Software (H.264)</div></div></div>
</div>'''


def nav_fn(theme):
    def item(icon_, label, cls=''):
        return f'<div class="it {cls}">{ic(icon_,18)}<span>{label}</span></div>'
    crop = f'<div class="shot" style="width:232px;height:316px;border-radius:12px;background:url({ASSETS}/screenshots/{theme}/smart-tracking.png) 0 -44px / 1500px auto no-repeat"></div>'
    return f'''<div class="spec" style="display:flex;gap:22px;align-items:flex-start">
<div><small class="cap" style="font-size:10.5px;letter-spacing:.1em;text-transform:uppercase;font-weight:600">Sidebar</small><div class="nav" style="margin-top:6px"><div class="ovl">Capture</div>{item('Home','Home')}{item('Monitor','Capture','hv')}{item('Focus','Smart Tracking','on')}{item('Video','Webcam')}{item('Pen','Annotations')}<div class="ovl">Media</div>{item('Film','Recordings')}</div></div>
<div><small class="cap" style="font-size:10.5px;letter-spacing:.1em;text-transform:uppercase;font-weight:600">Real app (existing)</small><div style="margin-top:6px">{crop}</div></div></div>
<div class="spec" style="margin-top:20px"><small class="cap" style="font-size:10.5px;letter-spacing:.1em;text-transform:uppercase;font-weight:600">Title bar</small>
<div style="margin-top:6px;height:44px;border-radius:10px;background:var(--bg2);border:1px solid var(--bsub);display:flex;align-items:center;gap:12px;padding:0 12px;width:520px">{ic('PanelLeft',16)}<b style="font-size:13px">Cap-IT</b><span style="color:var(--fgm)">/</span><span style="color:var(--fg2);font-size:13px">Smart Tracking</span><span style="flex:1"></span><span class="badge" style="color:var(--ok)"><i></i>Ready</span><span class="rec-btn"><i></i>Record</span></div></div>'''


def record_fn(theme):
    def badge(label, color, ic_):
        return f'<span class="badge" style="color:{color}">{ic(ic_,12)}<span style="color:var(--fg)">{label}</span></span>'
    return f'''<div class="spec">
<small class="cap" style="font-size:10.5px;letter-spacing:.1em;text-transform:uppercase;font-weight:600">Floating recording controller · never appears in the recording</small>
<div style="margin:8px 0 22px;display:inline-flex;align-items:center;gap:4px;padding:6px;border-radius:16px;background:var(--l2);border:1px solid var(--bctl);box-shadow:var(--eo)">
 <span style="display:flex;align-items:center;gap:10px;padding:0 12px 0 10px"><i style="width:10px;height:10px;border-radius:50%;background:var(--rec)"></i><span class="mono" style="font-size:14px;font-weight:600;min-width:58px">00:12:04</span></span>
 <i style="width:1px;height:22px;background:var(--div)"></i>
 <span class="btn tertiary" style="width:34px;padding:0">{ic('Fill.Pause',16)}</span><span class="btn tertiary" style="width:34px;padding:0;color:var(--rec)">{ic('Fill.Stop',16)}</span><span class="btn tertiary" style="width:34px;padding:0">{ic('Snowflake',16)}</span>
 <i style="width:1px;height:22px;background:var(--div)"></i>
 <span class="btn tertiary" style="width:34px;padding:0">{ic('Mic',16)}</span><span class="btn tertiary" style="width:34px;padding:0">{ic('Video',16)}</span><span class="btn tertiary" style="width:34px;padding:0">{ic('Pen',16)}</span><span class="btn tertiary" style="width:34px;padding:0;background:var(--sel);color:var(--brand)">{ic('ZoomIn',16)}</span>
 <i style="width:1px;height:22px;background:var(--div)"></i><span class="btn tertiary" style="width:34px;padding:0">{ic('Maximize',16)}</span></div>
<small class="cap" style="font-size:10.5px;letter-spacing:.1em;text-transform:uppercase;font-weight:600;display:block">Status — icon + label, never colour alone</small>
<div style="display:flex;flex-wrap:wrap;gap:10px;margin:8px 0 22px">{badge('Ready','var(--ok)','CheckCircle')}{badge('Recording','var(--rec)','Fill.Dot')}{badge('Paused','var(--recp)','Fill.Pause')}{badge('Exporting','var(--recx)','Export')}{badge('Export complete','var(--recd)','CheckCircle')}{badge('Error','var(--err)','AlertCircle')}</div>
<small class="cap" style="font-size:10.5px;letter-spacing:.1em;text-transform:uppercase;font-weight:600;display:block">Audio level meters</small>
<div style="display:grid;grid-template-columns:110px 1fr;gap:10px 14px;margin-top:8px;align-items:center"><span class="lbl" style="margin:0">System audio</span><span class="meter">{''.join('<i class="g"></i>' for _ in range(8))}{''.join('<i></i>' for _ in range(12))}</span>
<span class="lbl" style="margin:0">Microphone</span><span class="meter">{''.join('<i class="g"></i>' for _ in range(11))}{''.join('<i class="y"></i>' for _ in range(3))}{''.join('<i></i>' for _ in range(6))}</span>
<span class="lbl" style="margin:0">Clipping</span><span class="meter">{''.join('<i class="g"></i>' for _ in range(12))}{''.join('<i class="y"></i>' for _ in range(4))}{''.join('<i class="r"></i>' for _ in range(4))}</span></div></div>'''


def cards_fn(theme):
    return f'''<div class="spec">
<small class="cap" style="font-size:10.5px;letter-spacing:.1em;text-transform:uppercase;font-weight:600">Source selection cards</small>
<div style="display:flex;gap:14px;margin:8px 0 20px"><div class="src on"><div class="th">{ic('Monitor')}</div><b style="font-size:13px">Display 1</b><div class="cap" style="font-size:11.5px">1920×1080 · Primary</div></div><div class="src"><div class="th">{ic('AppWindow')}</div><b style="font-size:13px">Window</b><div class="cap" style="font-size:11.5px">One app, wherever it is</div></div></div>
<div style="display:flex;gap:22px;align-items:flex-start"><div><small class="cap" style="font-size:10.5px;letter-spacing:.1em;text-transform:uppercase;font-weight:600;display:block;margin-bottom:8px">Tooltip</small><span class="tip">Freeze the screen image</span>
<small class="cap" style="font-size:10.5px;letter-spacing:.1em;text-transform:uppercase;font-weight:600;display:block;margin:18px 0 8px">Context menu</small><div style="width:190px;padding:4px;border-radius:12px;background:var(--overlay);border:1px solid var(--bsub);box-shadow:var(--eo)"><div style="padding:7px 10px;border-radius:8px;background:var(--hover)">Open recording</div><div style="padding:7px 10px;color:var(--fg2)">Show in folder</div><div style="padding:7px 10px;color:var(--err)">Delete</div></div></div>
<div><small class="cap" style="font-size:10.5px;letter-spacing:.1em;text-transform:uppercase;font-weight:600;display:block;margin-bottom:8px">Dialog</small><div class="dlg" style="width:300px;padding:18px"><h4>Delete this recording?</h4><p>The video and its edits are moved to the Recycle Bin.</p><div class="acts"><span class="btn tertiary sm">Cancel</span><span class="btn danger sm">Delete</span></div></div></div></div>
<small class="cap" style="font-size:10.5px;letter-spacing:.1em;text-transform:uppercase;font-weight:600;display:block;margin:20px 0 8px">Notifications</small>
<div style="display:flex;flex-direction:column;gap:8px"><span class="toast" style="min-width:0;width:520px"><span style="color:var(--ok)">{ic('CheckCircle',18)}</span>Export complete · Open folder</span><span class="toast" style="min-width:0;width:520px"><span style="color:var(--warn)">{ic('AlertTriangle',18)}</span>Hardware export failed — try Software (H.264)</span><span class="toast" style="min-width:0;width:520px"><span style="color:var(--info)">{ic('Info',18)}</span>Original recording preserved · edits save automatically</span></div></div>'''


def timeline_fn(theme):
    thumbs = ''.join(f'<span class="th" style="left:{8+i*52}px"></span>' for i in range(9))
    return f'''<div class="spec">
<small class="cap" style="font-size:10.5px;letter-spacing:.1em;text-transform:uppercase;font-weight:600">Timeline · trim handles · playhead</small>
<div class="tl" style="margin:8px 0 18px">{thumbs}<span class="sel"></span><span class="ph"></span></div>
<div style="display:flex;gap:20px;align-items:center;margin-bottom:18px"><span class="btn secondary sm">{ic('ZoomIn',14)} Zoom region at playhead</span><span class="inp" style="min-width:90px;height:30px">0.00</span><span class="inp" style="min-width:90px;height:30px">6.00</span></div>
<small class="cap" style="font-size:10.5px;letter-spacing:.1em;text-transform:uppercase;font-weight:600">Progress · export</small>
<div style="margin:8px 0 18px;display:flex;align-items:center;gap:12px"><span class="prog"><i style="width:62%"></i></span><span style="font-size:12.5px;color:var(--fg2)">Exporting 62 %</span></div>
<div style="display:flex;gap:18px">
 <div><small class="cap" style="font-size:10.5px;letter-spacing:.1em;text-transform:uppercase;font-weight:600;display:block;margin-bottom:8px">Loading · skeleton</small><div style="width:130px;display:flex;flex-direction:column;gap:8px"><div class="skel" style="width:60%"></div><div class="skel"></div><div class="skel" style="width:80%"></div></div></div>
 <div><small class="cap" style="font-size:10.5px;letter-spacing:.1em;text-transform:uppercase;font-weight:600;display:block;margin-bottom:8px">Empty</small><div class="empty" style="width:170px;padding:14px"><div class="ic" style="width:36px;height:36px">{ic('Film',18)}</div><b style="font-size:13px">No recordings yet</b><span style="font-size:11.5px">Your first capture will appear here.</span></div></div>
 <div><small class="cap" style="font-size:10.5px;letter-spacing:.1em;text-transform:uppercase;font-weight:600;display:block;margin-bottom:8px">Error</small><div class="empty" style="width:170px;padding:14px;border-color:var(--err)"><div class="ic" style="width:36px;height:36px;color:var(--err)">{ic('AlertCircle',18)}</div><b style="font-size:13px">Couldn’t open this video</b><span style="font-size:11.5px">Check the file and try again.</span><div style="margin-top:8px"><span class="btn secondary sm">Try again</span></div></div></div></div></div>'''


def ui_pages():
    return [
        dual(CH10, 'Buttons', 'Buttons and their states', buttons_fn, 'Primary uses brand.primary with brand.on-primary (≥ 4.5 : 1 in both themes). Focus is a 2 px ring with a 2 px offset.'),
        dual(CH10, 'Inputs', 'Inputs and selection controls', inputs_fn, 'Control outlines use border.control (≥ 3 : 1). Switch and slider fills use brand.primary.'),
        dual(CH10, 'Navigation', 'Sidebar and title bar', nav_fn, 'Active item: selected wash + 3 px accent bar + accent icon — three cues, not colour alone.'),
        dual(CH10, 'Recording', 'Recording controls, status, meters', record_fn, 'The controller follows the shipped layout (grip + timer · transport · live toggles · show app).'),
        dual(CH10, 'Surfaces', 'Cards, overlays, notifications', cards_fn, 'Overlays use surface.overlay plus the overlay shadow; cards use layer 1 with a subtle border.'),
        dual(CH10, 'Editor', 'Timeline, progress, empty/loading/error', timeline_fn, 'Skeletons shimmer only when motion is allowed; otherwise they are static.'),
    ]


# ------------------------------------------------------------------------------------------- features
def feature_matrix():
    feats = TOK['features']
    shots = {'capture': 'capture', 'smart-tracking': 'smart-tracking', 'audio': 'audio', 'webcam': 'webcam', 'effects': 'effects', 'annotations': 'annotations',
             'review-export': 'review-export', 'recordings': 'recordings', 'settings': 'settings'}
    cards = ''
    for f in feats:
        sc = shots[f['key']]
        cards += f'''<div class="card" style="padding:14px;display:grid;grid-template-columns:230px 1fr;gap:18px;align-items:center">
<div class="shot" style="border-radius:10px">{img(f'screenshots/light/{sc}.png')}</div>
<div><div class="row" style="align-items:center;gap:12px;margin-bottom:8px"><div style="width:42px;height:42px;border-radius:11px;background:{f['colorLight']}1F;color:{f['colorLight']};display:flex;align-items:center;justify-content:center;font-size:22px">{icon(f['icon'].replace('Icon.',''))}</div><b style="font-size:21px;color:var(--fg);letter-spacing:-.01em">{f['name']}</b></div>
<div style="font-size:15.5px;line-height:1.45;color:var(--fg2)">{f['benefit']}</div>
<div class="mono" style="margin-top:8px;font-size:13px;color:var(--fgm)">“{f['microcopy']}”</div>
<div class="row" style="gap:6px;margin-top:8px"><span class="sw" style="width:16px;height:16px;background:{f['colorDark']};margin:0"></span><span class="sw" style="width:16px;height:16px;background:{f['colorLight']};margin:0"></span><span class="cap mono" style="font-size:12px">{f['colorDark']} · {f['colorLight']}</span></div></div></div>'''
    body = f'''
<div class="row" style="align-items:center;justify-content:space-between"><div><div class="eyebrow">Feature identity system</div><h1 class="h2" style="margin:8px 0 0">Nine areas, one family.</h1></div><div class="row" style="gap:12px">{tag('existing')}<span class="cap">screens &amp; names</span>{tag('proposed')}<span class="cap">colours &amp; microcopy emphasis</span></div></div>
<p class="p" style="margin:10px 0 16px;max-width:1500px;font-size:19px">Each feature gets an icon (from the existing set), a colour for its icon chip, a one-sentence benefit and a line of its own microcopy. Feature colours are drawn from the logo’s own hues plus brand cyan and violet; they tint chips and small markers only, and stay subordinate to the master brand.</p>
<div class="g3" style="gap:14px">{cards}</div>'''
    return Page('light', CH11, 'Feature identity system', body, toc='11 · Feature identity')


# --------------------------------------------------------------------------------------- Smart Tracking
def st_diagram():
    c = 'var(--brand)'
    return f'''<svg viewBox="0 0 1000 380" style="width:100%;height:350px;display:block" fill="none" stroke-linecap="round" stroke-linejoin="round">
<g stroke="var(--bctl)" stroke-width="2"><rect x="8" y="30" width="420" height="260" rx="14" fill="var(--l1)"/></g>
<g stroke="{c}" stroke-width="4"><rect x="150" y="96" width="210" height="136" rx="8" fill="{c}" fill-opacity=".10"/></g>
<g stroke="{c}" stroke-width="2" stroke-dasharray="3 8" opacity=".6"><path d="M150 96 8 30M360 96l68-66M150 232 8 290M360 232l68 58"/></g>
<path d="M60 230C110 220 160 180 250 160" stroke="{c}" stroke-width="3" opacity=".35" stroke-dasharray="2 8"/><circle cx="250" cy="160" r="7" fill="{c}"/><circle cx="250" cy="160" r="20" stroke="{c}" stroke-width="2" opacity=".5"/>
<text x="8" y="22" fill="var(--fgm)" font-size="15" font-weight="600" letter-spacing="2">FULL DESKTOP (1×)</text>
<text x="150" y="258" fill="{c}" font-size="14" font-weight="600" letter-spacing="1">CAMERA CROP (2×)</text>
<path d="M440 160h70" stroke="{c}" stroke-width="3"/><path d="M500 148l14 12-14 12" stroke="{c}" stroke-width="3"/>
<g stroke="var(--bctl)" stroke-width="2"><rect x="540" y="30" width="450" height="260" rx="14" fill="var(--l1)"/></g>
<rect x="556" y="48" width="418" height="224" rx="8" fill="{c}" fill-opacity=".10" stroke="{c}" stroke-width="3"/>
<text x="540" y="22" fill="var(--fgm)" font-size="15" font-weight="600" letter-spacing="2">WHAT THE VIDEO SHOWS</text>
<g fill="var(--fg2)" font-size="18"><rect x="590" y="90" width="260" height="10" rx="5" fill="var(--l3)"/><rect x="590" y="116" width="330" height="10" rx="5" fill="var(--l3)"/><rect x="590" y="142" width="210" height="10" rx="5" fill="var(--l3)"/></g>
<circle cx="800" cy="146" r="9" fill="{c}"/><path d="M800 146l-6 22 9-5 8 14 4-2-8-14 10-1z" fill="var(--fg)" stroke="none"/>
<path d="M590 220h220" stroke="var(--fg)" stroke-width="3"/><path d="M812 208v26" stroke="{c}" stroke-width="3"/>
<g><text x="8" y="352" fill="var(--fgm)" font-size="15" letter-spacing="1">cursor / text caret</text><text x="540" y="352" fill="var(--fgm)" font-size="15" letter-spacing="1">follows, eases in, eases back out</text></g></svg>'''


def smart_tracking_story():
    steps = [('Interaction', 'You move, click or type.', 'Icon.CursorClick'), ('Focus', 'The camera picks the cursor — or the text caret while you type.', 'Icon.Focus'),
             ('Smooth reframing', 'A critically damped spring eases in, pans and eases back out — no overshoot.', 'Icon.ZoomIn'), ('Clear communication', 'The viewer sees exactly what you are talking about.', 'Icon.Eye')]
    st = ''.join(f'''<div style="flex:1"><div style="width:64px;height:64px;border-radius:18px;background:rgba(124,92,255,.18);color:var(--sec-t);display:flex;align-items:center;justify-content:center;font-size:32px">{icon(n.replace('Icon.',''))}</div>
<div class="eyebrow" style="margin-top:16px;font-size:14px;color:var(--sec-t)">{i+1:02d}</div><div class="h3" style="margin:4px 0 8px">{a}</div><div class="small" style="font-size:17px">{b}</div></div>''' for i, (a, b, n) in enumerate(steps))
    body = f'''
<div style="position:absolute;inset:-100px -100px 0 0;background:radial-gradient(45% 55% at 85% 0%,rgba(124,92,255,.22),transparent 70%),radial-gradient(40% 50% at 10% 100%,rgba(24,220,232,.12),transparent 70%)"></div>
<div style="position:relative">
 <div class="row" style="align-items:center;gap:14px"><span class="eyebrow" style="color:var(--sec-t)">Signature feature</span>{tag('existing')}</div>
 <h1 class="display" style="margin:12px 0 22px;font-size:72px">A camera that follows <span style="color:var(--sec-t)">the action.</span></h1>
 <div class="row" style="gap:36px;margin-bottom:26px">{st}</div>
 <div class="card" style="padding:14px 28px">{st_diagram()}</div>
</div>'''
    return Page('dark', CH12, 'Smart Tracking — the story', body, toc='12 · Smart Tracking')


def smart_tracking_detail():
    t = TOK['motion']['smartTracking']
    facts = [('Zoom levels', '125 · 150 · 175 · 200 · 300 %'), ('Triggers', 'Cursor movement, clicks and typing — or clicks only'), ('Zoom in', f'{t["zoomInSmoothTime"]:.2f} s smooth time'), ('Zoom out', f'{t["zoomOutSmoothTime"]:.2f} s · or instant'),
             ('Pan', f'{t["panSmoothTime"]:.2f} s smooth time'), ('Release', f'{t["idleReleaseSeconds"]:.1f} s idle · {t["clickHoldSeconds"]:.1f} s after a click'), ('Dead zone', f'{t["panDeadZoneFraction"]*100:.0f} % of the crop before the camera re-aims'),
             ('Speed', '0–50 % faster, live, even mid-recording')]
    rows = ''.join(f'<tr><td>{a}</td><td>{b}</td></tr>' for a, b in facts)
    body = f'''
<div class="g12" style="height:100%;align-content:start;row-gap:24px">
 <div style="grid-column:1/6"><div class="row" style="align-items:center;gap:14px"><span class="eyebrow">Smart Tracking in the product</span>{tag('existing')}</div><h1 class="h1" style="margin:10px 0 14px">Behaviour you can describe in one breath.</h1>
  <table class="tbl tight" style="font-size:17px"><tbody>{rows}</tbody></table>
  <p class="cap" style="margin-top:10px">From <span class="mono">VideoCaptureService.cs</span> and the Smart Tracking page. Camera motion is computed from real elapsed time, so it stays smooth when frames arrive unevenly.</p></div>
 <div style="grid-column:7/13"><div class="eyebrow" style="margin-bottom:8px">Real screens</div><div class="g2" style="gap:14px"><div>{shot('dark','smart-tracking')}<div class="cap" style="margin-top:6px">Midnight</div></div><div>{shot('light','smart-tracking')}<div class="cap" style="margin-top:6px">Arctic</div></div></div>
  <div class="card" style="margin-top:18px;padding:22px 26px"><div class="h4" style="margin-bottom:6px">Microcopy (existing)</div><div class="mono" style="font-size:14.5px;line-height:1.7;color:var(--fg2)">“A camera that follows the action: it glides toward your cursor and caret, then eases back out.”<br>“Applies immediately — even mid-recording, the camera eases between modes.”<br>“Motion uses a critically damped spring: smooth acceleration, no overshoot.”</div></div></div>
 <div style="grid-column:1/13" class="card" style="padding:22px 28px"><div class="row" style="gap:16px;align-items:flex-start"><div style="min-width:240px"><div class="h4">What has and has not been verified</div>{tag('existing')}</div>
  <p class="small" style="font-size:17px">The recording-continuity work for Smart Tracking (timeline, tearing, encoder stalls) is documented in <span class="mono">docs/SMART-TRACKING-RECORDING-FIX.md</span> with measurements from one machine (AMD integrated GPU, 1080p). This book makes no performance claim beyond it. <b style="color:var(--fg)">Still open:</b> a literal “cut section” was not reproduced; the audio tail can end up to ~0.8 s short; text sharpness while zoomed is not measured; 1440p, 4K, NVENC and QSV are untested.</p></div></div>
</div>'''
    return Page('light', CH12, 'Smart Tracking — in the product', body)


# --------------------------------------------------------------------------------------- product screens
def screens_page(theme_bg, a, b, title):
    def row(name, label, note):
        dk = shot('dark', name, style='max-width:640px') ; lt = shot('light', name, style='max-width:640px')
        return f'''<div style="display:grid;grid-template-columns:200px 1fr 1fr;gap:22px;align-items:center;margin-bottom:14px">
<div><div class="h3" style="margin-bottom:6px">{label}</div><div class="small" style="font-size:16px">{note}</div></div>
<div>{dk}<div class="cap" style="margin-top:6px;font-size:13.5px"><b style="color:var(--fg)">Midnight</b> · real render</div></div><div>{lt}<div class="cap" style="margin-top:6px;font-size:13.5px"><b style="color:var(--fg)">Arctic</b> · real render</div></div></div>'''
    body = f'''
<div class="row" style="align-items:center;gap:14px;margin-bottom:12px"><span class="eyebrow">Dark | Light</span>{tag('existing')}<span class="cap">Rendered by the application’s own snapshot harness (v3.13.1, 1500×840 / 1280×720) with a clean demo configuration.</span></div>
{row(*a)}{row(*b)}'''
    return Page(theme_bg, CH13, title, body)


def screens():
    return [
        screens_page('dark', ('home', 'Home', 'The dashboard: one obvious action, the source and the setup.'), ('capture', 'Capture', 'Source, quality, encoder and format.'), 'Home and Capture'),
        screens_page('light', ('smart-tracking', 'Smart Tracking', 'Mode, zoom level, speed, keystrokes.'), ('audio', 'Audio', 'System audio and microphone with level meters.'), 'Smart Tracking and Audio'),
        screens_page('dark', ('webcam', 'Webcam', 'Picture-in-picture templates and picture tuning.'), ('effects', 'Effects', 'Spotlight, click ripples and click sounds.'), 'Webcam and Effects'),
        screens_page('light', ('annotations', 'Annotations', 'Tools, style and global shortcuts.'), ('review-video', 'Review & Export', 'Preview, trim timeline, zoom regions, inspector.'), 'Annotations and Review & Export'),
        screens_page('dark', ('recordings', 'Recordings', 'Search, thumbnails, grid and list.'), ('settings', 'Settings', 'General, recording, storage, shortcuts, updates.'), 'Recordings and Settings'),
    ]


def build():
    pages = ui_pages() + [feature_matrix(), smart_tracking_story(), smart_tracking_detail()]
    s = screens()
    s[0].toc = '13 · Product screens'
    pages += s
    pages[0].toc = '10 · Product UI system'
    return pages
