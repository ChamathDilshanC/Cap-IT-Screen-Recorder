"""Chapters 00-03: cover, introduction, strategy, logo."""
from lib import *

CH0, CH1, CH2, CH3 = 'Front matter', '01 · Brand introduction', '02 · Brand strategy', '03 · Logo identity'


def bracket_svg(w=520, h=340, c='currentColor', sw=4, arm=64):
    """Capture brackets: four corners, the book's recurring frame device."""
    a = arm
    return f'''<svg viewBox="0 0 {w} {h}" width="{w}" height="{h}" fill="none" stroke="{c}" stroke-width="{sw}" stroke-linecap="round" stroke-linejoin="round">
<path d="M2 {a}V12a10 10 0 0 1 10-10H{a}"/><path d="M{w-a} 2h{a-12}a10 10 0 0 1 10 10V{a}"/>
<path d="M{w-2} {h-a}v{a-12}a10 10 0 0 1-10 10H{w-a}"/><path d="M{a} {h-2}H12a10 10 0 0 1-10-10V{h-a}"/></svg>'''


def cover():
    body = f'''
<div style="position:absolute;inset:0;background:radial-gradient(70% 90% at 78% 40%,rgba(24,220,232,.22),transparent 62%),radial-gradient(50% 60% at 95% 100%,rgba(124,92,255,.22),transparent 70%)"></div>
<div style="position:absolute;right:120px;top:150px;width:760px;height:760px;opacity:.95">
  <div style="position:absolute;inset:0;border:2px solid var(--bsub);border-radius:40px;background:linear-gradient(160deg,rgba(27,48,66,.8),rgba(9,17,29,.2))"></div>
  <div style="position:absolute;inset:-26px;color:var(--brand)">{bracket_svg(812,812,sw=5,arm=110)}</div>
  {img('logo/existing/capit-symbol.png','', 'position:absolute;left:130px;top:130px;width:500px;height:auto')}
  <div class="mono" style="position:absolute;left:34px;bottom:30px;font-size:15px;color:var(--fgm);letter-spacing:.08em">● REC  00:00:12  ·  1920×1080  ·  60 FPS</div>
</div>
<div style="position:absolute;left:112px;top:96px;display:flex;align-items:center;gap:16px">
  {img('logo/existing/capit-symbol.png','', 'height:44px;width:auto')}<span style="font-size:22px;font-weight:600;letter-spacing:-.01em">Cap-IT</span>
</div>
<div style="position:absolute;left:112px;bottom:132px;width:900px">
  <div class="eyebrow" style="margin-bottom:28px">Brand identity guidelines</div>
  <div class="hero" style="margin-bottom:34px">Capture<br>with <span style="color:var(--brand)">intention.</span></div>
  <div class="lead" style="max-width:720px">Cap-IT Screen Recorder — the native Windows recorder for tutorials, demos and bug reports. One book, two complete themes: <b>Midnight</b> and <b>Arctic</b>.</div>
</div>
<div style="position:absolute;left:112px;right:112px;bottom:56px;display:flex;justify-content:space-between;font-size:14px;letter-spacing:.16em;text-transform:uppercase;color:var(--fgm);font-weight:600">
  <span>Edition 1.0 · 10 October 2026</span><span>Product baseline v3.13.1 · Windows 10 / 11 x64</span></div>'''
    return Page('dark', CH0, 'Cover', body, chrome=False)


def reading():
    body = f'''
<div class="g12" style="height:100%;align-content:start;row-gap:36px">
 <div style="grid-column:1/7">
  <div class="eyebrow">How to read this book</div>
  <h1 class="display" style="margin:18px 0 22px">Every claim<br>is labelled.</h1>
  <p class="lead">This book describes a brand that already exists, and recommends how it could grow. The two are never mixed on the same line without a label.</p>
 </div>
 <div style="grid-column:8/13;padding-top:20px" class="col">
   <div class="card" style="margin-bottom:14px;padding:22px 26px">{tag('existing')}<p class="small" style="margin-top:10px">Verified in the repository: the logo artwork, the shipped colour, type, spacing, radius and motion tokens, the screenshots, the README, the capture code. Values are parsed from source, not retyped.</p></div>
   <div class="card" style="margin-bottom:14px;padding:22px 26px">{tag('derived')}<p class="small" style="margin-top:10px">Produced mechanically from existing artwork — a crop, a recolour, a plate behind the symbol. Nothing is redrawn.</p></div>
   <div class="card" style="margin-bottom:14px;padding:22px 26px">{tag('proposed')}<p class="small" style="margin-top:10px">A recommendation, with its reasoning and measured contrast. Not applied to the application. Includes the Midnight / Arctic palettes and the horizontal lockup.</p></div>
   <div class="card" style="padding:22px 26px">{tag('concept')}<p class="small" style="margin-top:10px">An exploratory visual (marketing mock-ups, graphic-language studies). Illustrative, not a promise of a feature.</p></div>
 </div>
 <div style="grid-column:1/13;margin-top:0">
  <hr class="rule" style="margin-bottom:18px">
  <div class="g4">
   <div><div class="h4" style="margin-bottom:6px;font-size:20px">Source of truth</div><p class="cap"><span class="mono">src/CapIT.Desktop/Styles/</span> (Colors, Tokens, Typography, Motion, Icons), <span class="mono">assets/Logo-CapIT.png</span>, <span class="mono">docs/screenshots</span>, README v3.13.1.</p></div>
   <div><div class="h4" style="margin-bottom:6px;font-size:20px">Screenshots</div><p class="cap">All Dark and Light application screens are real renders of the implemented themes, produced by the app's own snapshot harness with a clean demo configuration. UI specimens elsewhere are drawn from tokens and labelled.</p></div>
   <div><div class="h4" style="margin-bottom:6px;font-size:20px">Tokens</div><p class="cap">Every colour in this PDF is generated from <span class="mono">Cap-IT-Dark-Theme.json</span> and <span class="mono">Cap-IT-Light-Theme.json</span>. A build gate fails if a claimed pair misses WCAG 2.2 AA.</p></div>
   <div><div class="h4" style="margin-bottom:6px;font-size:20px">Out of scope</div><p class="cap">This book changes no application code. The recording pipeline and its separate technical report are untouched.</p></div>
  </div>
 </div>
</div>'''
    return Page('light', CH0, 'How to read this book', body)


