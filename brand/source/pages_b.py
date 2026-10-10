"""Chapters 04-09: dual-theme identity, typography, layout, icons, graphic language, motion."""
import math
from lib import *
from pages_a import opener, bracket_svg
from contrast import ratio, grade

CH4, CH5, CH6, CH7, CH8, CH9 = ('04 · Dual-theme identity', '05 · Typography', '06 · Layout & spacing',
                                '07 · Iconography', '08 · Graphic language', '09 · Motion identity')


def v(theme, name):
    return hexv(theme, name)


def hx6(h):
    return '#' + h.lstrip('#')[-6:].upper() if len(h.lstrip('#')) == 8 else h.upper()


def dual_opener():
    def half(theme, title, sub, bullets):
        glow = 'rgba(24,220,232,.18)' if theme == 'dark' else 'rgba(8,124,136,.12)'
        li = ''.join(f'<li>{b}</li>' for b in bullets)
        return f'''<div class="half t-{theme}" style="background:var(--bg);color:var(--fg)">
<div style="position:absolute;inset:0;background:radial-gradient(70% 70% at 50% 0%,{glow},transparent 70%)"></div>
<div style="position:absolute;left:{96 if theme=='dark' else 130}px;right:{130 if theme=='dark' else 96}px;bottom:120px">
 <div class="eyebrow">{'Theme one' if theme=='dark' else 'Theme two'}</div>
 <div class="display" style="margin:14px 0 18px;font-size:96px">{title}</div>
 <div class="lead" style="margin-bottom:22px">{sub}</div><ul class="list sm">{li}</ul></div></div>'''
    body = f'''<div class="split">
{half('dark','Midnight','Precision after dark: deep navy surfaces, restrained cyan, a violet whisper.',['Backgrounds #09111D → #1B3042','Cyan #18DCE8 on every action','Text 17.9 : 1 on Midnight'])}
{half('light','Arctic','Precision in daylight: crisp white, calibrated teal, ink-navy type.',['Backgrounds #FFFFFF → #F1F5F9','Teal #087C88, tuned for 4.5 : 1','Text 17.7 : 1 on Arctic'])}
</div>
<div style="position:absolute;left:96px;top:112px;z-index:3"><div class="eyebrow" style="color:#18DCE8">04 · Dual-theme identity</div></div>
<div style="position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);z-index:4;width:172px;height:172px;border-radius:50%;background:#fff;box-shadow:0 20px 60px rgba(0,0,0,.4);display:flex;align-items:center;justify-content:center">{img('logo/existing/capit-symbol.png','', 'width:112px;height:auto')}</div>'''
    p = Page('dark', '04 · Dual-theme identity — Midnight & Arctic', 'Dual-theme identity — Midnight & Arctic', body, chrome=False, key='full', toc='04 · Dual-theme identity')
    return p


def impl_swatches(theme, label):
    d = DARK if theme == 'dark' else LIGHT
    im = d['implemented']
    names = [('Brush.BackgroundPrimary', 'Background'), ('Brush.BackgroundSecondary', 'Title bar'), ('Brush.Surface', 'Surface'),
             ('Brush.SurfaceRaised', 'Raised'), ('Brush.BorderNormal', 'Border'), ('Brush.TextPrimary', 'Text'),
             ('Brush.TextSecondary', 'Text 2'), ('Brush.TextMuted', 'Muted'), ('Brush.Accent', 'Accent'),
             ('Brush.Success', 'Success'), ('Brush.Warning', 'Warning'), ('Brush.Error', 'Error'),
             ('Brush.Info', 'Info'), ('Brush.Recording', 'Recording')]
    cells = ''.join(f'''<div style="border-radius:10px;overflow:hidden;border:1px solid rgba(128,128,128,.4)"><div style="height:54px;background:{hx6(im[k])}"></div>
<div style="padding:8px 10px;background:#00000010"><div style="font-size:14.5px;font-weight:600">{n}</div><div class="mono" style="font-size:13px;opacity:.75">{hx6(im[k])}</div></div></div>''' for k, n in names)
    return f'<div><div class="row" style="align-items:center;gap:14px;margin-bottom:14px"><span class="h3">{label}</span>{tag("existing")}</div><div style="display:grid;grid-template-columns:repeat(4,1fr);gap:12px">{cells}</div></div>'


def existing_palette():
    body = f'''
<div class="eyebrow">What ships today</div>
<h1 class="h1" style="margin:14px 0 14px">Both themes already exist in the app.</h1>
<p class="lead" style="max-width:1500px;margin-bottom:26px">Cap-IT v3.13.1 implements a Dark and a Light theme in <span class="mono">Styles/Colors.axaml</span>, switched by <span class="mono">RequestedThemeVariant</span> (System · Light · Dark). Views only reference semantic <span class="mono">Brush.*</span> keys — the system this book builds on, rather than replaces.</p>
<div class="g2" style="gap:44px">{impl_swatches('dark','Dark (flagship) — implemented')}{impl_swatches('light','Light — implemented')}</div>
<p class="cap" style="margin-top:20px">Values are parsed from the source file at build time. Accent: Dark <span class="mono">#22CBDD</span> · Light <span class="mono">#0A8FA0</span>. The five-colour logo is not used as a UI colour source in either theme.</p>'''
    return Page('dark', CH4, 'What exists today', body)


def audit():
    def rows(theme):
        d = DARK if theme == 'dark' else LIGHT
        out = ''
        for a in d['implementedAudit']:
            if a['kind'] == 'text' or a['foreground'] in ('Brush.BorderNormal', 'Brush.BorderStrong'):
                ok = a['verdict'] in ('AA', 'AAA')
                if a['kind'] == 'non-text' or a['foreground'].startswith('Brush.Border'):
                    ok = a['ratio'] >= 3
                    vd = 'AA' if ok else 'FAIL'
                else:
                    vd = a['verdict']
                kind = 'UI 3:1' if a['foreground'].startswith('Brush.Border') else 'text 4.5:1'
                out += f'''<tr><td>{a['foreground'].replace('Brush.','')} <span class="cap">on</span> {a['background'].replace('Brush.','')}</td><td class="num mono">{a['ratio']:.2f}</td><td class="cap">{kind}</td><td class="{'pass' if ok else 'failt'}">{vd}</td></tr>'''
        return out
    body = f'''
<div class="g12" style="height:100%;align-content:start;row-gap:24px">
 <div style="grid-column:1/13"><div class="eyebrow">Measured, not guessed</div><h1 class="h2" style="margin:10px 0 8px">An accessibility audit of the shipped themes</h1>
 <p class="lead" style="max-width:1600px;font-size:23px">Every pair below was computed from the implemented token values (WCAG 2.x relative luminance). Pairs that fail are the reason the proposed palette in this chapter changes some values.</p></div>
 <div style="grid-column:1/7"><div class="h3" style="margin-bottom:6px">Dark — implemented</div><table class="tbl tight" style="font-size:15.5px"><thead><tr><th>Pair</th><th class="num">Ratio</th><th>Needs</th><th>Result</th></tr></thead><tbody>{rows('dark')}</tbody></table></div>
 <div style="grid-column:7/13"><div class="h3" style="margin-bottom:6px">Light — implemented</div><table class="tbl tight" style="font-size:15.5px"><thead><tr><th>Pair</th><th class="num">Ratio</th><th>Needs</th><th>Result</th></tr></thead><tbody>{rows('light')}</tbody></table></div>
 <div style="grid-column:1/13;padding:18px 26px" class="card"><b style="color:var(--fg)">Findings.</b> <span class="p" style="font-size:16.5px;line-height:1.5">Light: the white label on the accent button is <b>3.85 : 1</b> (needs 4.5 for 13 px semibold), and Success, Warning and Recording text on white are 3.4–4.2 : 1. Dark: Muted text is 3.9–4.2 : 1. Input and card borders in both themes are 1.4–1.9 : 1, acceptable for decorative card outlines but not for identifying an input (SC 1.4.11 asks 3 : 1). Disabled text is exempt. Nothing here was changed in the application.</span></div>
</div>'''
    return Page('light', CH4, 'Accessibility audit of the shipped themes', body)


