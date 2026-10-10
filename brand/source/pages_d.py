"""Chapters 14-20 and the closing page: marketing, voice, applications, technical, distribution, accessibility, governance."""
from lib import *
from pages_c import dual, ic

CH14, CH15, CH16, CH17, CH18, CH19, CH20 = ('14 · Marketing identity', '15 · Voice & messaging', '16 · Brand applications', '17 · Technical overview',
                                             '18 · Platform & distribution', '19 · Accessibility', '20 · Governance')


def tpl(theme, name, style='width:100%'):
    return f'<div class="shot" style="{style}">{img(f"templates/{theme}/{name}.png")}</div>'


# ------------------------------------------------------------------------------------------------ marketing
def website_hero():
    def fn(theme):
        return f'''<div style="border-radius:14px;overflow:hidden;border:1px solid var(--bsub);box-shadow:var(--e2);background:var(--bg)">
 <div style="height:34px;background:var(--l2);display:flex;align-items:center;gap:8px;padding:0 14px;border-bottom:1px solid var(--bsub)"><i style="width:10px;height:10px;border-radius:50%;background:var(--bctl)"></i><i style="width:10px;height:10px;border-radius:50%;background:var(--bctl)"></i><i style="width:10px;height:10px;border-radius:50%;background:var(--bctl)"></i><span class="cap" style="margin-left:14px;font-size:12px">cap-it · concept</span></div>
 <div style="padding:26px 36px 0;position:relative;background-image:var(--surf)"><div style="position:absolute;inset:0;background:var(--spot)"></div>
  <div style="position:relative"><div class="row" style="align-items:center;gap:10px">{img('logo/existing/capit-symbol.png','', 'height:30px')}<b style="font-size:17px">Cap-IT</b><span style="flex:1"></span><span class="cap" style="font-size:13px">Features · Download · Releases</span></div>
  <div class="eyebrow" style="margin-top:22px;font-size:13px">Native screen recorder for Windows</div>
  <div style="font-size:48px;font-weight:700;letter-spacing:-.04em;line-height:1.02;margin:8px 0 10px">Capture with<br><span class="brandtext">intention.</span></div>
  <div style="font-size:17px;line-height:1.5;color:var(--fg2);max-width:430px">A camera that follows the action, effects that make clicks readable, and a workspace to finish — in one app.</div>
  <div class="row" style="gap:12px;margin-top:20px"><span class="btn primary lg" style="font-size:14px">{ic('Download',16)} Download for Windows</span><span class="btn secondary lg" style="font-size:14px">See screenshots</span></div>
  <div class="cap" style="margin-top:10px;font-size:12.5px">v3.13.1 · Windows 10 / 11 x64 · self-contained installer</div>
  <div style="margin:18px -36px 0;padding:0 36px;height:190px;overflow:hidden">{img(f'screenshots/{theme}/smart-tracking.png','', 'width:100%;border-radius:12px 12px 0 0;border:1px solid var(--bsub);border-bottom:0;display:block')}</div></div></div></div>'''
    return dual(CH14, 'Website hero', 'Official website hero — concept', fn, None, 'Concept composition from the proposed tokens and a real screenshot. No website exists in the repository.')


def social_page():
    items = [('github-social-preview', 'GitHub social preview', '1280 × 640'), ('youtube-thumbnail', 'YouTube thumbnail', '1280 × 720'), ('linkedin-post', 'LinkedIn', '1200 × 627'),
             ('x-post', 'X / Twitter', '1600 × 900'), ('product-hunt-gallery', 'Product Hunt gallery', '1270 × 760'), ('release-announcement', 'Release announcement', '1920 × 1080')]
    def col(theme):
        cells = ''.join(f'<div>{tpl(theme, n)}<div class="cap" style="margin-top:6px;font-size:14px"><b style="color:var(--fg)">{t}</b> · {d}</div></div>' for n, t, d in items)
        pad = 'padding:112px 56px 92px 96px' if theme == 'dark' else 'padding:112px 96px 92px 56px'
        return f'<div class="half t-{theme}" style="background:var(--bg);color:var(--fg);{pad}"><div class="row" style="justify-content:space-between;align-items:center"><span class="eyebrow">{"Midnight" if theme=="dark" else "Arctic"}</span>{tag("concept")}</div><div class="g2" style="gap:16px;margin-top:14px">{cells}</div></div>'
    body = f'<div class="split">{col("dark")}{col("light")}</div><div class="hdr"><span class="logo">{img("logo/existing/capit-symbol.png")}<span>Cap-IT Brand Book</span></span><b>{CH14}</b></div>'
    return Page('dark', CH14, 'Social and launch artwork', body, chrome=False)