def opener(num, title, sub, theme='dark'):
    body = f'''
<div style="position:absolute;inset:0;background:radial-gradient(60% 80% at 85% 20%,{'rgba(24,220,232,.16)' if theme=='dark' else 'rgba(8,124,136,.10)'},transparent 65%)"></div>
<div style="position:absolute;right:96px;top:120px;color:var(--brand);opacity:.9">{bracket_svg(420,280,sw=3,arm=56)}</div>
<div style="position:absolute;left:96px;bottom:150px;right:96px">
 <div class="num-big" style="font-size:200px;font-weight:200;letter-spacing:-.06em;opacity:.95">{num}</div>
 <div class="hero" style="font-size:112px;margin:12px 0 28px;max-width:1500px">{title}</div>
 <div class="lead" style="max-width:900px">{sub}</div>
 <div class="spectrum" style="width:240px;margin-top:44px"></div>
</div>'''
    return Page(theme, f'{num} · {title}', title, body, chrome=True, key='full', toc=f'{num} · {title}')


def overview():
    body = f'''
<div class="g12" style="row-gap:30px;height:100%;align-content:start">
 <div style="grid-column:1/8"><div class="eyebrow">Brand overview</div>
  <h1 class="display" style="margin:16px 0 24px">The recording should be easy.<br><span class="brandtext">The result should look intentional.</span></h1>
  <p class="lead">Cap-IT is a focused, GPU-assisted Windows screen recorder for polished tutorials, demos, bug reports and social clips. It keeps source selection, live preview, recording, audio monitoring, effects, annotations, editing and export in one native place.</p>
 </div>
 <div style="grid-column:9/13;padding-top:34px">{shot('light','home')}<div class="cap" style="margin-top:10px">Home — the real Arctic theme, v3.13.1.</div></div>
 <div style="grid-column:1/13"><hr class="rule"></div>
 <div style="grid-column:1/5"><div class="eyebrow">Purpose</div><p class="p" style="margin-top:12px">To remove the distance between doing something on screen and showing it well. People should spend their effort on what they are demonstrating, not on assembling the video.</p></div>
 <div style="grid-column:5/9"><div class="eyebrow">Vision</div><p class="p" style="margin-top:12px">Every tutorial, bug report and walkthrough made on Windows can be captured, refined and shared in a single sitting — without a second tool and without a result that looks accidental.</p></div>
 <div style="grid-column:9/13"><div class="eyebrow">Mission</div><p class="p" style="margin-top:12px">Build the most considered native recorder on Windows: precise capture, a camera that follows the action, and a review workspace that keeps the original safe.</p></div>
 <div style="grid-column:1/13" class="row"><span class="chip">{icon('Monitor',18)} Native Windows</span><span class="chip">{icon('Focus',18)} Smart Tracking zoom</span><span class="chip">{icon('Film',18)} Capture + edit in one app</span><span class="chip">{icon('Export',18)} MP4 &amp; GIF</span><span class="chip">{icon('Sparkles',18)} Cursor &amp; click effects</span></div>
</div>'''
    return Page('light', CH1, 'Brand overview', body)