def palette_page(theme):
    name = 'Midnight Precision' if theme == 'dark' else 'Arctic Precision'
    keys = ['bg.app', 'bg.secondary', 'surface.layer1', 'surface.layer2', 'surface.layer3', 'brand.primary', 'brand.secondary',
            'fg.primary', 'fg.secondary', 'fg.muted', 'status.success', 'status.warning', 'status.error', 'rec.active']
    labels = {'bg.app': 'Background', 'bg.secondary': 'Canvas', 'surface.layer1': 'Layer 1', 'surface.layer2': 'Layer 2', 'surface.layer3': 'Layer 3',
              'brand.primary': 'Brand primary', 'brand.secondary': 'Brand secondary', 'fg.primary': 'Text', 'fg.secondary': 'Text 2', 'fg.muted': 'Muted',
              'status.success': 'Success', 'status.warning': 'Warning', 'status.error': 'Error', 'rec.active': 'Recording'}
    cells = ''
    for k in keys:
        e = tk(theme, k)
        bd = 'rgba(128,128,128,.4)'
        adj = f'<div class="cap" style="margin-top:2px">adjusted from {e["adjustedFrom"]}</div>' if 'adjustedFrom' in e else ''
        cells += f'''<div style="border-radius:14px;overflow:hidden;border:1px solid {bd};background:var(--l1)"><div style="height:112px;background:{e['hex']}"></div>
<div style="padding:12px 14px"><div style="font-size:17px;font-weight:600">{labels[k]}</div><div class="mono" style="font-size:14px;color:var(--fg2);margin-top:3px">{e['hex']} · {e['rgb']}</div>{adj}</div></div>'''
    glow = TOK['gradients']['spotlightDark' if theme == 'dark' else 'spotlightLight']['css']
    body = f'''
<div class="row" style="align-items:flex-end;justify-content:space-between"><div><div class="eyebrow">{name}</div><h1 class="h1" style="margin:12px 0 8px">{'Deep, calm, exact.' if theme=='dark' else 'Crisp, clear, composed.'}</h1></div>{tag('proposed')}</div>
<p class="lead" style="max-width:1500px;margin-bottom:24px">{'Navy-tinted surfaces replace the near-neutral charcoal; the cyan brightens slightly, and violet appears only as a creative accent.' if theme=='dark' else 'A white-first palette: white cards on a cool-white canvas, ink-navy type, and a teal tuned down just far enough to carry white labels at 4.5 : 1.'}</p>
<div style="display:grid;grid-template-columns:repeat(7,1fr);gap:16px">{cells}</div>
<div class="g3" style="margin-top:26px;gap:24px">
 <div class="card" style="padding:22px 26px"><div class="h4" style="margin-bottom:6px">Not a simple inversion</div><p class="small">{'Light-on-dark text is slightly cool (#F5F9FC), never pure white, to reduce glare on long sessions.' if theme=='dark' else 'The accent is darker, not just desaturated, because a bright cyan has too little contrast on white.'}</p></div>
 <div class="card" style="padding:22px 26px"><div class="h4" style="margin-bottom:6px">Where it starts from</div><p class="small">The initial palette in the brief. Values marked “adjusted” missed WCAG 2.2 AA when measured and moved the shortest distance that passes.</p></div>
 <div class="card" style="padding:22px 26px"><div class="h4" style="margin-bottom:6px">Logo relationship</div><p class="small">The five-colour symbol is unchanged and sits on these surfaces as-is; only the interface accent is cyan.</p></div></div>'''
    return Page(theme, CH4, name, body, glow=True)


def token_pages(theme):
    d = DARK if theme == 'dark' else LIGHT
    toks = d['proposed']['tokens']
    groups = [
        ('Surfaces & foreground', ['bg.app', 'bg.secondary', 'surface.layer1', 'surface.layer2', 'surface.layer3', 'surface.overlay', 'surface.scrim',
                                   'fg.primary', 'fg.secondary', 'fg.muted', 'fg.disabled']),
        ('Brand', ['brand.primary', 'brand.primary.hover', 'brand.primary.pressed', 'brand.on-primary', 'brand.secondary', 'brand.secondary.text',
                   'brand.secondary.solid', 'brand.secondary.on-solid']),
        ('Borders & state', ['border.subtle', 'divider', 'border.control', 'state.hover', 'state.pressed', 'state.selected', 'state.focus-ring',
                             'state.selection-text', 'state.disabled-fill']),
        ('Status & recording', ['status.info', 'status.success', 'status.warning', 'status.error', 'rec.active', 'rec.paused', 'rec.exporting', 'rec.export-complete']),
    ]
    pages = []
    for gi in (0, 2):
        blocks = []
        for gname, names in (groups[gi], groups[gi + 1]):
            trs = ''
            for n in names:
                e = toks[n]
                if 'contrast' in e:
                    c = min(e['contrast'].values())
                    con = f'<span class="{"pass" if c >= (3 if n == "border.control" else 4.5) else "failt"}">{c:.1f}</span>'
                elif 'onColor' in e:
                    con = f'<span class="pass">{e["onColor"]["contrast"]:.1f}</span>'
                else:
                    con = '<span class="cap">—</span>'
                alpha = f' <span class="cap">α{e["alpha"]:g}</span>' if e['alpha'] < 1 else ''
                chk = 'background-image:conic-gradient(#8883 25%,transparent 0 50%,#8883 0 75%,transparent 0);background-size:8px 8px;' if e['alpha'] < 1 else ''
                rgba_ = rgba(theme, n)
                trs += f'''<tr><td class="mono" style="font-size:14.5px">{n}</td><td><span class="sw" style="{chk}position:relative"><i style="position:absolute;inset:0;background:{rgba_};border-radius:5px"></i></span><span class="mono" style="font-size:14.5px">{e['hex']}{alpha}</span></td>
<td class="mono" style="font-size:13.5px;white-space:nowrap">{e['rgb']}</td><td class="mono" style="font-size:13.5px;white-space:nowrap">{e['hsl']}</td><td class="num">{con}</td><td class="cap" style="font-size:14px">{e['usage'].split('.')[0]}.</td></tr>'''
            blocks.append(f'<div class="h3" style="margin:6px 0 4px">{gname}</div><table class="tbl tight" style="font-size:15px"><thead><tr><th>Token</th><th>HEX</th><th>RGB</th><th>HSL</th><th class="num">AA</th><th>Usage</th></tr></thead><tbody>{trs}</tbody></table>')
        part = 'Surfaces · foreground · brand' if gi == 0 else 'Borders · state · status · recording'
        body = f'''<div class="row" style="justify-content:space-between;align-items:center"><div><div class="eyebrow">{'Midnight' if theme=='dark' else 'Arctic'} semantic tokens · {part}</div></div>{tag('proposed')}</div>
<div style="margin-top:6px">{blocks[0]}<div style="height:6px"></div>{blocks[1]}</div>
<p class="cap" style="margin-top:12px">“AA” = lowest measured contrast of the token against every text-bearing layer (4.5 : 1; borders 3 : 1; “on” colours against their fill). CMYK approximations, full usage text and the Avalonia keys are in the JSON and the Markdown guide.</p>'''
        pages.append(Page(theme, CH4, f'Semantic tokens — {theme}', body))
    return pages