def marketing_specs():
    rows = [('GitHub social preview', '1280 × 640', '2 : 1', 'Keep 40 px margin; text left, product right'),
            ('YouTube thumbnail', '1280 × 720', '16 : 9', 'Keep clear of the duration badge (bottom-right)'),
            ('YouTube channel banner', '2560 × 1440', '16 : 9', 'Safe area 1546 × 423, centred — TV and desktop crop the rest'),
            ('LinkedIn image', '1200 × 627', '1.91 : 1', 'Avoid text near the edges'),
            ('X / Twitter image', '1600 × 900', '16 : 9', 'Edges may be cropped in feeds'),
            ('Instagram portrait', '1080 × 1350', '4 : 5', 'Keep key content in the central 1080 × 1080'),
            ('Product Hunt gallery', '1270 × 760', '1.67 : 1', 'First image is the thumbnail'),
            ('Email header', '1200 × 400 (shown 600 × 200)', '3 : 1', 'Text as live HTML where possible; alt text always'),
            ('Desktop wallpaper', '3840 × 2160', '16 : 9', 'Left third reserved for icons'),
            ('Installer artwork', 'Inno Setup wizard images', '—', 'Concept; the script uses only the icon today'),
            ('Documentation cover', '1920 × 1080', '16 : 9', 'Same layout as the tutorial title card')]
    trs = ''.join(f'<tr><td>{a}</td><td class="mono" style="font-size:16px">{b}</td><td style="white-space:nowrap">{c}</td><td>{d}</td></tr>' for a, b, c, d in rows)
    body = f'''
<div class="g12" style="height:100%;align-content:start;row-gap:22px">
 <div style="grid-column:1/13"><div class="eyebrow">Platform sizes &amp; safe areas</div><h1 class="h2" style="margin:10px 0 0">One system, many canvases.</h1></div>
 <div style="grid-column:1/8"><table class="tbl tight"><thead><tr><th>Asset</th><th>Dimensions</th><th>Ratio</th><th>Safe-area note</th></tr></thead><tbody>{trs}</tbody></table>
  <p class="cap" style="margin-top:10px">Dimensions are common platform guidance at the time of writing and change; confirm before publishing. Every size is a ready template in <span class="mono">Cap-IT-Brand-Assets/templates/</span>, in both themes.</p></div>
 <div style="grid-column:8/13" class="col" style="gap:14px"><div style="position:relative">{tpl('dark','youtube-banner')}<div style="position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);width:60.4%;height:29.4%;border:2px dashed #fff;border-radius:6px;box-shadow:0 0 0 1px #0006"></div></div>
  <div class="cap" style="margin:8px 0 18px">YouTube banner with the 1546 × 423 safe area outlined.</div><div class="g2" style="gap:14px"><div>{tpl('light','email-header')}<div class="cap" style="margin-top:6px">Email header</div></div><div>{tpl('light','download-banner')}<div class="cap" style="margin-top:6px">Download banner</div></div></div></div>
</div>'''
    return Page('light', CH14, 'Platform sizes and safe areas', body)


# ------------------------------------------------------------------------------------------------- voice
def voice_page():
    traits = [('Clear', 'Say the thing once, in the order it happens.'), ('Confident', 'State what Cap-IT does. No hedging, no hype.'), ('Helpful', 'Tell people what to do next.'),
              ('Precise', 'Real numbers and real names; never “blazing fast”.'), ('Modern', 'Plain, current language. No jargon for jargon’s sake.'), ('Human', 'Written by a person for a person.'), ('Professional', 'Warm, not chatty.')]
    cards = ''.join(f'<div class="card" style="padding:18px 22px"><div class="h4" style="color:var(--brand)">{a}</div><div class="small" style="font-size:16.5px;margin-top:4px">{b}</div></div>' for a, b in traits)
    stages = [('Onboarding', 'Ready to create your next recording?', '“Welcome to the ultimate recording experience!”'),
              ('Recording', 'Ready · Recording 00:12:04', '“You’re live! Crushing it!”'),
              ('Editing', 'Original recording preserved · edits save automatically', '“Don’t worry, we’ve got your back.”'),
              ('Exporting', 'Export complete · Open folder', '“Boom! Your masterpiece is ready.”')]
    trs = ''.join(f'<tr><td>{a}</td><td class="pass" style="font-weight:500;color:var(--fg)">{b}</td><td style="color:var(--fgm)"><s>{c}</s></td></tr>' for a, b, c in stages)
    body = f'''
<div class="g12" style="height:100%;align-content:start;row-gap:22px">
 <div style="grid-column:1/13"><div class="eyebrow">Voice &amp; tone</div><h1 class="h1" style="margin:10px 0 0">A calm professional who respects your time.</h1></div>
 <div style="grid-column:1/6" class="col" style="gap:10px"><div style="display:grid;gap:10px">{cards}</div></div>
 <div style="grid-column:7/13"><div class="h3" style="margin-bottom:6px">Preferred vs avoided, by moment</div>
  <table class="tbl tight" style="font-size:16.5px"><thead><tr><th>Moment</th><th>Say</th><th>Avoid</th></tr></thead><tbody>{trs}</tbody></table>
  <div class="row" style="gap:12px;margin-top:14px;align-items:center">{tag('existing')}<span class="cap">“Say” lines are the application’s own copy; “avoid” lines are illustrative.</span></div>
  <div class="g2" style="gap:14px;margin-top:18px"><div class="do"><div class="h4">Avoid</div><p class="small" style="font-size:16px">Exaggerated hype, unsupported claims (“fastest”, “zero lag”), buzzwords, robotic phrasing, unexplained jargon.</p></div><div class="do"><div class="h4">Prefer</div><p class="small" style="font-size:16px">Verbs first, sentence case, one idea per line, the real feature name, a next step.</p></div></div></div>
</div>'''
    return Page('light', CH15, 'Voice and tone', body, toc='15 · Voice & messaging')