def positioning():
    body = f'''
<div style="position:absolute;inset:-100px 0 0 0;background:radial-gradient(50% 60% at 15% 100%,rgba(124,92,255,.16),transparent 70%)"></div>
<div class="g12" style="height:100%;align-content:start;row-gap:34px;position:relative">
 <div style="grid-column:1/9"><div class="eyebrow">Positioning statement</div>
  <div class="big-quote" style="margin-top:22px">For people who teach, demonstrate and report on Windows, Cap-IT is the <span class="brandtext">native screen recorder</span> that follows the action and hands back a finished-looking result — <span class="sectext">without leaving the app.</span></div>
  <p class="small" style="margin-top:24px;max-width:760px">Unlike general-purpose broadcasting suites, Cap-IT is built around the short, explanatory recording rather than the live stream. Unlike a bare capture tool, it does not stop at the raw file.</p>
 </div>
 <div style="grid-column:10/13" class="card"><div class="eyebrow">Tagline</div><div class="h1" style="margin:12px 0 10px">Capture with<br>intention.</div>{tag('proposed')}<p class="cap" style="margin-top:14px">Supporting lines: Capture beautifully · Make every recording count · From screen to story · Record. Refine. Share.</p></div>
 <div style="grid-column:1/13"><hr class="rule"></div>
 <div style="grid-column:1/5" class="card"><div class="h3" style="margin-bottom:10px">Value proposition</div><ul class="list sm"><li><b style="color:var(--fg)">Capture the right thing</b> — display or single window, with live thumbnails.</li><li><b style="color:var(--fg)">Look intentional</b> — camera, cursor and click effects built in.</li><li><b style="color:var(--fg)">Finish in place</b> — trim, compose, add text, export.</li></ul></div>
 <div style="grid-column:5/9" class="card"><div class="h3" style="margin-bottom:10px">Brand philosophy</div><p class="p">“The recording should be easy, while the result should look intentional.” — the line already in the README, and the test every design decision has to pass.</p></div>
 <div style="grid-column:9/13" class="card"><div class="h3" style="margin-bottom:10px">Five principles</div><div class="row" style="flex-wrap:wrap;gap:10px"><span class="chip">Precision</span><span class="chip">Motion</span><span class="chip">Clarity</span><span class="chip">Creativity</span><span class="chip">Control</span></div><p class="small" style="margin-top:14px">Used as a checklist for layout, motion and copy.</p></div>
</div>'''
    return Page('dark', CH1, 'Positioning', body)


def story():
    body = f'''
<div class="g12" style="height:100%;align-content:start;row-gap:30px">
 <div style="grid-column:1/7"><div class="eyebrow">Brand story</div>
  <h1 class="h1" style="margin:16px 0 22px">Made by someone who got tired of the tape and string.</h1>
  <p class="p">Cap-IT began as a way to make tutorials without juggling a recorder, a zoom plug-in, a webcam tool, an annotation overlay and a video editor. Each release since has pulled one more of those into the same window: DXGI capture, a camera that eases toward the cursor, a floating controller that never appears in the recording, a library, a review workspace.</p>
  <p class="p" style="margin-top:16px">The result is deliberately a <b>craft tool</b>, not a platform: native, quick to open, quiet in use, and honest about what it does.</p>
  <div style="margin-top:30px" class="row"><div><div class="num-big">3.13</div><div class="cap">current release line</div></div><div><div class="num-big">1</div><div class="cap">window from capture to export</div></div></div>
 </div>
 <div style="grid-column:8/13"><div class="eyebrow">Who it is for</div>
  <div class="col" style="gap:12px;margin-top:16px">
   <div class="card" style="padding:18px 22px"><b style="color:var(--fg)">Educators &amp; tutorial creators</b><div class="small">Need a camera that follows the lesson.</div></div>
   <div class="card" style="padding:18px 22px"><b style="color:var(--fg)">Developers &amp; QA engineers</b><div class="small">Need reproducible bug reports and quick GIFs.</div></div>
   <div class="card" style="padding:18px 22px"><b style="color:var(--fg)">Product teams &amp; SaaS founders</b><div class="small">Need walkthroughs and demos that look finished.</div></div>
   <div class="card" style="padding:18px 22px"><b style="color:var(--fg)">Designers, freelancers &amp; marketers</b><div class="small">Need backgrounds, frames and text without another editor.</div></div>
  </div>
  <p class="cap" style="margin-top:14px">Audience list from the product brief. No usage numbers are claimed.</p>
 </div>
 <div style="grid-column:1/7"><div style="height:250px;overflow:hidden;border-radius:14px;border:1px solid var(--bsub);box-shadow:var(--e2)">{img('screenshots/dark/home.png','', 'width:100%;display:block')}</div><div class="cap" style="margin-top:8px">Midnight · Home</div></div>
 <div style="grid-column:7/13"><div style="height:250px;overflow:hidden;border-radius:14px;border:1px solid var(--bsub);box-shadow:var(--e2)">{img('screenshots/light/home.png','', 'width:100%;display:block')}</div><div class="cap" style="margin-top:8px">Arctic · Home</div></div>
</div>'''
    return Page('light', CH1, 'Brand story', body)