def matrix_page():
    def matrix(theme):
        d = DARK if theme == 'dark' else LIGHT
        bgs = d['proposed']['textBackgrounds']
        names = ['fg.primary', 'fg.secondary', 'fg.muted', 'brand.primary', 'brand.secondary.text', 'status.info', 'status.success', 'status.warning', 'status.error', 'rec.active']
        head = ''.join(f'<th class="num"><span class="sw" style="background:{b};margin:0 6px 0 0"></span>{b}</th>' for b in bgs)
        rows = ''
        for n in names:
            e = d['proposed']['tokens'][n]
            cells = ''.join(f'<td class="num mono {"pass" if e["contrast"][b] >= 4.5 else "failt"}">{e["contrast"][b]:.2f}</td>' for b in bgs)
            rows += f'<tr><td class="mono" style="font-size:15px"><span class="sw" style="background:{e["hex"]}"></span>{n}</td>{cells}</tr>'
        return f'<div><div class="row" style="align-items:center;gap:14px;margin-bottom:6px"><span class="h3">{"Midnight" if theme=="dark" else "Arctic"}</span>{tag("proposed")}</div><table class="tbl tight" style="font-size:15px"><thead><tr><th>Foreground</th>{head}</tr></thead><tbody>{rows}</tbody></table></div>'
    adj = []
    for theme, d in (('Midnight', DARK), ('Arctic', LIGHT)):
        for n, e in d['proposed']['tokens'].items():
            if 'adjustedFrom' in e:
                adj.append(f'<div style="padding:8px 0;border-bottom:1px solid var(--div);font-size:14.5px"><span class="cap" style="font-size:12.5px">{theme}</span> <span class="mono" style="color:var(--fg)">{n}</span><div class="mono" style="font-size:14px;color:var(--fg2);margin-top:3px"><span class="sw" style="width:16px;height:16px;background:{e["adjustedFrom"]}"></span>{e["adjustedFrom"]} → <span class="sw" style="width:16px;height:16px;background:{e["hex"]}"></span>{e["hex"]}</div></div>')
    body = f'''
<div class="eyebrow">Verification</div><h1 class="h2" style="margin:10px 0 12px">Every foreground against every layer it can meet</h1>
<div class="g2" style="gap:44px">{matrix('dark')}{matrix('light')}</div>
<div style="margin-top:16px"><div class="h4" style="margin-bottom:6px">What changed from the brief’s starting palette <span class="cap" style="font-weight:400">— shortest move that passes</span></div>
<div style="display:grid;grid-template-columns:repeat(4,1fr);column-gap:28px">{''.join(adj)}</div></div>
<p class="cap" style="margin-top:12px"><b style="color:var(--fg)">Reading the numbers.</b> WCAG 2.2 AA asks 4.5 : 1 for normal text, 3 : 1 for large text and for the boundary of a control. Layer 3 is excluded because no text is placed on it. Colour is never the only carrier of status: every state also has an icon or label (chapter 19).</p>'''
    return Page('dark', CH4, 'Contrast verification', body)


def gradients_page():
    g = TOK['gradients']
    def half(theme):
        spot = g['spotlightDark' if theme == 'dark' else 'spotlightLight']['css']
        ident = g['identity']['css']
        surf = g['midnightSurface' if theme == 'dark' else 'arcticSurface']['css']
        rec = g['recording']['css']
        acc = g['accentSubtle']['css']
        el = TOK['elevation'][theme]
        def box(label, css, h=165, text=''):
            return f'<div><div style="height:{h}px;border-radius:14px;background:{css};border:1px solid var(--bsub);display:flex;align-items:flex-end;padding:12px;font-size:14px;color:var(--fg2)">{text}</div><div class="small" style="margin-top:8px;color:var(--fg)">{label}</div></div>'
        pad = 'padding:112px 56px 92px 96px' if theme == 'dark' else 'padding:112px 96px 92px 56px'
        return f'''<div class="half t-{theme}" style="background:var(--bg);color:var(--fg);{pad}">
<div class="eyebrow">{'Midnight' if theme=='dark' else 'Arctic'} · gradients &amp; elevation</div>
<div class="g2" style="gap:16px;margin-top:18px">
 {box('Identity · cyan → violet', ident)}{box('Spotlight', f'{spot},var(--bg)')}
 {box('Surface', surf)}{box('Recording edge glow', f'{rec},var(--l1)', text='● REC')}
 {box('Accent subtle', f'{acc},var(--l1)')}<div><div style="height:165px;border-radius:14px;background:var(--l1);border:1px solid var(--bsub);display:flex;align-items:center;padding:0 22px"><div class="spectrum" style="width:100%"></div></div><div class="small" style="margin-top:8px;color:var(--fg)">Spectrum · the five logo hues, ≤ 6 px</div></div>
</div>
<div class="g3" style="gap:14px;margin-top:22px">
 <div style="height:92px;border-radius:12px;background:var(--l1);border:1px solid var(--bsub);box-shadow:{el['e1']}"></div>
 <div style="height:92px;border-radius:12px;background:var(--l1);border:1px solid var(--bsub);box-shadow:{el['e2']}"></div>
 <div style="height:92px;border-radius:12px;background:var(--overlay);border:1px solid var(--bsub);box-shadow:{el['overlay']}"></div></div>
<div class="g3" style="gap:14px;margin-top:10px"><span class="cap">e1 · cards</span><span class="cap">e2 · raised</span><span class="cap">overlay · menus, dialogs</span></div>
<p class="cap" style="margin-top:14px">Gradients decorate; they never sit behind body text and never carry meaning alone. Shadows are used only on raised or floating surfaces — not on every component.</p></div>'''
    body = f'<div class="split">{half("dark")}{half("light")}</div><div class="hdr"><span class="logo">{img("logo/existing/capit-symbol.png")}<span>Cap-IT Brand Book</span></span><b>04 · Dual-theme identity</b></div>'
    return Page('dark', CH4, 'Gradients and elevation', body, chrome=False)


def migration():
    rows = [
        ('Brush.BackgroundPrimary', '#0B0D11 / #F4F5F7', 'bg.app', '#09111D / #FFFFFF', 'Navy-tinted dark; white-first light', 'Low'),
        ('Brush.Surface', '#13161C / #FFFFFF', 'surface.layer1', '#142633 / #FFFFFF', 'Same role, new values', 'Low'),
        ('Brush.Accent', '#22CBDD / #0A8FA0', 'brand.primary', '#18DCE8 / #087C88', 'Brighter dark; light now passes 4.5 : 1', 'Low'),
        ('Brush.TextOnAccent', '#03191C / #FFFFFF', 'brand.on-primary', '#04141A / #FFFFFF', 'Fixes light button label 3.85 → 5.1 : 1', 'Low'),
        ('Brush.TextMuted', '#6C7583 / #78818F', 'fg.muted', '#8398A7 / #667182', 'Fixes 3.9–4.2 : 1', 'Low'),
        ('Brush.BorderNormal (inputs)', '#272D36 / #D5DAE1', 'border.control', '#527188 / #8191A8', 'Inputs reach 3 : 1; cards keep border.subtle', 'Medium — visible'),
        ('Brush.Success / Warning / Error', 'see audit', 'status.*', 'see tokens', 'Light variants darkened to ≥ 4.5 : 1', 'Low'),
        ('Brush.Recording', '#F2555A / #E5383E', 'rec.active', '#F3686C / #DC1D23', 'Passes on every text layer', 'Low'),
        ('(new)', '—', 'brand.secondary*', '#7C5CFF … / #6941C6', 'Creative accent: export state, Smart Tracking identity', 'Additive'),
    ]
    trs = ''.join(f'<tr><td class="mono" style="font-size:15px">{a}</td><td class="mono" style="font-size:14.5px">{b}</td><td class="mono" style="font-size:15px;color:var(--brand)">{c}</td><td class="mono" style="font-size:14.5px">{d}</td><td style="font-size:16px">{e}</td><td style="font-size:15px">{f}</td></tr>' for a, b, c, d, e, f in rows)
    body = f'''
<div class="g12" style="height:100%;align-content:start;row-gap:22px">
 <div style="grid-column:1/13"><div class="row" style="align-items:center;gap:16px"><span class="eyebrow">Migration path</span>{tag('proposed')}</div>
  <h1 class="h1" style="margin:12px 0 10px">From today’s tokens to the proposed ones, one key at a time.</h1></div>
 <div style="grid-column:1/13"><table class="tbl"><thead><tr><th>Existing key</th><th>Existing dark / light</th><th>Proposed token</th><th>Proposed dark / light</th><th>Why</th><th>Risk</th></tr></thead><tbody>{trs}</tbody></table></div>
 <div style="grid-column:1/13" class="g4">
  <div class="card" style="padding:22px 26px"><b style="color:var(--fg)">1 · Add</b><p class="small">Merge <span class="mono">avalonia/CapIT.Brand.Proposed.axaml</span> as new <span class="mono">Brand.*</span> keys beside the current ones.</p></div>
  <div class="card" style="padding:22px 26px"><b style="color:var(--fg)">2 · Alias</b><p class="small">Point one <span class="mono">Brush.*</span> key at its <span class="mono">Brand.*</span> brush at a time; review in both themes.</p></div>
  <div class="card" style="padding:22px 26px"><b style="color:var(--fg)">3 · Verify</b><p class="small">Re-run the UI snapshot harness for all pages in both themes; compare against this book.</p></div>
  <div class="card" style="padding:22px 26px"><b style="color:var(--fg)">4 · Retire</b><p class="small">Remove old values only after every screen is signed off. No production styling was changed in this pass.</p></div></div>
</div>'''
    return Page('light', CH4, 'Migration path', body)