def messaging_page():
    items = [
        ('Homepage headline', 'Capture with intention.', 'The most powerful screen recorder ever built.'),
        ('Short description', 'A focused, GPU-assisted screen recorder for Windows.', 'Revolutionary AI-powered capture solution.'),
        ('Feature description', 'A camera that follows the action: it glides toward your cursor and caret, then eases back out.', 'Next-gen cinematic hyper-zoom technology.'),
        ('Tooltip', 'Freeze the screen image — audio and timer keep recording.', 'Click to freeze!'),
        ('Settings help', 'Hardware encoders need a compatible GPU. If a hardware export fails, choose Software.', 'Hardware encoding may not work for some users.'),
        ('Error message', 'Couldn’t open this video. Check that the file still exists, then try again.', 'Error 0x80004005. Operation failed.'),
        ('Export complete', 'Export complete. Open folder', 'Success!!! 🎉'),
        ('Update announcement', 'Cap-IT 3.13.1 is available. Update now installs it and reopens Cap-IT.', 'A new version is ready! Don’t miss out!'),
        ('Release note', 'Smoother Smart Tracking motion and sharper live preview scaling.', 'Various improvements and bug fixes.'),
        ('Support reply', 'Thanks — could you share your Windows version, the capture target and a short description of what happened?', 'Please provide more information.'),
    ]
    trs = ''.join(f'<tr><td style="width:190px">{a}</td><td style="color:var(--fg);font-weight:500;width:46%">{b}</td><td style="color:var(--fgm)"><s>{c}</s></td></tr>' for a, b, c in items)
    body = f'''
<div class="row" style="align-items:center;gap:14px"><span class="eyebrow">Messaging examples</span>{tag('proposed')}</div><h1 class="h2" style="margin:10px 0 14px">Ten places the voice has to hold.</h1>
<table class="tbl tight" style="font-size:17px"><thead><tr><th>Where</th><th>Preferred</th><th>Avoid</th></tr></thead><tbody>{trs}</tbody></table>
<p class="cap" style="margin-top:12px">Lines that already ship (tooltip, settings help, export button, release note) are quoted from the application and the README. Others are proposed wording in the same voice; no unsupported claim is made in any “preferred” line.</p>'''
    return Page('dark', CH15, 'Messaging examples', body)


# --------------------------------------------------------------------------------------- applications
def app_window_page():
    def win(theme, shot_name, w):
        return f'<div style="width:{w}px;border-radius:12px;overflow:hidden;border:1px solid rgba(128,128,128,.4);box-shadow:0 30px 70px rgba(0,0,0,.45)">{img(f"screenshots/{theme}/{shot_name}.png","", "width:100%;display:block")}</div>'
    body = f'''
<div style="position:absolute;inset:0;background:linear-gradient(135deg,#0B2A4A 0%,#1C1B4B 45%,#38164F 100%)"></div>
<div style="position:absolute;inset:0;background:radial-gradient(50% 60% at 20% 20%,rgba(24,220,232,.35),transparent 65%)"></div>
<div style="position:absolute;left:96px;top:130px">{win('dark','home',1000)}</div>
<div style="position:absolute;right:96px;top:300px">{win('light','review-video',900)}</div>
<div style="position:absolute;left:96px;bottom:96px;color:#fff"><div class="eyebrow" style="color:#7DF0F6">Brand application · concept</div><div class="h1" style="color:#fff;margin-top:10px">The app, in both themes.</div><div class="lead" style="color:rgba(255,255,255,.78);max-width:760px;margin-top:10px">Real application renders on a desktop backdrop. Dark: Home. Light: Review &amp; Export.</div></div>'''
    p = Page('dark', CH16, 'Application window composition', body, chrome=True, key='full', toc='16 · Brand applications')
    return p