def archetype():
    def slider(l, r, pos):
        return f'''<div style="margin:22px 0"><div class="row" style="justify-content:space-between;font-size:17px;color:var(--fg2)"><span>{l}</span><span>{r}</span></div>
<div style="height:4px;background:var(--l3);border-radius:2px;position:relative;margin-top:12px"><i style="position:absolute;left:{pos}%;top:-8px;width:20px;height:20px;border-radius:50%;background:var(--brand);transform:translateX(-50%);box-shadow:0 0 0 4px var(--bg)"></i></div></div>'''
    body = f'''
<div class="g12" style="height:100%;align-content:start;row-gap:30px">
 <div style="grid-column:1/6"><div class="eyebrow">Archetype &amp; personality</div>
  <h1 class="display" style="margin:16px 0 22px">The Craftsperson<br>with a <span class="brandtext">steady hand.</span></h1>
  <p class="p">Cap-IT is not the entertainer or the rebel. It is the skilled assistant who frames the shot for you and then gets out of the way: <b>exact</b>, <b>calm</b>, <b>helpful</b> and quietly <b>proud</b> of a clean result.</p>
  <div class="row" style="flex-wrap:wrap;gap:10px;margin-top:26px"><span class="chip">Precise</span><span class="chip">Calm</span><span class="chip">Helpful</span><span class="chip">Modern</span><span class="chip">Dependable</span><span class="chip">Quietly creative</span></div>
 </div>
 <div style="grid-column:7/13" class="card">
  <div class="eyebrow" style="margin-bottom:4px">Personality spectrum</div>
  {slider('Technical','Approachable',58)}{slider('Expressive','Restrained',72)}{slider('Playful','Professional',68)}{slider('Premium','Accessible',44)}{slider('Experimental','Dependable',80)}
  <p class="cap">Position = where the brand should sit when two qualities pull against each other. Leans professional, restrained and dependable; close to the middle on premium vs accessible, because the product is free to try and demonstrably polished.</p>
 </div>
 <div style="grid-column:1/13" class="g4">
  <div class="card"><div class="h4">Trust attributes</div><p class="small" style="margin-top:8px">Native, not a web wrapper. Original recording preserved. No account wall in the shipped build.</p></div>
  <div class="card"><div class="h4">Core promises</div><p class="small" style="margin-top:8px">You will see what is being recorded. Your original is never overwritten. Effects can change while you record.</p></div>
  <div class="card"><div class="h4">Perception goals</div><p class="small" style="margin-top:8px">“Considered.” “Fast to learn.” “My videos look better than I expected.”</p></div>
  <div class="card"><div class="h4">Not</div><p class="small" style="margin-top:8px">Hype, gamer neon, enterprise stiffness, or a feature checklist shouted at the user.</p></div>
 </div>
</div>'''
    return Page('dark', CH2, 'Archetype & personality', body)


def pains():
    rows = [
        ('Recordings look amateur: static, hard to follow.', 'Smart Tracking camera follows cursor and text caret and eases back out.', 'Interaction-triggered zoom; critically damped motion'),
        ('Too many tools for one tutorial.', 'Capture, effects, webcam, annotations, trim, compose and export in one window.', 'One native app, one library'),
        ('Unsure what is actually being recorded.', 'Live source thumbnails and a live preview before you press record.', 'DXGI + Windows Graphics Capture previews'),
        ('Edits risk the original footage.', 'Edits are metadata beside the video; the original stays untouched.', '“Original recording preserved”'),
        ('Heavy recorders slow the machine.', 'GPU-assisted processing and hardware encoder support with software fallback.', 'NVENC · AMF · QSV · x264'),
        ('Sharing needs another conversion.', 'MP4 and two-pass GIF export from the same workspace.', 'Review & Export'),
    ]
    trs = ''.join(f'<tr><td>{a}</td><td>{b}</td><td class="cap">{c}</td></tr>' for a, b, c in rows)
    body = f'''
<div class="eyebrow">Pain points → benefits → differentiators</div>
<h1 class="h1" style="margin:16px 0 28px;max-width:1300px">What people struggle with, and the plain answer to each.</h1>
<table class="tbl"><thead><tr><th style="width:36%">Customer pain point</th><th style="width:38%">Benefit Cap-IT delivers</th><th>Evidence in the product</th></tr></thead><tbody>{trs}</tbody></table>
<div class="row" style="margin-top:34px;gap:16px;flex-wrap:wrap"><span class="chip">1 Native Windows workflow</span><span class="chip">2 GPU-assisted processing</span><span class="chip">3 Smart Tracking zoom</span><span class="chip">4 Smooth, controlled camera</span><span class="chip">5 Capture + edit together</span><span class="chip">6 Cursor enhancements</span><span class="chip">7 Backgrounds &amp; composition</span><span class="chip">8 Hardware encoding</span><span class="chip">9 Audio monitoring</span><span class="chip">10 MP4 &amp; GIF delivery</span></div>
<p class="cap" style="margin-top:14px">The ten differentiators come from the product brief and are all described in the v3.13.1 README. No benchmark or performance figure is claimed here.</p>'''
    return Page('light', CH2, 'Pain points & benefits', body)