def theme_behavior():
    body = f'''
<div style="position:absolute;inset:-120px 0 0 0;background:radial-gradient(40% 50% at 90% 100%,rgba(24,220,232,.12),transparent 70%)"></div>
<div class="g12" style="height:100%;align-content:start;row-gap:28px;position:relative">
 <div style="grid-column:1/8"><div class="eyebrow">Theme behaviour</div><h1 class="h2" style="margin:12px 0 14px;font-size:44px">Switching themes must never touch a recording.</h1>
  <p class="p">Theme is presentation state. In v3.13.1 it lives in <span class="mono">AppSettings.Theme</span> and <span class="mono">App.ApplyTheme</span>, which only sets Avalonia’s <span class="mono">RequestedThemeVariant</span>. The recording engine (<span class="mono">CapIT.Infrastructure.Windows</span>) contains no reference to theme at all — switching mid-recording only repaints brushes.</p></div>
 <div style="grid-column:9/13" class="card"><div class="row" style="gap:12px;align-items:center">{tag('existing')}<b>Verified</b></div><p class="small" style="margin-top:10px">A search of the infrastructure project for “Theme” finds nothing. The floating recording controller is themed like any other window and is never part of the captured frame.</p></div>
 <div style="grid-column:1/13" class="g3">
  <div class="card" style="padding:24px 28px"><div class="h4">Manual &amp; system</div><ul class="list sm" style="font-size:16.5px"><li>Options: System default · Light · Dark (existing).</li><li>“System default” follows the Windows app mode (existing).</li><li>Proposed: the toggle in the title bar shows the <i>resolved</i> theme, not the setting.</li></ul></div>
  <div class="card" style="padding:24px 28px"><div class="h4">Persistence</div><ul class="list sm" style="font-size:16.5px"><li>Stored in <span class="mono">settings.json</span> beside other preferences (existing).</li><li>Applied before the first window is shown, to avoid a flash of the wrong theme.</li><li>The standalone editor (<span class="mono">--review</span>) reads the same preference.</li></ul></div>
  <div class="card" style="padding:24px 28px"><div class="h4">Transitions</div><ul class="list sm" style="font-size:16.5px"><li>Proposed: 180 ms cross-fade of surfaces; none under reduced motion.</li><li>Never animate the live preview, meters or the REC indicator.</li><li>No re-initialisation of capture, audio or encode.</li></ul></div>
 </div>
 <div style="grid-column:1/13" class="g3">
  <div class="card" style="padding:22px 28px"><div class="h4">Correct variants</div><p class="small" style="font-size:16.5px">Icons use <span class="mono">currentColor</span>, so they follow the theme. The logo uses the stacked-reversed lockup on Midnight and the standard one on Arctic; the colour symbol is unchanged in both.</p></div>
  <div class="card" style="padding:22px 28px"><div class="h4">High-DPI</div><p class="small" style="font-size:16.5px">Strokes scale with icon size (existing). Hairlines are 1 px at 100 % and stay 1 device-independent px at 125–200 %; verified renders were produced at 125 % scaling.</p></div>
  <div class="card" style="padding:22px 28px"><div class="h4">Semantic colour</div><p class="small" style="font-size:16.5px">Components reference semantic tokens only. A component that needs a raw hex has found a missing token.</p></div></div>
</div>'''
    return Page('dark', CH4, 'Theme behaviour', body)


# ---------------------------------------------------------------------------------------------- typography
def typo_intro():
    t = TOK['typography']['scale']
    names = [('display', 'Page title (Display)'), ('title', 'Title'), ('section', 'Section heading'), ('cardTitle', 'Card title'), ('body', 'Body'), ('rowTitle', 'Row title'),
             ('secondary', 'Secondary / description'), ('caption', 'Caption'), ('overline', 'Overline')]
    trs = ''
    for k, n in names:
        s = t[k]
        extra = ', '.join(f'{a} {b}' for a, b in s.items() if a not in ('size', 'weight'))
        trs += f'<tr><td>{n}</td><td class="num mono">{s["size"]:g}</td><td class="num mono">{s["weight"]}</td><td class="mono" style="font-size:15px">{extra or "—"}</td></tr>'
    weights = ''.join(f'<div style="flex:1"><div style="font-size:64px;font-weight:{w};letter-spacing:-.03em;line-height:1">Aa</div><div class="cap">{n} {w}</div></div>' for w, n in ((300, 'Light'), (400, 'Regular'), (500, 'Medium'), (600, 'SemiBold'), (700, 'Bold')))
    body = f'''
<div class="g12" style="height:100%;align-content:start;row-gap:26px">
 <div style="grid-column:1/8"><div class="row" style="align-items:center;gap:14px"><span class="eyebrow">Typeface</span>{tag('existing')}</div>
  <div class="hero" style="margin:8px 0 6px;font-size:112px">Inter</div>
  <p class="p">Inter is the interface typeface (bundled via Avalonia.Fonts.Inter, SIL Open Font License 1.1), with Segoe UI Variable Text and Segoe UI as fallbacks. Cascadia Mono, then Consolas, sets timers, shortcuts and code. One family keeps the product calm; hierarchy comes from size, weight and space.</p></div>
 <div style="grid-column:9/13" class="card"><div class="eyebrow">Stacks</div><p class="small mono" style="margin-top:10px;font-size:15px">Inter, Segoe UI Variable Text, Segoe UI, sans-serif</p><p class="small mono" style="font-size:15px">Cascadia Mono, Consolas, Courier New, monospace</p><p class="cap" style="margin-top:10px">Brand headline, UI, body: all Inter. No second display face is proposed.</p></div>
 <div style="grid-column:1/13" class="row" style="gap:20px">{weights}</div>
 <div style="grid-column:1/13"><div class="row" style="align-items:center;gap:14px;margin-bottom:6px"><span class="h3">Application type scale</span>{tag('existing')}</div>
 <table class="tbl tight" style="font-size:15.5px"><thead><tr><th>Role</th><th class="num">Size px</th><th class="num">Weight</th><th>Other</th></tr></thead><tbody>{trs}</tbody></table></div>
</div>'''
    return Page('light', CH5, 'Typeface and application scale', body)


def typo_editorial():
    t = TOK['typographyEditorial']['scale']
    samples = [('hero', 'Capture with intention.', 120), ('display', 'Make every recording count.', 72), ('h1', 'From screen to story', 48), ('h2', 'Smart Tracking follows the action', 32),
               ('h3', 'Record. Refine. Share.', 24), ('h4', 'Review & Export workspace', 20), ('subtitle', 'A camera that glides toward your cursor and caret, then eases back out.', 22),
               ('bodyLarge', 'Cap-IT keeps source selection, live preview, recording, effects and export in one place.', 18),
               ('body', 'Trim, compose, add text or a device frame, then keep the MP4 or export a GIF.', 15),
               ('caption', 'Original recording preserved · edits save automatically', 12), ('overline', 'Capture · Smart Tracking · Audio', 11)]
    rows = ''
    for k, text, size in samples:
        s = t[k]
        ls = f'letter-spacing:{s.get("letterSpacing",0)}em;' if s.get('letterSpacing') else ''
        up = 'text-transform:uppercase;' if s.get('case') == 'upper' else ''
        col = 'var(--brand)' if k == 'overline' else ('var(--fg2)' if k in ('subtitle', 'bodyLarge', 'body', 'caption') else 'var(--fg)')
        rows += f'''<div style="display:grid;grid-template-columns:200px 1fr;align-items:baseline;border-bottom:1px solid var(--div);padding:{8 if size>40 else 5}px 0">
<div class="mono" style="font-size:14px;color:var(--fgm)">{k}<br>{s['size']}/{s['weight']} · {s['lineHeight']}</div>
<div style="font-size:{s['size']}px;font-weight:{s['weight']};line-height:{s['lineHeight']};{ls}{up}color:{col};white-space:nowrap;overflow:hidden">{text}</div></div>'''
    body = f'''
<div class="row" style="align-items:center;justify-content:space-between"><div><div class="eyebrow">Editorial scale · for the book, website and marketing</div></div>{tag('proposed')}</div>
<div style="margin-top:2px">{rows}</div>
<p class="cap" style="margin-top:6px;font-size:14px">Sizes in px on a 1920-wide canvas; line-heights are unitless. Negative tracking tightens as size grows (−0.04 em at 120 px, 0 at 16 px). Capitalisation: sentence case everywhere except overlines (uppercase, +0.14 em) and the product name.</p>'''
    return Page('dark', CH5, 'Editorial type scale', body, glow=True)