def cards_page():
    def card(theme, name, label, note):
        return f'<div>{tpl(theme, name)}<div class="small" style="margin-top:8px;font-size:16px"><b style="color:var(--fg)">{label}</b> · {note}</div></div>'
    body = f'''
<div class="g12" style="height:100%;align-content:start;row-gap:20px">
 <div style="grid-column:1/13"><div class="row" style="align-items:center;gap:14px"><span class="eyebrow">Video, release and documentation</span>{tag('concept')}</div><h1 class="h2" style="margin:10px 0 0">Tutorial cards, outros and the export watermark.</h1></div>
 <div style="grid-column:1/5">{card('dark','tutorial-title-card','Tutorial title card','dark')}</div>
 <div style="grid-column:5/9">{card('light','tutorial-title-card','Tutorial title card','light')}</div>
 <div style="grid-column:9/13">{card('dark','video-outro','Video outro','dark')}</div>
 <div style="grid-column:1/5">{card('light','video-outro','Video outro','light')}</div>
 <div style="grid-column:5/9" class="card" style="padding:18px"><div class="h4">Export watermark</div><div style="margin-top:12px;height:150px;border-radius:12px;background:linear-gradient(135deg,#142633,#243D52);position:relative"><div style="position:absolute;right:12px;bottom:12px;display:flex;align-items:center;gap:8px;background:rgba(9,17,29,.55);padding:6px 10px;border-radius:8px">{img('logo/existing/capit-symbol.png','', 'height:20px')}<span style="font-size:13px;color:#F5F9FC;font-weight:600">Cap-IT</span></div></div><p class="cap" style="margin-top:8px">Optional, bottom-right, 4 % of frame height, 55 % opacity plate. The editor already supports image watermarks; this is a suggested default.</p></div>
 <div style="grid-column:9/13" class="card" style="padding:18px"><div class="h4">Documentation &amp; gallery</div><p class="small" style="margin-top:8px;font-size:16.5px">Docs use the Arctic theme by default and the editorial scale; each page opens with an overline, a Level 0–1 graphic and a real screenshot in a 12 px-radius frame. The v3.13.0 gallery is regenerated with the snapshot harness whenever a page layout changes.</p></div>
</div>'''
    return Page('light', CH16, 'Cards, outro, watermark and docs', body)


# ------------------------------------------------------------------------------------------ technical
def architecture_page():
    def node(x, y, w, h, title, sub='', kind=''):
        stroke = 'var(--brand)' if kind == 'core' else ('var(--sec)' if kind == 'out' else 'var(--bctl)')
        ty = y + h / 2 - (6 if sub else -6)
        out = (f'<g><rect x="{x}" y="{y}" width="{w}" height="{h}" rx="12" fill="var(--l1)" stroke="{stroke}" stroke-width="2"/>'
               f'<text x="{x + w / 2}" y="{ty}" text-anchor="middle" fill="var(--fg)" font-size="19" font-weight="600">{title}</text>')
        if sub:
            out += f'<text x="{x + w / 2}" y="{y + h / 2 + 18}" text-anchor="middle" fill="var(--fgm)" font-size="14">{sub}</text>'
        return out + '</g>'
    def arrow(x1, y1, x2, y2):
        return f'<path d="M{x1} {y1}L{x2} {y2}" stroke="var(--brand)" stroke-width="2.5" marker-end="url(#ah)"/>'
    svg = f'''<svg viewBox="0 0 1700 560" style="width:100%;height:520px" fill="none"><defs><marker id="ah" markerWidth="10" markerHeight="10" refX="8" refY="5" orient="auto"><path d="M0 0L10 5 0 10z" fill="var(--brand)"/></marker></defs>
{node(20,20,300,70,'Avalonia UI shell','Views · MVVM view models','')}
{node(400,20,300,70,'RecordingManager','session · pacer · writer','core')}
{node(780,0,300,60,'VideoCaptureService','DXGI · Windows Graphics Capture')}
{node(780,100,300,60,'AudioCaptureService','NAudio · WASAPI')}
{node(780,200,300,60,'FFmpegEncoderService','named pipes → MP4 / MKV')}
{node(1160,0,330,60,'DXGI Desktop Duplication','monitor capture')}
{node(1160,80,330,60,'Windows Graphics Capture','single window')}
{node(1160,160,330,70,'GPU compose · NV12','zoom · cursor · effects (D3D11)')}
{node(1160,250,330,60,'CPU kernels','fallback path')}
{node(780,340,300,70,'Recorded file','fragmented MP4 → faststart MP4 / MKV','out')}
{node(400,340,300,70,'Review & Export','Media Foundation preview · timeline')}
{node(400,470,300,70,'Composition renderer','canvas · background · frame · text')}
{node(780,470,300,70,'MP4 / GIF export','FFmpeg · two-pass GIF','out')}
{arrow(320,55,400,55)}{arrow(700,40,780,30)}{arrow(700,60,780,130)}{arrow(700,75,780,230)}
{arrow(1080,30,1160,30)}{arrow(1080,40,1160,110)}{arrow(1080,40,1160,195)}{arrow(1325,230,1325,250)}
{arrow(930,260,930,340)}{arrow(780,375,700,375)}{arrow(550,410,550,470)}{arrow(700,505,780,505)}</svg>'''
    stack = ['C# / .NET 8', 'Avalonia UI 11', 'CommunityToolkit.Mvvm', 'Microsoft.Extensions.DependencyInjection', 'DXGI Desktop Duplication', 'Windows Graphics Capture', 'Vortice.Direct3D11 / DXGI', 'NAudio · WASAPI', 'FFmpeg', 'Media Foundation', 'GDI / GDI+', 'Win32 input hooks']
    chips = ''.join(f'<span class="chip" style="height:34px;font-size:15px">{c}</span>' for c in stack)
    body = f'''
<div class="row" style="align-items:center;gap:14px"><span class="eyebrow">Technical overview</span>{tag('existing')}</div><h1 class="h2" style="margin:10px 0 8px">How a recording becomes a finished video.</h1>
<div class="card" style="padding:14px 18px;margin-top:6px">{svg}</div>
<div class="row" style="flex-wrap:wrap;gap:10px;margin-top:14px">{chips}</div>
<p class="cap" style="margin-top:10px">Current architecture, drawn from the README and the source. Monitor capture composes on the GPU when the device supports it and otherwise on the CPU kernels; window capture uses Windows Graphics Capture. No future architecture is proposed in this book.</p>'''
    return Page('dark', CH17, 'Architecture', body, toc='17 · Technical overview')