def stages():
    stages = [
        ('Onboarding', 'Welcoming, unhurried', 'Ready to create your next recording?', 'One obvious action. No setup wall. Show the source before asking for anything.', 'Home'),
        ('Recording', 'Quiet and certain', 'Ready · ● Record', 'Disappear. A small controller that never appears in the video, a clear REC state, nothing that competes.', 'Capture'),
        ('Editing', 'Precise and generous', 'Original recording preserved · edits save automatically', 'Dense controls, but reassuring. Every change is reversible and says so.', 'Review'),
        ('Exporting', 'Satisfying, honest', 'Export video · Export GIF 720px / 12 fps', 'Determinate progress, a clear finish, and the file one click away.', 'Export'),
    ]
    cards = ''.join(f'''<div class="card" style="display:flex;flex-direction:column;gap:12px">
 <div class="eyebrow">{i+1:02d}</div><div class="h2">{s[0]}</div><div class="h4" style="color:var(--brand)">{s[1]}</div>
 <div class="mono" style="font-size:14px;color:var(--fg);background:var(--l2);border-radius:8px;padding:10px 12px">“{s[2]}”</div>
 <p class="small">{s[3]}</p></div>''' for i, s in enumerate(stages))
    body = f'''
<div class="eyebrow">How the brand should feel</div>
<h1 class="display" style="margin:16px 0 34px">Four moments. One temperament.</h1>
<div class="g4" style="align-items:stretch">{cards}</div>
<div class="row" style="margin-top:28px;align-items:center;gap:20px">{tag('existing')}<span class="cap">The quoted strings are the application's own microcopy (Home, status bar, Review &amp; Export).</span></div>'''
    return Page('dark', CH2, 'The brand through the workflow', body, glow=True)


# ------------------------------------------------------------------------------------------- logo
def logo_anatomy():
    body = f'''
<div class="g12" style="height:100%;align-content:start">
 <div style="grid-column:1/6"><div class="row" style="align-items:center;gap:14px"><span class="eyebrow">The existing mark</span>{tag('existing')}</div>
  <h1 class="h1" style="margin:16px 0 20px">Five arcs, one centre, in motion.</h1>
  <p class="p">The Cap-IT symbol is five rounded arcs, each ending in a detached round terminal, turning around a shared centre. Five hues sit around the circle; none repeats. The turn suggests a camera iris and a handover between the five stages of a recording.</p>
  <table class="tbl" style="margin-top:22px"><thead><tr><th>Hue</th><th>HEX</th><th>RGB</th></tr></thead><tbody>
   <tr><td><span class="sw" style="background:#FFAA21"></span>Orange</td><td class="mono">#FFAA21</td><td>255, 170, 33</td></tr>
   <tr><td><span class="sw" style="background:#EA0B7F"></span>Magenta</td><td class="mono">#EA0B7F</td><td>234, 11, 127</td></tr>
   <tr><td><span class="sw" style="background:#971A8D"></span>Purple</td><td class="mono">#971A8D</td><td>151, 26, 141</td></tr>
   <tr><td><span class="sw" style="background:#1AABE3"></span>Blue</td><td class="mono">#1AABE3</td><td>26, 171, 227</td></tr>
   <tr><td><span class="sw" style="background:#85C536"></span>Green</td><td class="mono">#85C536</td><td>133, 197, 54</td></tr>
   <tr><td><span class="sw" style="background:#3A3939"></span>Wordmark grey</td><td class="mono">#3A3939</td><td>58, 57, 57</td></tr></tbody></table>
  <p class="cap" style="margin-top:12px">Sampled from <span class="mono">assets/Logo-CapIT.png</span> (2000×2000, transparent).</p>
 </div>
 <div style="grid-column:7/13;position:relative;height:820px">
  <div style="position:absolute;inset:0;border-radius:24px;background:#fff;border:1px solid var(--bsub)"></div>
  {img('logo/existing/capit-lockup-stacked.png','', 'position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);height:690px;width:auto')}
  <svg style="position:absolute;inset:0" viewBox="0 0 800 820" width="100%" height="100%"><g stroke="#0A8FA0" stroke-width="1.5" fill="none" stroke-dasharray="6 6" opacity=".7"><circle cx="400" cy="378" r="268"/><circle cx="400" cy="378" r="120"/><line x1="400" y1="70" x2="400" y2="690"/><line x1="90" y1="378" x2="710" y2="378"/></g></svg>
  <div class="cap" style="position:absolute;left:24px;bottom:18px;color:#475569">Construction guides drawn over the unaltered artwork</div>
 </div>
</div>'''
    return Page('light', CH3, 'The existing mark', body)