def typo_rules():
    body = f'''
<div class="g12" style="height:100%;align-content:start;row-gap:24px">
 <div style="grid-column:1/13"><div class="eyebrow">Numerics, code, fallback and language</div><h1 class="h1" style="margin:12px 0 0">Details that keep type honest.</h1></div>
 <div style="grid-column:1/5" class="card"><div class="h4">Numeric displays</div>
  <div class="mono" style="font-size:54px;margin:14px 0 6px;color:var(--brand);letter-spacing:-.02em">00:12:04</div><div class="cap">Recording timer — tabular figures, SemiBold (existing “timer” style).</div>
  <div style="font-size:54px;font-weight:600;margin:16px 0 6px;font-variant-numeric:tabular-nums;letter-spacing:-.03em">1920 × 1080</div><div class="cap">Resolution, frame rate and bitrate use tabular figures so columns never jitter.</div></div>
 <div style="grid-column:5/9" class="card"><div class="h4">Code &amp; shortcuts</div>
  <pre class="mono" style="font-size:16px;line-height:1.6;color:var(--fg2);margin:14px 0;background:var(--l2);padding:16px;border-radius:10px;border:1px solid var(--bsub)">dotnet build CapIT.sln
dotnet run --project src\\CapIT.Desktop</pre>
  <div class="row" style="gap:8px;flex-wrap:wrap"><span class="k">Ctrl</span><span class="k">Shift</span><span class="k">D</span> <span class="cap" style="align-self:center">toggle drawing</span></div></div>
 <div style="grid-column:9/13" class="card"><div class="h4">Responsive behaviour</div><ul class="list sm"><li>Application text does not scale with window width; layout reflows instead.</li><li>Headlines may wrap to two lines, never truncate.</li><li>Single-line values truncate with an ellipsis and a tooltip.</li></ul></div>
 <div style="grid-column:1/7" class="card"><div class="h4">Localisation</div><ul class="list sm"><li>Inter covers Latin, Greek and Cyrillic. For Sinhala, Tamil, CJK and other scripts the interface falls through to Segoe UI Variable / Segoe UI and then Windows system font fallback — verify rendering per language before shipping it.</li><li>Allow ~30–40 % text expansion in buttons and rows; never size a control to its English label.</li><li>Dates, numbers and file names follow the system locale.</li></ul></div>
 <div style="grid-column:7/13" class="card"><div class="h4">Accessibility</div><ul class="list sm"><li>Body text ≥ 13 px in the app (12.5 px secondary, 11.5 px captions are for supporting text only).</li><li>Muted text is held to 4.5 : 1 in the proposed tokens.</li><li>Honour Windows text scaling; no fixed-height text containers.</li><li>Do not redistribute fonts other than Inter (OFL) and Cascadia Mono (OFL); both licences travel with the source folder.</li></ul></div>
</div>'''
    return Page('light', CH5, 'Numerics, code, fallback and language', body)


# ------------------------------------------------------------------------------------------------ layout
def layout_dark():
    sp = TOK['spacing']['scale']
    bars = ''.join(f'<div style="display:flex;align-items:center;gap:16px;margin:5px 0"><span class="mono" style="width:92px;color:var(--fgm);font-size:15px">Space.{k}</span><div style="height:16px;width:{vv*5}px;background:var(--brand);border-radius:4px"></div><span class="mono" style="font-size:15px;color:var(--fg2)">{vv:g}</span></div>' for k, vv in sp.items())
    rad = TOK['radius']['scale']
    rads = ''.join(f'<div style="text-align:center"><div style="width:84px;height:84px;background:var(--l2);border:1.5px solid var(--bctl);border-radius:{min(vv,42)}px"></div><div class="mono" style="font-size:14px;margin-top:8px;color:var(--fg2)">{k}<br>{vv:g}</div></div>' for k, vv in rad.items())
    hts = TOK['controlHeight']['scale']
    hbs = ''.join(f'<div class="row" style="align-items:center;gap:14px;margin:6px 0"><div class="btn primary" style="height:{vv}px;width:150px;padding:0;font-size:13px;display:inline-flex">{k} · {vv:g}</div></div>' for k, vv in hts.items() if k in ('small', 'default', 'large'))
    cols = ''.join('<div style="background:rgba(24,220,232,.14);border:1px solid rgba(24,220,232,.5);height:70px;border-radius:4px"></div>' for _ in range(12))
    body = f'''
<div class="row" style="align-items:center;gap:14px"><span class="eyebrow">Spacing, grid, radius, height</span>{tag('existing')}</div><h1 class="h1" style="margin:10px 0 16px">A 4 px base, an 8 px rhythm.</h1>
<div class="g2" style="gap:28px;align-items:start">
 <div class="col" style="gap:22px">
  <div class="card" style="padding:24px 28px"><div class="h4" style="margin-bottom:6px">Spacing scale</div>{bars}<p class="cap" style="margin-top:8px">4 · 8 · 12 · 16 · 20 · 24 · 32 · 40 · 48. Views use these tokens; ad-hoc values are not allowed.</p></div>
  <div class="card" style="padding:24px 28px"><div class="row" style="justify-content:space-between"><span class="h4" style="margin-bottom:12px">Editorial 12-column grid</span>{tag('proposed')}</div><div style="display:grid;grid-template-columns:repeat(12,1fr);gap:6px">{cols}</div>
   <p class="cap" style="margin-top:10px">For this book and marketing layouts: 96 px margins, 24 px gutters, 12 columns on a 1920 canvas; baseline 8 px.</p></div></div>
 <div class="col" style="gap:22px">
  <div class="card" style="padding:24px 28px"><div class="h4" style="margin-bottom:14px">Corner radii</div><div class="row" style="gap:22px;flex-wrap:wrap">{rads}</div>
   <p class="cap" style="margin-top:12px">Cards use Large (12), controls Medium (8), pills 999. One radius per nesting level; never mix radii on adjoining edges.</p></div>
  <div class="card" style="padding:24px 28px"><div class="h4" style="margin-bottom:12px">Control heights</div><div class="spec">{hbs}</div><p class="cap" style="margin-top:10px">Small 30 · Default 36 · Large 44 · Title bar 44 · Nav item 36 (shown at 135 %).</p></div></div>
</div>'''
    return Page('dark', CH6, 'Spacing, grid, radius and height', body)