# ------------------------------------------------------------------------------------------ distribution
def distribution_page():
    body = f'''
<div class="g12" style="height:100%;align-content:start;row-gap:24px">
 <div style="grid-column:1/13"><div class="row" style="align-items:center;gap:14px"><span class="eyebrow">Platform, version &amp; distribution</span>{tag('existing')}</div><h1 class="h1" style="margin:10px 0 0">Windows 10 / 11, x64, one installer.</h1></div>
 <div style="grid-column:1/5" class="card"><div class="h4">Positioning</div><ul class="list sm"><li>Windows 10 version 2004 (build 19041) or later, 64-bit; Windows 11 recommended.</li><li>Self-contained installer: no separate .NET runtime, Windows App SDK runtime or manual FFmpeg set-up.</li><li>Adds a Start Menu entry and an optional desktop shortcut; uninstalling does not remove recordings.</li></ul></div>
 <div style="grid-column:5/9" class="card"><div class="h4">Releases &amp; updates</div><ul class="list sm"><li>Published through GitHub Releases.</li><li>The app checks on startup and while open. <b style="color:var(--fg)">Update now</b> downloads the installer, waits for the app to close, installs and relaunches.</li><li>The version string comes from <span class="mono">Directory.Build.props</span> (3.13.1 at the time of writing) — never hard-code “latest” in artwork.</li></ul></div>
 <div style="grid-column:9/13" class="card"><div class="h4">Version badges</div><div class="col" style="gap:12px;margin-top:12px"><div class="row" style="gap:10px;flex-wrap:wrap"><span class="tag existing" style="font-size:13px">v3.13.1</span><span class="tag proposed" style="font-size:13px">Latest</span><span class="tag concept" style="font-size:13px">Beta</span></div><div class="small" style="font-size:16px">Outlined pill, uppercase, 13 px. Colour comes from the label’s meaning: current, new, pre-release. Always a text label.</div></div></div>
 <div style="grid-column:1/13" class="g2" style="gap:30px">
  <div class="t-dark" style="background:var(--bg);border-radius:18px;padding:30px;border:1px solid var(--bsub)"><div class="eyebrow">Download button · Midnight</div><div class="row" style="gap:16px;margin-top:16px;align-items:center"><span class="btn primary lg spec" style="zoom:1.3;font-size:14px">{ic('Download',16)} Download for Windows</span><span class="btn secondary lg spec" style="zoom:1.3;font-size:14px">Release notes</span></div><div class="cap" style="margin-top:14px">44 px, 8 px radius, label ≥ 14 px SemiBold; version and OS under the button in muted text.</div></div>
  <div class="t-light" style="background:var(--bg);border-radius:18px;padding:30px;border:1px solid var(--bsub)"><div class="eyebrow">Download button · Arctic</div><div class="row" style="gap:16px;margin-top:16px;align-items:center"><span class="btn primary lg spec" style="zoom:1.3;font-size:14px">{ic('Download',16)} Download for Windows</span><span class="btn secondary lg spec" style="zoom:1.3;font-size:14px">Release notes</span></div><div class="cap" style="margin-top:14px">Teal #087C88 carries a white label at 5.1 : 1 (the shipped light button is 3.85 : 1 — see chapter 04).</div></div></div>
 <div style="grid-column:1/13" class="card"><div class="h4">Release announcement styling</div><p class="small" style="font-size:17.5px;margin-top:6px">Headline = product + version. One sentence of what changed, in plain words. One real screenshot. Footer: platform line. Template: <span class="mono">templates/*/release-announcement.png</span> (shown in chapter 14). Release notes follow the voice rules in chapter 15.</p></div>
</div>'''
    return Page('light', CH18, 'Platform, version and distribution', body, toc='18 · Platform & distribution')