def lockups():
    def cell(title, kind, content, bg, note):
        return f'''<div><div class="row" style="align-items:center;gap:12px;margin-bottom:12px"><span class="h4">{title}</span>{tag(kind)}</div>
<div style="height:250px;border-radius:16px;background:{bg};display:flex;align-items:center;justify-content:center;border:1px solid var(--bsub)">{content}</div><div class="cap" style="margin-top:10px">{note}</div></div>'''
    body = f'''
<div class="eyebrow">Logo system</div>
<h1 class="h1" style="margin:14px 0 28px">Lockups and variants</h1>
<div class="g3" style="row-gap:30px">
 {cell('Stacked lockup', 'existing', img('logo/existing/capit-lockup-stacked.png','', 'height:205px;width:auto'), '#FFFFFF', 'The shipped artwork. Wordmark is 2.4% of the symbol height: use the symbol alone below 200 px.')}
 {cell('Stacked, reversed', 'derived', img('logo/derived/capit-lockup-stacked-reversed.png','', 'height:205px;width:auto'), '#09111D', 'Wordmark recoloured #3A3939 → #F5F9FC. Symbol pixels untouched.')}
 {cell('Symbol only', 'existing', img('logo/existing/capit-symbol.png','', 'height:200px;width:auto'), 'linear-gradient(160deg,#142633,#09111D)', 'Preferred mark for the app, icons and anywhere ≤ 200 px.')}
 {cell('Horizontal lockup · Midnight', 'proposed', img('logo/proposed/capit-lockup-horizontal-on-midnight.png','', 'height:84px;width:auto'), '#09111D', 'Symbol + “Cap-IT” in Inter SemiBold. New: the repository defines no horizontal lockup.')}
 {cell('Horizontal lockup · Arctic', 'proposed', img('logo/proposed/capit-lockup-horizontal-on-arctic.png','', 'height:84px;width:auto'), '#FFFFFF', 'Same construction for light surfaces. Cap-height = 38% of symbol height.')}
 {cell('Monochrome', 'derived', img('logo/derived/capit-symbol-mono-black.png','', 'height:150px;width:auto;margin-right:40px')+img('logo/derived/capit-symbol-mono-white.png','', 'height:150px;width:auto;background:#111827;border-radius:12px;padding:16px'), '#F1F5F9', 'Single-colour for stamps, engraving and one-colour print. Brand-cyan variants are included in the asset pack.')}
</div>'''
    return Page('dark', CH3, 'Lockups and variants', body)


def clearspace():
    X = 100
    body = f'''
<div class="g12" style="height:100%;align-content:start">
 <div style="grid-column:1/6"><div class="eyebrow">Clear space &amp; size</div>
  <h1 class="h1" style="margin:14px 0 20px">Room to breathe.</h1>
  <p class="p">Keep clear space equal to <b>X</b> on every side of the symbol, where X is the diameter of the largest round terminal (the magenta dot). Nothing — text, edge, image or another logo — enters that margin.</p>
  <table class="tbl" style="margin:24px 0"><thead><tr><th>Use</th><th>Minimum</th></tr></thead><tbody>
   <tr><td>Symbol, screen</td><td>16 px (favicon, tray) — optimised app icon below</td></tr>
   <tr><td>Symbol, print</td><td>8 mm</td></tr>
   <tr><td>Stacked lockup, screen</td><td>200 px symbol height (wordmark stays legible)</td></tr>
   <tr><td>Horizontal lockup</td><td>24 px symbol height</td></tr>
   <tr><td>Optical alignment</td><td>Centre on the circle’s centre, not on the bounding box</td></tr>
  </tbody></table>
  <p class="cap">Aspect ratio of the symbol artwork is 0.989 : 1 (1375 × 1391). Never stretch; scale proportionally.</p>
 </div>
 <div style="grid-column:7/13;position:relative;height:820px">
  <div style="position:absolute;inset:0;border-radius:24px;background:#fff;border:1px solid var(--bsub)"></div>
  <div style="position:absolute;left:150px;top:150px;width:500px;height:520px;outline:2px dashed #0A8FA0;outline-offset:60px">{img('logo/existing/capit-symbol.png','', 'width:100%;height:100%;object-fit:contain')}</div>
  <div style="position:absolute;left:90px;top:90px;width:560px;height:640px;border:1px solid rgba(10,143,160,.3)"></div>
  <div class="mono" style="position:absolute;left:340px;top:56px;color:#087C88;font-size:18px;font-weight:700">X</div><div class="mono" style="position:absolute;left:96px;top:380px;color:#087C88;font-size:18px;font-weight:700">X</div>
  <div style="position:absolute;right:48px;bottom:44px;display:flex;align-items:flex-end;gap:28px">
    <div style="text-align:center">{img('logo/existing/capit-symbol.png','', 'height:96px')}<div class="cap" style="color:#475569">96</div></div>
    <div style="text-align:center">{img('logo/existing/capit-symbol.png','', 'height:48px')}<div class="cap" style="color:#475569">48</div></div>
    <div style="text-align:center">{img('logo/existing/capit-symbol.png','', 'height:32px')}<div class="cap" style="color:#475569">32</div></div>
    <div style="text-align:center">{img('logo/existing/capit-symbol.png','', 'height:16px')}<div class="cap" style="color:#475569">16</div></div>
  </div>
 </div>
</div>'''
    return Page('light', CH3, 'Clear space and minimum size', body)