def layout_light():
    L = TOK['layout']
    def wire(compact):
        sbw = 68 if compact else 232
        return f'''<div style="position:relative;height:250px;border-radius:12px;border:1.5px solid var(--bctl);background:var(--bg2);overflow:hidden">
<div style="height:30px;background:var(--bg);border-bottom:1px solid var(--bsub);display:flex;align-items:center;padding:0 12px;font-size:12px;color:var(--fgm)">title bar · 44</div>
<div style="position:absolute;top:30px;bottom:0;left:0;width:{sbw*0.78}px;background:var(--bg);border-right:1px solid var(--bsub);font-size:12px;color:var(--fgm);padding:8px">sidebar {sbw}</div>
<div style="position:absolute;top:30px;bottom:0;left:{sbw*0.78}px;right:0;padding:16px"><div style="height:26px;width:160px;background:var(--l3);border-radius:6px;margin-bottom:12px"></div>
<div style="display:grid;grid-template-columns:2fr 1fr;gap:12px;height:140px"><div style="background:var(--l1);border:1px solid var(--bsub);border-radius:10px"></div><div style="background:var(--l1);border:1px solid var(--bsub);border-radius:10px"></div></div></div></div>'''
    bp = TOK['breakpoints']
    body = f'''
<div class="g12" style="height:100%;align-content:start;row-gap:24px">
 <div style="grid-column:1/13"><div class="row" style="align-items:center;gap:14px"><span class="eyebrow">Window layout, scaling, minimum sizes</span>{tag('existing')}</div><h1 class="h2" style="margin:10px 0 0">One shell. Two sidebar widths.</h1></div>
 <div style="grid-column:1/7"><div class="h4" style="margin-bottom:10px">Expanded — sidebar {L['sidebar']:g} px</div>{wire(False)}</div>
 <div style="grid-column:7/13"><div class="h4" style="margin-bottom:10px">Compact — sidebar {L['sidebarCompact']:g} px (icons only)</div>{wire(True)}</div>
 <div style="grid-column:1/5" class="card"><div class="h4">Metrics</div><table class="tbl tight" style="font-size:16px"><tbody>
  <tr><td>Page content max</td><td class="num mono">{L['pageMax']:g}</td></tr><tr><td>Form width</td><td class="num mono">{L['form']:g}</td></tr><tr><td>Inspector</td><td class="num mono">{L['inspector']:g}</td></tr><tr><td>Control column</td><td class="num mono">{L['controlColumn']:g}</td></tr>
  <tr><td>Page padding</td><td class="num mono">32 28 32 40</td></tr><tr><td>Card padding</td><td class="num mono">20 · compact 16</td></tr></tbody></table></div>
 <div style="grid-column:5/9" class="card"><div class="h4">Window sizes</div><table class="tbl tight" style="font-size:16px"><tbody><tr><td>Main · default</td><td class="num mono">1360 × 860</td></tr><tr><td>Main · minimum</td><td class="num mono">1024 × 640</td></tr><tr><td>Review · default</td><td class="num mono">1440 × 900</td></tr><tr><td>Review · minimum</td><td class="num mono">960 × 600</td></tr></tbody></table>
  <p class="cap" style="margin-top:8px">From <span class="mono">MainWindow.axaml</span> and <span class="mono">ReviewWindow.axaml</span>.</p></div>
 <div style="grid-column:9/13" class="card"><div class="row" style="justify-content:space-between"><span class="h4">Breakpoints</span>{tag('proposed')}</div><table class="tbl tight" style="font-size:16px"><tbody><tr><td>Compact</td><td class="num mono">&lt; {bp['default']}</td></tr><tr><td>Default</td><td class="num mono">{bp['default']}–{bp['wide']-1}</td></tr><tr><td>Wide</td><td class="num mono">{bp['wide']}+</td></tr></tbody></table><p class="cap" style="margin-top:8px">Sidebar auto-compacts in the compact band. High-DPI: layout in device-independent px; reference captures at 125 %.</p></div>
 <div style="grid-column:1/13" class="row" style="gap:20px"><div class="do" style="flex:1"><div class="h4">Proper alignment</div><p class="small">Content left edges align to the 32 px page padding; labels and values sit on the same 8 px baseline grid; icons centre on text x-height.</p></div><div class="dont" style="flex:1"><div class="h4">Improper alignment</div><p class="small">Mixed paddings (e.g. 18 / 22) inside one card; controls of different heights in one row; centred text next to left-aligned labels.</p></div></div>
</div>'''
    return Page('light', CH6, 'Window layout and scaling', body)


# --------------------------------------------------------------------------------------------- icons
def icons_dark():
    pick = ['Home', 'Monitor', 'AppWindow', 'Focus', 'Video', 'Pen', 'Sparkles', 'Volume', 'Mic', 'Film', 'Settings', 'Sliders', 'ZoomIn', 'Cursor', 'CursorClick', 'Scissors',
            'Image', 'Layers', 'Frame', 'Ratio', 'Shadow', 'Stamp', 'Keyboard', 'Folder', 'Search', 'Grid', 'List', 'Export', 'Download', 'Gif', 'Timer', 'Clock', 'Eye', 'Wave',
            'Palette', 'Sun', 'Moon', 'CheckCircle', 'AlertTriangle', 'Info', 'Fill.Record', 'Fill.Play', 'Fill.Pause', 'Fill.Stop']
    pick = [p for p in pick if p in ICONS]
    cells = ''.join(f'<div style="text-align:center;padding:12px 4px;border-radius:12px;background:var(--l1);border:1px solid var(--bsub)"><div style="font-size:34px;color:var(--fg)">{icon(n)}</div><div class="mono" style="font-size:12px;color:var(--fgm);margin-top:6px">{n}</div></div>' for n in pick)
    sizes = ''.join(f'<div style="text-align:center"><div style="font-size:{s}px;color:var(--brand)">{icon("Focus")}</div><div class="cap">{s}</div></div>' for s in (14, 16, 18, 20, 24, 48))
    body = f'''
<div class="g12" style="height:100%;align-content:start;row-gap:24px">
 <div style="grid-column:1/5"><div class="row" style="align-items:center;gap:14px"><span class="eyebrow">Iconography</span>{tag('existing')}</div><h1 class="h1" style="margin:12px 0 16px">One family, drawn on a 24 px grid.</h1>
  <p class="p">{len(ICONS)} icons in <span class="mono">Styles/Icons.axaml</span>: round-capped, round-joined outline strokes at 1.75 px on a 24 × 24 grid (Lucide-compatible). Strokes scale with the icon, so a 14 px and a 24 px icon share optical weight. Record, play, pause and stop are the only solid glyphs.</p>
  <table class="tbl" style="margin:18px 0;font-size:17px"><tbody><tr><td>Stroke</td><td class="num mono">1.75 px · round cap / join</td></tr><tr><td>Optical bounds</td><td class="num mono">20 px live area · 2 px padding</td></tr><tr><td>Corner treatment</td><td class="num mono">2–3 px radius</td></tr><tr><td>Sizes</td><td class="num mono">14 · 16 · 18 · 20 · 24</td></tr></tbody></table>
  <div class="row" style="align-items:flex-end;gap:22px">{sizes}</div></div>
 <div style="grid-column:6/13"><div style="display:grid;grid-template-columns:repeat(7,1fr);gap:12px">{cells}</div></div>
</div>'''
    return Page('dark', CH7, 'Icon family', body)


def icons_light():
    feats = TOK['features']
    cells = ''.join(f'''<div class="card" style="padding:20px 22px;display:flex;gap:16px;align-items:center"><div style="width:56px;height:56px;border-radius:14px;background:{f['colorLight']}1F;color:{f['colorLight']};display:flex;align-items:center;justify-content:center;font-size:28px">{icon(f['icon'].replace('Icon.',''))}</div>
<div><b style="color:var(--fg);font-size:19px">{f['name']}</b><div class="cap mono">{f['icon']}</div></div></div>''' for f in feats)
    def state(label, color, extra=''):
        return f'<div class="cell"><div style="font-size:34px;color:{color};{extra}">{icon("Monitor")}</div><small>{label}</small></div>'
    body = f'''
<div class="g12" style="height:100%;align-content:start;row-gap:24px">
 <div style="grid-column:1/13"><div class="eyebrow">Icon states &amp; feature icons</div><h1 class="h1" style="margin:12px 0 0">Colour follows meaning, not decoration.</h1></div>
 <div style="grid-column:1/6" class="card"><div class="h4" style="margin-bottom:16px">States — Arctic</div><div class="row" style="gap:34px">{state('Default','var(--fg2)')}{state('Hover','var(--fg)')}{state('Active','var(--brand)')}{state('Disabled','var(--fgd)')}</div>
  <div class="t-dark" style="margin:20px -32px -32px;padding:22px 32px;border-radius:0 0 16px 16px;background:var(--l1)"><div class="row" style="gap:34px">{state('Default','var(--fg2)')}{state('Hover','var(--fg)')}{state('Active','var(--brand)')}{state('Disabled','var(--fgd)')}</div><div class="cap" style="margin-top:10px">Same icon on Midnight</div></div></div>
 <div style="grid-column:6/13" class="card"><div class="h4" style="margin-bottom:10px">Usage rules</div><ul class="list sm"><li><b style="color:var(--fg)">Outlined</b> for navigation and commands; <b style="color:var(--fg)">filled</b> only for transport (record, play, pause, stop).</li><li>Default colour is secondary text; the active item uses the brand accent; disabled uses the disabled foreground.</li><li>Pair every icon-only button with a tooltip and an accessible name.</li><li>Icons inherit text colour (<span class="mono">currentColor</span>) so themes need no icon variants.</li><li>Feature colours (below) tint the icon chip, never the label.</li></ul></div>
 <div style="grid-column:1/13"><div class="row" style="align-items:center;gap:14px;margin-bottom:12px"><span class="h3">Feature icons</span>{tag('existing')}<span class="cap">icons · colours are proposed (chapter 11)</span></div><div class="g3" style="gap:16px">{cells}</div></div>
</div>'''
    return Page('light', CH7, 'Icon states and feature icons', body)