# ------------------------------------------------------------------------------------------ accessibility
def accessibility_page():
    rows = [
        ('Text contrast', 'Normal text ≥ 4.5 : 1, large ≥ 3 : 1', 'Proposed tokens verified by build gate', 'ok'),
        ('Control boundaries', '≥ 3 : 1 for inputs and toggles (SC 1.4.11)', 'border.control passes; shipped borders do not', 'ok'),
        ('Keyboard focus', '2 px ring, 2 px offset, brand colour (11 : 1 on Midnight, 4.5 : 1 on Arctic)', 'Specified; focus visuals not audited in the app', 'warn'),
        ('Colour independence', 'Every status pairs colour with an icon or label', 'Specified and shown in chapter 10', 'ok'),
        ('Reduced motion', 'Travel and scale removed; feedback kept', 'Specified; app support not verified', 'warn'),
        ('Text scaling', 'No fixed-height text containers; allow 30–40 % growth', 'Specified; not tested', 'warn'),
        ('Screen readers', 'Name every icon-only button (AutomationProperties.Name) where the framework supports it', 'Not verified in the app', 'warn'),
        ('High-DPI', 'Layout in device-independent px; strokes scale with size', 'Renders verified at 125 %', 'ok'),
        ('Theme contrast', 'Both themes pass independently', 'Proposed: yes · Shipped: see audit', 'warn'),
        ('Touch targets', 'Desktop app: 30–44 px controls; use Large (44) for touch', 'Informational', 'ok'),
    ]
    trs = ''.join(f'<tr><td>{a}</td><td style="font-size:16px">{b}</td><td style="font-size:16px"><span class="{"pass" if s=="ok" else "failt"}" style="color:{"var(--ok)" if s=="ok" else "var(--warn)"}">{"●" if s=="ok" else "◐"}</span> {c}</td></tr>' for a, b, c, s in rows)
    def half(theme):
        pad = 'padding:112px 56px 92px 96px' if theme == 'dark' else 'padding:112px 96px 92px 56px'
        extra = f'''<div style="margin-top:22px;display:flex;gap:18px;align-items:center"><span class="btn primary spec" style="zoom:1.2">Focus ring</span><span class="btn primary foc spec" style="zoom:1.2">Focused</span><span class="badge spec" style="zoom:1.2;color:var(--err)">{ic('AlertCircle',12)}<span style="color:var(--fg)">Error — icon + label</span></span></div>'''
        return f'<div class="half t-{theme}" style="background:var(--bg);color:var(--fg);{pad}"><span class="eyebrow">{"Midnight" if theme=="dark" else "Arctic"}</span>{extra}<p class="small" style="margin-top:18px;font-size:17px">{"Focus ring #18DCE8 on #09111D: 11.2 : 1." if theme=="dark" else "Focus ring #087C88 on #FFFFFF: 5.1 : 1."}</p></div>'
    body = f'''
<div class="g12" style="height:100%;align-content:start;row-gap:20px">
 <div style="grid-column:1/13"><div class="eyebrow">Accessibility &amp; consistency</div><h1 class="h2" style="margin:10px 0 0">What is specified, and what is verified.</h1></div>
 <div style="grid-column:1/13"><table class="tbl tight" style="font-size:17px"><thead><tr><th>Area</th><th>Requirement</th><th>Status</th></tr></thead><tbody>{trs}</tbody></table>
 <p class="cap" style="margin-top:10px">● verified or measured in this pass · ◐ specified here but not verified in the running application. Quality checklist: run the snapshot harness in both themes, review focus states with the keyboard only, and re-run <span class="mono">build_tokens.py</span> after any colour change.</p></div>
</div>'''
    return Page('light', CH19, 'Accessibility', body, toc='19 · Accessibility')


def accessibility_dual():
    def fn(theme):
        return f'''<div class="spec"><div class="cell" style="gap:14px"><small>Focus visibility</small><div style="display:flex;gap:20px;align-items:center"><span class="btn primary foc">Start recording</span><span class="btn secondary foc">Choose source</span><span class="inp foc">Recording_2026</span></div>
<small style="margin-top:12px">Status never by colour alone</small><div style="display:flex;gap:10px;flex-wrap:wrap"><span class="badge" style="color:var(--rec)">{ic('Fill.Dot',12)}<span style="color:var(--fg)">Recording</span></span><span class="badge" style="color:var(--recp)">{ic('Fill.Pause',12)}<span style="color:var(--fg)">Paused</span></span><span class="badge" style="color:var(--err)">{ic('AlertCircle',12)}<span style="color:var(--fg)">Error</span></span><span class="badge" style="color:var(--recd)">{ic('CheckCircle',12)}<span style="color:var(--fg)">Done</span></span></div>
<small style="margin-top:12px">Muted text, held to 4.5 : 1</small><div style="font-size:13px;color:var(--fgm)">Captions, placeholders and timestamps remain readable.</div>
<small style="margin-top:12px">Disabled (exempt, never the only cue)</small><div style="display:flex;gap:12px"><span class="btn primary dis">Export video</span><span class="inp dis">Locked</span></div></div></div>'''
    return dual(CH19, 'Examples', 'Accessible states in both themes', fn, None, 'Specimens from the proposed tokens. Disabled controls also carry a tooltip that says why.')