def logo_backgrounds():
    def tile(bg, fg_label, logo, ok=True, h=170, label=''):
        mark = '✓' if ok else '✕'
        col = 'var(--ok)' if ok else 'var(--err)'
        return f'''<div style="border-radius:14px;background:{bg};height:{h+60}px;position:relative;display:flex;align-items:center;justify-content:center;border:1px solid rgba(128,128,128,.35)">{logo}
<div style="position:absolute;left:14px;bottom:10px;font-size:13px;font-weight:700;letter-spacing:.06em;color:{fg_label}">{label}</div>
<div style="position:absolute;right:12px;top:10px;font-size:18px;font-weight:700;color:{col};background:rgba(128,128,128,.18);border-radius:50%;width:28px;height:28px;display:flex;align-items:center;justify-content:center">{mark}</div></div>'''
    sym = lambda p='logo/existing/capit-symbol.png': img(p, '', 'height:150px;width:auto')
    body = f'''
<div class="split">
 <div class="half t-dark" style="background:var(--bg);color:var(--fg);padding:112px 56px 92px 96px">
  <div class="eyebrow">Midnight surfaces</div><h2 class="h2" style="margin:10px 0 22px">Colour symbol on dark</h2>
  <div class="g2" style="gap:18px">
   {tile('#09111D','#93A8B9', sym(), True, label='Midnight  #09111D')}
   {tile('#142633','#93A8B9', sym(), True, label='Layer 1  #142633')}
   {tile('#243D52','#93A8B9', sym('logo/derived/capit-symbol-mono-white.png'), True, label='Layer 3 · mono white')}
   {tile('#18DCE8','#04141A', sym('logo/derived/capit-symbol-mono-black.png'), True, label='On brand cyan · mono black')}
  </div><p class="cap" style="margin-top:18px">Use the reversed stacked lockup (wordmark #F5F9FC) whenever the wordmark is shown.</p>
 </div>
 <div class="half t-light" style="background:var(--bg);color:var(--fg);padding:112px 96px 92px 56px">
  <div class="eyebrow">Arctic surfaces</div><h2 class="h2" style="margin:10px 0 22px">Colour symbol on light</h2>
  <div class="g2" style="gap:18px">
   {tile('#FFFFFF','#475569', sym(), True, label='Arctic  #FFFFFF')}
   {tile('#F1F5F9','#475569', sym(), True, label='Layer 2  #F1F5F9')}
   {tile('#087C88','#FFFFFF', sym('logo/derived/capit-symbol-mono-white.png'), True, label='On brand · mono white')}
   {tile('#18DCE8','#04141A', sym(), False, label='Colour on cyan: avoid')}
  </div><p class="cap" style="margin-top:18px">The colour symbol never sits on a saturated brand field: use mono white or mono black there.</p>
 </div>
</div>
<div class="hdr"><span class="logo">{img('logo/existing/capit-symbol.png')}<span>Cap-IT Brand Book</span></span><b>03 · Logo identity</b></div>'''
    return Page('dark', CH3, 'Logo on Midnight and Arctic', body, chrome=False)


def misuse():
    items = [
        ('Do not stretch or squash', 'transform:scaleX(1.45)'), ('Do not rotate', 'transform:rotate(28deg)'),
        ('Do not recolour the hues', 'filter:hue-rotate(110deg)'), ('Do not add shadows or glows', 'filter:drop-shadow(0 10px 8px rgba(0,0,0,.55))'),
        ('Do not outline', 'filter:grayscale(1) contrast(1.6)'), ('Do not reduce opacity', 'opacity:.35'),
        ('Do not crop the symbol', 'clip-path:inset(0 0 45% 0)'), ('Do not place on busy imagery', ''),
    ]
    cells = ''
    for t, st in items:
        if t.startswith('Do not place'):
            inner = f'<div style="position:absolute;inset:0;background:repeating-linear-gradient(45deg,#EA0B7F,#EA0B7F 14px,#1AABE3 14px,#1AABE3 28px);opacity:.9"></div>' + img('logo/existing/capit-symbol.png','', 'position:relative;height:120px')
        else:
            inner = img('logo/existing/capit-symbol.png','', f'height:120px;{st}')
        cells += f'''<div><div style="height:190px;border-radius:12px;background:#fff;border:1px solid var(--bsub);display:flex;align-items:center;justify-content:center;position:relative;overflow:hidden">{inner}
<div style="position:absolute;right:10px;top:8px;color:var(--err);font-size:22px;font-weight:700">✕</div></div><div class="small" style="margin-top:10px;color:var(--fg)">{t}</div></div>'''
    body = f'''
<div class="eyebrow">Incorrect usage</div><h1 class="h1" style="margin:14px 0 28px">Eight ways not to use the mark</h1>
<div class="g4" style="row-gap:26px">{cells}</div>
<div class="row" style="margin-top:34px;gap:40px"><div class="do" style="flex:1"><div class="h4">Accessibility</div><p class="small">The multi-colour symbol is decorative next to the name “Cap-IT”; give it empty alt text there, and “Cap-IT logo” when it stands alone.</p></div>
<div class="do" style="flex:1"><div class="h4">Background restrictions</div><p class="small">Place on Midnight or Arctic surfaces and on photographs only where the symbol sits on a flat, calm area at ≥ 4.5:1 average contrast.</p></div></div>'''
    return Page('light', CH3, 'Incorrect logo usage', body)