# ---------------------------------------------------------------------------------------- graphic language
def graphic_tiles(dark):
    c = 'var(--brand)'
    def tile(title, svg, note):
        return f'''<div class="card" style="padding:20px"><div style="height:190px;display:flex;align-items:center;justify-content:center;border-radius:12px;background:var(--bg);border:1px solid var(--bsub);overflow:hidden">{svg}</div>
<div class="h4" style="margin-top:14px">{title}</div><div class="cap">{note}</div></div>'''
    brackets = f'<div style="color:{c}">{bracket_svg(260,160,sw=4,arm=40)}</div>'
    focus = f'''<svg viewBox="0 0 220 160" width="260" height="190" fill="none" stroke="{c}" stroke-width="3" stroke-linecap="round"><circle cx="110" cy="80" r="14"/><circle cx="110" cy="80" r="40" opacity=".5" stroke-dasharray="3 9"/><path d="M110 20v22M110 118v22M50 80h22M148 80h22"/></svg>'''
    viewport = f'''<svg viewBox="0 0 260 170" width="270" height="180" fill="none" stroke-width="2.5"><rect x="6" y="6" width="248" height="158" rx="10" stroke="var(--bctl)"/><rect x="70" y="40" width="120" height="76" rx="6" stroke="{c}" stroke-width="3.5"/><path d="M70 40 6 6M190 40l64-34M70 116 6 164M190 116l64 48" stroke="{c}" opacity=".35" stroke-dasharray="4 6"/></svg>'''
    recind = f'''<div style="display:flex;align-items:center;gap:14px"><span style="width:20px;height:20px;border-radius:50%;background:var(--rec);box-shadow:0 0 0 6px color-mix(in srgb,var(--rec) 24%,transparent)"></span><span class="mono" style="font-size:22px;color:var(--fg);letter-spacing:.06em">REC 00:12:04</span></div>'''
    trail = f'''<svg viewBox="0 0 260 160" width="270" height="170" fill="none" stroke-linecap="round"><path d="M14 130C60 130 70 40 120 60S190 120 240 34" stroke="{c}" stroke-width="3" opacity=".25" stroke-dasharray="2 9"/><path d="M150 96C170 112 200 96 240 34" stroke="{c}" stroke-width="3.5"/><circle cx="240" cy="34" r="9" fill="{c}"/></svg>'''
    timeline = f'''<svg viewBox="0 0 260 120" width="270" height="124" fill="none"><g stroke="var(--bctl)" stroke-width="2">{''.join(f'<path d="M{20+i*16} {70 if i%4 else 58}v{14 if i%4 else 26}"/>' for i in range(15))}</g><rect x="64" y="40" width="112" height="60" rx="6" fill="{c}" fill-opacity=".14" stroke="{c}" stroke-width="3"/><path d="M110 24v90" stroke="var(--fg)" stroke-width="2.5"/></svg>'''
    cross = f'''<svg viewBox="0 0 160 160" width="190" height="190" fill="none" stroke="{c}" stroke-width="2.5" stroke-linecap="round"><path d="M80 8v52M80 100v52M8 80h52M100 80h52"/><circle cx="80" cy="80" r="6" fill="{c}"/></svg>'''
    layers = f'''<svg viewBox="0 0 240 170" width="250" height="176" fill="none" stroke-width="2.5"><rect x="64" y="8" width="168" height="108" rx="12" stroke="var(--bctl)" fill="var(--l1)"/><rect x="38" y="30" width="168" height="108" rx="12" stroke="var(--bctl)" fill="var(--l1)"/><rect x="12" y="52" width="168" height="108" rx="12" stroke="{c}" stroke-width="3.5" fill="var(--l2)"/></svg>'''
    grid = f'''<div style="width:100%;height:100%;background-image:linear-gradient(var(--div) 1px,transparent 1px),linear-gradient(90deg,var(--div) 1px,transparent 1px);background-size:24px 24px;position:relative"><div style="position:absolute;inset:0;background:radial-gradient(60% 70% at 70% 30%,color-mix(in srgb,var(--brand) 22%,transparent),transparent 70%)"></div></div>'''
    return [tile('Capture brackets', brackets, 'The frame around anything captured.'), tile('Focus target', focus, 'Where the camera is aiming.'), tile('Viewport frame', viewport, 'The crop the camera will show.'),
            tile('Recording indicator', recind, 'Dot + label + timer. Never colour alone.'), tile('Camera path', trail, 'Eased path A → B; dotted = history.'), tile('Timeline geometry', timeline, 'Ticks, selection, playhead.'),
            tile('Precision crosshair', cross, 'Alignment and measurement.'), tile('Layered frames', layers, 'Composition depth. The page grid behind is the 9th device: ≤ 55 % opacity.')]


def graphics_dark():
    t = graphic_tiles(True)
    body = f'''
<div class="row" style="justify-content:space-between;align-items:flex-end"><div><div class="eyebrow">Brand graphic language</div><h1 class="h1" style="margin:12px 0 0">Frames, focus and motion.</h1></div>{tag('proposed')}</div>
<p class="lead" style="max-width:1600px;margin:12px 0 22px">Original devices drawn from what the product does: framing a region, following it, and keeping time. Built from the interface’s own 1.75–4 px round-capped stroke so they sit comfortably next to icons.</p>
<div class="g4" style="gap:22px">{''.join(t)}</div>'''
    return Page('dark', CH8, 'Graphic language', body, grid=True)


def graphics_light():
    body = f'''
<div class="g12" style="height:100%;align-content:start;row-gap:26px">
 <div style="grid-column:1/13"><div class="eyebrow">Graphic language in use</div><h1 class="h1" style="margin:12px 0 0">Density, background and restraint.</h1></div>
 <div style="grid-column:1/5" class="card" style="padding:0"><div style="height:280px;border-radius:12px;background:var(--bg2);position:relative;overflow:hidden;border:1px solid var(--bsub)"><div style="position:absolute;left:36px;bottom:34px"><div class="eyebrow">Level 0</div><div class="h3" style="margin-top:6px">Quiet</div></div></div><p class="small" style="margin-top:12px"><b style="color:var(--fg)">Application surfaces.</b> No decoration. The interface is the content.</p></div>
 <div style="grid-column:5/9" class="card" style="padding:0"><div style="height:280px;border-radius:12px;background:var(--bg);position:relative;overflow:hidden;border:1px solid var(--bsub)"><div style="position:absolute;right:30px;top:26px;color:var(--brand);opacity:.9">{bracket_svg(150,100,sw=3,arm=26)}</div><div style="position:absolute;left:36px;bottom:34px"><div class="eyebrow">Level 1</div><div class="h3" style="margin-top:6px">Framed</div></div></div><p class="small" style="margin-top:12px"><b style="color:var(--fg)">Dialogs, empty states, chapter pages.</b> One bracket or one focus mark.</p></div>
 <div style="grid-column:9/13" class="card" style="padding:0"><div style="height:280px;border-radius:12px;background:var(--bg);position:relative;overflow:hidden;border:1px solid var(--bsub);background-image:linear-gradient(var(--div) 1px,transparent 1px),linear-gradient(90deg,var(--div) 1px,transparent 1px);background-size:32px 32px"><div style="position:absolute;inset:0;background:radial-gradient(60% 80% at 80% 20%,rgba(8,124,136,.14),transparent 70%)"></div><div style="position:absolute;left:36px;bottom:34px"><div class="eyebrow">Level 2</div><div class="h3" style="margin-top:6px">Expressive</div></div></div><p class="small" style="margin-top:12px"><b style="color:var(--fg)">Marketing and covers.</b> Grid, glow, brackets, trail — never all at full strength.</p></div>
 <div style="grid-column:1/7" class="do"><div class="h4">Do</div><ul class="list sm"><li>Use at most two devices per composition.</li><li>Keep brackets at a 4 : 3–16 : 9 ratio; they should look like a capture frame.</li><li>Let the grid fade out toward the content.</li></ul></div>
 <div style="grid-column:7/13" class="dont"><div class="h4">Don’t</div><ul class="list sm"><li>Add decoration behind running text or UI.</li><li>Animate brackets in the product UI (reserve for onboarding and marketing).</li><li>Use the logo’s five hues as a gradient fill; the spectrum strip is ≤ 6 px.</li></ul></div>
</div>'''
    return Page('light', CH8, 'Graphic language in use', body)