# -------------------------------------------------------------------------------------------- governance
def governance_pages():
    p1 = f'''
<div class="g12" style="height:100%;align-content:start;row-gap:22px">
 <div style="grid-column:1/13"><div class="eyebrow">Brand governance</div><h1 class="h1" style="margin:10px 0 0">Who owns what, and how it changes.</h1></div>
 <div style="grid-column:1/7" class="card"><div class="h4">Ownership &amp; versioning</div><ul class="list sm"><li><b style="color:var(--fg)">Owner:</b> the repository maintainer (ChamathDilshanC). Contributors propose; the owner approves.</li><li><b style="color:var(--fg)">Brand version:</b> this book is 1.0 and uses semantic versioning: patch = fixes, minor = additions, major = identity change.</li><li><b style="color:var(--fg)">Product baseline:</b> v3.13.1. Each release re-checks the screenshots and tokens.</li><li>Assets are named <span class="mono">capit-&lt;thing&gt;-&lt;variant&gt;-&lt;size&gt;</span> and are never edited in place — a new file gets a new variant name.</li></ul></div>
 <div style="grid-column:7/13" class="card"><div class="h4">Approved logo versions</div><div class="row" style="gap:18px;margin-top:14px;align-items:center;flex-wrap:wrap">{img('logo/existing/capit-lockup-stacked.png','', 'height:96px;background:#fff;border-radius:10px;padding:6px')}{img('logo/derived/capit-lockup-stacked-reversed.png','', 'height:96px;background:#09111D;border-radius:10px;padding:6px')}{img('logo/existing/capit-symbol.png','', 'height:80px')}{img('app-icon/capit-icon-midnight-128.png','', 'height:80px')}</div><p class="small" style="margin-top:12px;font-size:16.5px">Existing: stacked lockup, symbol. Derived: reversed lockup, monochrome. Proposed (pending approval): horizontal lockup, app-icon plates.</p></div>
 <div style="grid-column:1/5" class="card"><div class="h4">Tokens</div><p class="small" style="font-size:16.5px;margin-top:6px">Source of truth is <span class="mono">Styles/*.axaml</span> for what ships and the two theme JSON files for what is proposed. <span class="mono">build_tokens.py</span> regenerates them and fails if contrast regresses.</p></div>
 <div style="grid-column:5/9" class="card"><div class="h4">Screenshots</div><p class="small" style="font-size:16.5px;margin-top:6px">Regenerate with the app’s snapshot harness (Debug, both themes) and a demo configuration whenever a page changes. Keep the previous version folder as an archive, as <span class="mono">v3.6.0</span> is.</p></div>
 <div style="grid-column:9/13" class="card"><div class="h4">Release artwork</div><p class="small" style="font-size:16.5px;margin-top:6px">Use the templates; headline = product + version; one real screenshot; both themes where the channel allows a choice.</p></div>
</div>'''
    p2 = f'''
<div class="g12" style="height:100%;align-content:start;row-gap:22px">
 <div style="grid-column:1/13"><div class="eyebrow">Change review &amp; rules</div><h1 class="h1" style="margin:10px 0 0">Four steps to change the brand.</h1></div>
 <div style="grid-column:1/13" class="g4">
  <div class="card"><div class="eyebrow" style="font-size:14px">01</div><div class="h4" style="margin:6px 0">Propose</div><p class="small">Open an issue with the reason, a before/after and the affected tokens or assets.</p></div>
  <div class="card"><div class="eyebrow" style="font-size:14px">02</div><div class="h4" style="margin:6px 0">Measure</div><p class="small">Run the token build; every pair must pass. Re-render the UI in both themes.</p></div>
  <div class="card"><div class="eyebrow" style="font-size:14px">03</div><div class="h4" style="margin:6px 0">Review</div><p class="small">Owner reviews in a pull request; recording code is reviewed separately from brand work.</p></div>
  <div class="card"><div class="eyebrow" style="font-size:14px">04</div><div class="h4" style="margin:6px 0">Publish</div><p class="small">Bump the brand version, update the book and assets, note it in the release.</p></div></div>
 <div style="grid-column:1/7" class="do"><div class="h3">Do</div><ul class="list sm"><li>Use semantic tokens; add a token before adding a hex.</li><li>Show both themes whenever you show a component.</li><li>Label anything that is not shipped (proposed / concept).</li><li>Keep the original recording promise in every export flow.</li></ul></div>
 <div style="grid-column:7/13" class="dont"><div class="h3">Don’t</div><ul class="list sm"><li>Redraw, recolour or re-proportion the logo.</li><li>Invert the dark palette to get a light one.</li><li>Claim performance, compatibility or features that are not verified.</li><li>Mix brand changes into recording-pipeline commits.</li></ul></div>
</div>'''
    return [Page('light', CH20, 'Governance', p1, toc='20 · Governance'), Page('dark', CH20, 'Change review and rules', p2)]