def icon_contexts():
    def plate(p, s, r=22):
        return img(p, '', f'width:{s}px;height:{s}px;border-radius:{r}%')
    body = f'''
<div class="eyebrow">Identity in context</div><h1 class="h1" style="margin:14px 0 24px">App icon, taskbar, installer, avatar</h1>
<div class="g12" style="row-gap:26px">
 <div style="grid-column:1/5" class="card"><div class="row" style="justify-content:space-between"><span class="h4">Application icon</span>{tag('proposed')}</div>
  <div class="row" style="align-items:flex-end;margin-top:20px;gap:20px">{plate('app-icon/capit-icon-midnight-256.png',150)}{plate('app-icon/capit-icon-arctic-256.png',150)}</div>
  <p class="small" style="margin-top:14px">A rounded plate (22.4%) holds the existing symbol at 64% of the plate. Midnight is the default; Arctic for light shells. The shipped <span class="mono">AppIcon.ico</span> (transparent symbol) stays until this is adopted.</p></div>
 <div style="grid-column:5/9" class="card"><div class="row" style="justify-content:space-between"><span class="h4">Windows taskbar &amp; Start</span>{tag('concept')}</div>
  <div style="margin-top:20px;border-radius:10px;background:#0B1420;height:62px;display:flex;align-items:center;gap:14px;padding:0 18px;border:1px solid #1d2b3a">
   <div style="width:34px;height:34px;border-radius:8px;background:#1d2b3a"></div>{img('app-icon/capit-icon-midnight-64.png','', 'width:36px;height:36px;border-radius:9px;box-shadow:0 0 0 2px rgba(24,220,232,.45)')}<div style="width:34px;height:34px;border-radius:8px;background:#1d2b3a"></div><div style="flex:1"></div><span class="cap">12:04</span></div>
  <div class="row" style="margin-top:20px;gap:14px;align-items:center">{plate('app-icon/capit-icon-midnight-64.png',48)}<div><b style="color:var(--fg)">Cap-IT Screen Recorder</b><div class="cap">Start Menu · optional desktop shortcut</div></div></div></div>
 <div style="grid-column:9/13" class="card"><div class="row" style="justify-content:space-between"><span class="h4">Favicon &amp; avatars</span>{tag('proposed')}</div>
  <div class="row" style="align-items:flex-end;margin-top:20px;gap:22px">{plate('app-icon/capit-avatar-midnight-512.png',120,50)}{plate('app-icon/capit-avatar-arctic-512.png',120,50)}{plate('app-icon/capit-icon-midnight-32.png',32)}{plate('app-icon/capit-icon-midnight-16.png',16)}</div>
  <p class="small" style="margin-top:14px">Round 512 px for GitHub, X, LinkedIn and YouTube. 32 and 16 px use the plate for contrast on browser tabs.</p></div>
 <div style="grid-column:1/7" class="card"><div class="row" style="justify-content:space-between"><span class="h4">Installer</span>{tag('concept')}</div>
  <div style="margin-top:14px;display:flex;border-radius:12px;overflow:hidden;border:1px solid var(--bsub);height:190px"><div style="width:150px;background:linear-gradient(180deg,#142633,#09111D);display:flex;align-items:center;justify-content:center">{img('logo/existing/capit-symbol.png','', 'width:96px')}</div>
   <div style="flex:1;background:var(--l1);padding:22px"><b style="color:var(--fg);font-size:18px">Setup — Cap-IT Screen Recorder</b><p class="small" style="margin-top:8px">Self-contained: no separate .NET runtime or FFmpeg set-up. Uninstalling does not remove your recordings.</p><div class="btn-row" style="margin-top:26px"><span class="chip" style="background:var(--brand);color:var(--on-brand);border-color:transparent">Install</span> <span class="chip">Cancel</span></div></div></div>
  <p class="cap" style="margin-top:10px">Inno Setup “modern” wizard (existing). Side-panel artwork is a concept; the script today uses only <span class="mono">SetupIconFile</span>.</p></div>
 <div style="grid-column:7/13" class="card"><div class="row" style="justify-content:space-between"><span class="h4">GitHub repository identity</span>{tag('proposed')}</div>
  <div style="margin-top:18px;border-radius:12px;border:1px solid var(--bsub);background:var(--bg);padding:20px;display:flex;gap:18px;align-items:center">{plate('app-icon/capit-avatar-midnight-512.png',72,50)}<div><b style="color:var(--fg);font-size:18px">ChamathDilshanC / Cap-IT-Screen-Recorder</b><div class="small">A focused, GPU-assisted Windows screen recorder for polished tutorials, demos and bug reports.</div></div></div>
  <p class="cap" style="margin-top:10px">Social preview (1280×640) templates are in the asset pack for both themes.</p></div>
</div>'''
    return Page('dark', CH3, 'Identity in context', body)


def build():
    def T(page, label):
        page.toc = label
        return page
    return [cover(), reading(),
            opener('01', 'Brand introduction', 'Why Cap-IT exists, who it is for, and what it believes.', 'dark'),
            overview(), positioning(), story(),
            T(archetype(), '02 · Brand strategy'), pains(), stages(),
            T(logo_anatomy(), '03 · Logo identity'), lockups(), clearspace(), logo_backgrounds(), misuse(), icon_contexts()]