# --------------------------------------------------------------------------------------------- motion
def spring_svg():
    """Critically damped vs underdamped step response (closed form), the Smart Tracking model."""
    w, h = 560, 260
    def path(fn, color, dash=''):
        pts = []
        for i in range(0, 121):
            t = i / 120 * 1.6
            x = 30 + t / 1.6 * (w - 60)
            y = h - 30 - fn(t) * (h - 70)
            pts.append(f'{x:.1f},{y:.1f}')
        return f'<polyline points="{" ".join(pts)}" fill="none" stroke="{color}" stroke-width="3.5" stroke-linecap="round" {dash}/>'
    st = 0.5
    om = 2 / st
    crit = lambda t: 1 - (1 + om * t) * math.exp(-om * t)
    zeta = 0.35
    wn = om
    wd = wn * math.sqrt(1 - zeta ** 2)
    under = lambda t: 1 - math.exp(-zeta * wn * t) * (math.cos(wd * t) + zeta / math.sqrt(1 - zeta ** 2) * math.sin(wd * t))
    return f'''<svg viewBox="0 0 {w} {h}" width="100%" fill="none"><line x1="30" y1="{h-30}" x2="{w-30}" y2="{h-30}" stroke="var(--bctl)"/><line x1="30" y1="{h-30-(h-70)}" x2="{w-30}" y2="{h-30-(h-70)}" stroke="var(--bctl)" stroke-dasharray="4 6"/>
{path(under,'var(--err)','stroke-dasharray="3 7"')}{path(crit,'var(--brand)')}
<text x="{w-34}" y="{h-30-(h-70)-8}" fill="var(--fgm)" font-size="14" text-anchor="end">target</text><text x="34" y="{h-8}" fill="var(--fgm)" font-size="14">0 s</text><text x="{w-34}" y="{h-8}" fill="var(--fgm)" font-size="14" text-anchor="end">1.6 s</text></svg>'''


def curve_svg(bez, color):
    x1, y1, x2, y2 = bez
    w, h = 150, 150
    pts = []
    for i in range(0, 41):
        t = i / 40
        x = 3 * (1 - t) ** 2 * t * x1 + 3 * (1 - t) * t ** 2 * x2 + t ** 3
        y = 3 * (1 - t) ** 2 * t * y1 + 3 * (1 - t) * t ** 2 * y2 + t ** 3
        pts.append(f'{14 + x * (w - 28):.1f},{h - 14 - y * (h - 28):.1f}')
    return f'<svg viewBox="0 0 {w} {h}" width="150" height="150" fill="none"><rect x="14" y="14" width="{w-28}" height="{h-28}" stroke="var(--bctl)" stroke-dasharray="3 5"/><polyline points="{" ".join(pts)}" stroke="{color}" stroke-width="4" stroke-linecap="round"/></svg>'


def motion_dark():
    principles = [('Purposeful', 'Motion explains a change; it never decorates.'), ('Smooth', 'Eased in and out; no abrupt starts.'), ('Predictable', 'The same action always moves the same way.'),
                  ('Responsive', 'Feedback begins within 80–120 ms of input.'), ('Accessible', 'Reduced motion removes travel, not feedback.'), ('Performance-conscious', 'Transform and opacity only; never animate layout in a recording.')]
    pc = ''.join(f'<div class="card" style="padding:22px 26px"><div class="eyebrow" style="font-size:14px">{i+1:02d}</div><div class="h4" style="margin:6px 0">{a}</div><p class="small">{b}</p></div>' for i, (a, b) in enumerate(principles))
    body = f'''
<div class="g12" style="height:100%;align-content:start;row-gap:24px">
 <div style="grid-column:1/13"><div class="eyebrow">Motion identity</div><h1 class="h1" style="margin:12px 0 0">Cap-IT is a motion product. Its motion is calm and exact.</h1></div>
 <div style="grid-column:1/8" class="g3" style="gap:16px">{pc}</div>
 <div style="grid-column:8/13" class="card"><div class="row" style="justify-content:space-between;align-items:center"><span class="h4">Smart Tracking camera</span>{tag('existing')}</div>
  <div style="margin-top:8px">{spring_svg()}</div>
  <p class="small"><b style="color:var(--brand)">Critically damped</b> (solid): fastest approach with no overshoot. <b style="color:var(--err)">Underdamped</b> (dotted, shown for contrast only): sails past the target and swings back — never used. In/out smooth times 0.50 s / 0.75 s, pan 0.32 s; framerate-independent.</p></div>
 <div style="grid-column:1/13" class="card"><div class="row" style="gap:36px;align-items:center">
   <div><div class="eyebrow" style="font-size:14px">Durations</div><div class="row" style="gap:20px;margin-top:8px"><div><span class="num-big" style="font-size:54px">120</span><span class="cap"> fast</span></div><div><span class="num-big" style="font-size:54px">180</span><span class="cap"> normal</span></div><div><span class="num-big" style="font-size:54px">220</span><span class="cap"> slow</span></div></div><div class="cap">ms · existing <span class="mono">Duration.*</span></div></div>
   <div style="flex:1"><div class="eyebrow" style="font-size:14px">Easing (proposed)</div><div class="row" style="gap:16px;margin-top:8px">{curve_svg((.22,1,.36,1),'var(--brand)')}{curve_svg((.16,1,.3,1),'var(--sec-t)')}{curve_svg((.4,0,1,1),'var(--warn)')}<div class="small" style="align-self:center">standard · enter · exit<br>CubicEaseOut is the existing curve for entrances.</div></div></div></div></div>
</div>'''
    return Page('dark', CH9, 'Motion principles', body)


def motion_light():
    specs = TOK['motionProposed']['specs']
    trs = ''.join(f'<tr><td>{s["name"]}</td><td class="num mono">{s["ms"]}</td><td class="mono" style="font-size:15px">{s["easing"]}</td><td style="font-size:16px">{s["props"]}</td><td class="num mono">{s["distance"] or "—"}</td><td class="num mono">{s["scale"] if s["scale"] != 1 else "—"}</td></tr>' for s in specs)
    body = f'''
<div class="g12" style="height:100%;align-content:start;row-gap:22px">
 <div style="grid-column:1/13"><div class="row" style="align-items:center;gap:14px"><span class="eyebrow">Motion specifications</span>{tag('proposed')}</div><h1 class="h1" style="margin:12px 0 0">Interface motion vs rendered camera motion.</h1></div>
 <div style="grid-column:1/9"><table class="tbl"><thead><tr><th>Interaction</th><th class="num">ms</th><th>Easing</th><th>Properties</th><th class="num">Dist. px</th><th class="num">Scale</th></tr></thead><tbody>{trs}</tbody></table></div>
 <div style="grid-column:9/13" class="col" style="gap:16px">
  <div class="card" style="margin-bottom:16px;padding:24px 28px"><div class="h4">Two kinds of motion</div><p class="small" style="margin-top:8px"><b style="color:var(--fg)">Interface</b> motion uses eased curves, ≤ 220 ms, and only transform and opacity. <b style="color:var(--fg)">Camera</b> motion lives in the recorded video: a critically damped spring on the crop rectangle, with no overshoot, using real elapsed time so it stays smooth when frames arrive unevenly.</p></div>
  <div class="card" style="margin-bottom:16px;padding:24px 28px"><div class="h4">Reduced motion</div><p class="small" style="margin-top:8px">Replace travel and scale with a 0–80 ms opacity change. Keep every state change; remove only the journey. Smart Tracking’s camera is a recording feature chosen by the user, so it is not suppressed by this setting.</p></div>
  <div class="card" style="padding:24px 28px"><div class="h4">Sharp text in motion</div><p class="small" style="margin-top:8px">Move whole pixels at rest; allow sub-pixel translation only during the move. Never scale text up from below 1× — start at 1× and scale down.</p></div></div>
</div>'''
    return Page('light', CH9, 'Motion specifications', body)


def build():
    def T(page, label):
        page.toc = label
        return page
    pages = [dual_opener(), existing_palette(), audit(), palette_page('dark'), palette_page('light')]
    pages += token_pages('dark') + token_pages('light')
    pages += [matrix_page(), gradients_page(), migration(), theme_behavior()]
    pages += [T(typo_intro(), '05 · Typography'), typo_editorial(), typo_rules()]
    pages += [T(layout_dark(), '06 · Layout & spacing'), layout_light()]
    pages += [T(icons_dark(), '07 · Iconography'), icons_light()]
    pages += [T(graphics_dark(), '08 · Graphic language'), graphics_light()]
    pages += [T(motion_dark(), '09 · Motion identity'), motion_light()]
    return pages