# ------------------------------------------------------------------------------------------- closing
def closing(summary):
    lines = ''.join(f'<tr><td>{a}</td><td class="mono" style="font-size:16px">{b}</td></tr>' for a, b in summary)
    body = f'''
<div style="position:absolute;inset:-120px 0 0 0;background:radial-gradient(50% 60% at 15% 100%,rgba(24,220,232,.14),transparent 70%)"></div>
<div class="g12" style="height:100%;align-content:start;row-gap:22px;position:relative">
 <div style="grid-column:1/7"><div class="eyebrow">Provenance &amp; limits</div><h1 class="h1" style="margin:10px 0 16px">What this book is built on, and what it is not.</h1>
  <ul class="list sm"><li><b style="color:var(--fg)">No vector master.</b> The repository holds only PNG logo artwork; every variant is derived from it. Request the original vector files before print production.</li><li><b style="color:var(--fg)">Wordmark is tiny</b> (2.4 % of the symbol height) — a horizontal lockup is proposed because of it.</li><li><b style="color:var(--fg)">Proposed ≠ shipped.</b> The Midnight / Arctic palettes are recommendations; the application’s colours are unchanged.</li><li><b style="color:var(--fg)">Not verified:</b> app support for reduced motion, screen readers and text scaling; light-theme focus visuals; non-Latin script rendering.</li><li><b style="color:var(--fg)">Recording pipeline untouched.</b> The Smart Tracking work and its limits are in <span class="mono">docs/SMART-TRACKING-RECORDING-FIX.md</span>; audio tail, zoomed text sharpness and broader continuity tests remain a separate validation pass.</li></ul></div>
 <div style="grid-column:8/13" class="card"><div class="h4" style="margin-bottom:8px">Package contents</div><table class="tbl tight" style="font-size:16px"><tbody>{lines}</tbody></table></div>
 <div style="grid-column:1/13;margin-top:10px" class="row"><div style="flex:1"><div class="spectrum"></div></div></div>
 <div style="grid-column:1/13" class="row" style="align-items:center;gap:22px">{img('logo/derived/capit-lockup-stacked-reversed.png','', 'height:120px')}<div class="lead" style="max-width:900px">Precision in capture. Clarity in communication. Creativity in presentation.</div></div>
</div>'''
    return Page('dark', 'Closing', 'Provenance and limits', body, toc='End · Provenance & limits')


def package_summary():
    base = BRAND / 'Cap-IT-Brand-Assets'
    count = lambda sub, pat='*': len(list((base / sub).rglob(pat)))
    return [
        ('Cap-IT-Brand-Book.pdf', '__PAGES__ pages · 16:9'),
        ('Cap-IT-Brand-Guidelines.md', 'editable specification'),
        ('Cap-IT-Brand-Tokens.json', 'spacing · type · motion · features'),
        ('Cap-IT-Dark-Theme.json', f"{len(DARK['proposed']['tokens'])} proposed + {len(DARK['implemented'])} shipped"),
        ('Cap-IT-Light-Theme.json', f"{len(LIGHT['proposed']['tokens'])} proposed + {len(LIGHT['implemented'])} shipped"),
        ('Cap-IT-Brand-Assets/logo', f"{count('logo', '*.png')} PNG + ico"),
        ('Cap-IT-Brand-Assets/app-icon', f"{count('app-icon', '*.png')} PNG + ico"),
        ('Cap-IT-Brand-Assets/icons', f"{count('icons', '*.svg')} SVG (the app's own)"),
        ('Cap-IT-Brand-Assets/screenshots', f"{count('screenshots', '*.png')} real renders"),
        ('Cap-IT-Brand-Assets/templates', f"{count('templates', '*.png')} PNG · both themes"),
        ('avalonia/CapIT.Brand.Proposed.axaml', 'not referenced by the app'),
        ('source/', 'HTML, Python builders, fonts (OFL)'),
    ]


def build(summary=None):
    pages = [website_hero(), social_page(), marketing_specs()]
    pages[0].toc = '14 · Marketing identity'
    pages += [voice_page(), messaging_page(), app_window_page(), cards_page(), architecture_page(), distribution_page(), accessibility_page(), accessibility_dual()]
    pages += governance_pages()
    pages.append(closing(package_summary()))
    return pages
