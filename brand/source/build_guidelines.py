"""Generates Cap-IT-Brand-Guidelines.md: the editable specification behind the brand book.
Tables of colours, tokens, motion and metrics are rendered from the JSON files so the Markdown, the PDF and the tokens cannot disagree."""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from contrast import ratio  # noqa: E402

BRAND = Path(__file__).resolve().parents[1]
TOK = json.loads((BRAND / 'Cap-IT-Brand-Tokens.json').read_text(encoding='utf-8'))
DARK = json.loads((BRAND / 'Cap-IT-Dark-Theme.json').read_text(encoding='utf-8'))
LIGHT = json.loads((BRAND / 'Cap-IT-Light-Theme.json').read_text(encoding='utf-8'))
ASSETS = BRAND / 'Cap-IT-Brand-Assets'

L = []
w = L.append


def table(headers, rows):
    w('| ' + ' | '.join(headers) + ' |')
    w('|' + '|'.join('---' for _ in headers) + '|')
    for r in rows:
        w('| ' + ' | '.join(str(c) for c in r) + ' |')
    w('')


def alpha(e):
    return f' α{e["alpha"]:g}' if e['alpha'] < 1 else ''


w('# Cap-IT Brand Guidelines')
w('')
w('**Edition 1.0 · 10 October 2026 · Product baseline v3.13.1 (Windows 10 / 11 x64)**')
w('')
w('> The editable specification behind `Cap-IT-Brand-Book.pdf`. It describes the brand that exists in the repository and recommends how it could grow, in two complete themes: **Midnight Precision** (dark) and **Arctic Precision** (light).')
w('')
w('## How to read this document')
w('')
table(['Label', 'Meaning'], [
    ['**EXISTING**', 'Verified in the repository: logo artwork, shipped colour/type/spacing/radius/motion tokens, screenshots, README, capture code. Colour tables are parsed from `src/CapIT.Desktop/Styles/*.axaml` at build time.'],
    ['**DERIVED**', 'Produced mechanically from existing artwork (crop, recolour, plate). Nothing is redrawn.'],
    ['**PROPOSED**', 'A recommendation with reasoning and measured contrast. Not applied to the application.'],
    ['**CONCEPT**', 'Exploratory visual (mock-ups, graphic studies). Illustrative, not a feature promise.'],
])
w('Files: `Cap-IT-Brand-Tokens.json` (shared tokens) · `Cap-IT-Dark-Theme.json` · `Cap-IT-Light-Theme.json` (each holds `implemented` = shipped values and `proposed` = Midnight/Arctic tokens with measured contrast) · `avalonia/CapIT.Brand.Proposed.axaml` (not referenced by the app) · `Cap-IT-Brand-Assets/`.')
w('')
w('The recording pipeline and its separate technical report (`docs/SMART-TRACKING-RECORDING-FIX.md`) were not touched by this work.')
w('')

# ------------------------------------------------------------------------------------------------ 1
w('## 1. Brand introduction')
w('')
w('**Product:** Cap-IT Screen Recorder — a focused, GPU-assisted Windows screen recorder for polished tutorials, demos, bug reports and social clips. Capture, smart camera movement, audio monitoring, annotation, effects, editing and export in one native app. **EXISTING** (README).')
w('')
w('**Philosophy (EXISTING):** “The recording should be easy, while the result should look intentional.”')
w('')
w('**Tagline (PROPOSED):** *Capture with intention.* Supporting lines: Capture beautifully · Make every recording count · From screen to story · Every detail, captured · Record. Refine. Share.')
w('')
table(['Element', 'Statement'], [
    ['Purpose', 'Remove the distance between doing something on screen and showing it well.'],
    ['Vision', 'Every tutorial, bug report and walkthrough made on Windows can be captured, refined and shared in one sitting — without a second tool and without a result that looks accidental.'],
    ['Mission', 'Build the most considered native recorder on Windows: precise capture, a camera that follows the action, and a review workspace that keeps the original safe.'],
    ['Positioning', 'For people who teach, demonstrate and report on Windows, Cap-IT is the native screen recorder that follows the action and hands back a finished-looking result — without leaving the app.'],
    ['Value proposition', 'Capture the right thing · Look intentional · Finish in place.'],
    ['Principles', 'Precision · Motion · Clarity · Creativity · Control.'],
])
w('**Brand story.** Cap-IT began as a way to make tutorials without juggling a recorder, a zoom plug-in, a webcam tool, an annotation overlay and a video editor. Each release pulled one more of those into the same window. It is deliberately a craft tool, not a platform: native, quick to open, quiet in use and honest about what it does.')
w('')
w('**Audience:** software developers · educators and instructors · content creators · UI/UX designers · product teams · technical documentation authors · QA engineers · freelancers · SaaS founders · tutorial creators · marketing and demo teams. No usage numbers are claimed.')
w('')

# ------------------------------------------------------------------------------------------------ 2
w('## 2. Brand strategy')
w('')
w('**Archetype:** the Craftsperson with a steady hand — exact, calm, helpful, quietly proud of a clean result.')
w('')
table(['Spectrum', 'Leans', 'Position (0 = left, 100 = right)'], [
    ['Technical ↔ Approachable', 'slightly approachable', 58], ['Expressive ↔ Restrained', 'restrained', 72], ['Playful ↔ Professional', 'professional', 68],
    ['Premium ↔ Accessible', 'balanced', 44], ['Experimental ↔ Dependable', 'dependable', 80]])
table(['Customer pain point', 'Benefit', 'Evidence in the product'], [
    ['Recordings look amateur', 'Smart Tracking follows cursor and text caret and eases back out', 'Interaction-triggered zoom, critically damped motion'],
    ['Too many tools for one tutorial', 'Capture, effects, webcam, annotations, trim, compose, export in one window', 'One native app, one library'],
    ['Unsure what is being recorded', 'Live source thumbnails and a live preview', 'DXGI + Windows Graphics Capture previews'],
    ['Edits risk the original', 'Edits are metadata beside the video', '“Original recording preserved”'],
    ['Heavy recorders slow the machine', 'GPU-assisted processing, hardware encoders with software fallback', 'NVENC · AMF · QSV · x264'],
    ['Sharing needs another conversion', 'MP4 and two-pass GIF export from the same workspace', 'Review & Export']])
w('**Differentiators:** (1) native Windows workflow · (2) GPU-assisted recording and processing · (3) Smart Tracking zoom · (4) smooth, controlled camera movement · (5) capture and editing in one app · (6) cursor interaction enhancements · (7) customisable composition and backgrounds · (8) hardware-accelerated encoding · (9) integrated audio capture and monitoring · (10) MP4 and GIF delivery.')
w('')
w('**Core promises:** you will see what is being recorded · your original is never overwritten · effects can change while you record. **Trust:** native, not a web wrapper; original preserved. **Perception goals:** considered, fast to learn, “my videos look better than I expected”.')
w('')
table(['Moment', 'Feeling', 'Example (EXISTING copy)'], [
    ['Onboarding', 'Welcoming, unhurried', 'Ready to create your next recording?'], ['Recording', 'Quiet and certain', 'Ready · Record'],
    ['Editing', 'Precise and generous', 'Original recording preserved · edits save automatically'], ['Exporting', 'Satisfying, honest', 'Export video · Export GIF 720px / 12 fps']])

# ------------------------------------------------------------------------------------------------ 3
w('## 3. Logo identity')
w('')
w('**EXISTING artwork:** `assets/Logo-CapIT.png` (2000 × 2000, transparent): five rounded arcs with detached round terminals turning around a centre, with a small “CAP IT” wordmark. `assets/Logo-Mark.png` (256 px) is the symbol alone. The repository has **no vector master**; every variant below is a crop or recolour of this artwork.')
w('')
table(['Hue', 'HEX', 'RGB'], [['Orange', '#FFAA21', '255, 170, 33'], ['Magenta', '#EA0B7F', '234, 11, 127'], ['Purple', '#971A8D', '151, 26, 141'],
                              ['Blue', '#1AABE3', '26, 171, 227'], ['Green', '#85C536', '133, 197, 54'], ['Wordmark grey', '#3A3939', '58, 57, 57']])
table(['Variant', 'Status', 'File'], [
    ['Stacked lockup (colour)', 'EXISTING', '`Cap-IT-Brand-Assets/logo/existing/capit-lockup-stacked.png`'],
    ['Symbol only', 'EXISTING', '`logo/existing/capit-symbol.png` · `capit-symbol-256-app.png` · `AppIcon.ico`'],
    ['Stacked, reversed (dark backgrounds)', 'DERIVED', '`logo/derived/capit-lockup-stacked-reversed.png` (wordmark #3A3939 → #F5F9FC)'],
    ['Monochrome / inverted', 'DERIVED', '`logo/derived/capit-symbol-mono-{black,white,cyan-midnight,cyan-arctic}.png` and `capit-lockup-mono-*.png`'],
    ['Horizontal lockup', 'PROPOSED', '`logo/proposed/capit-lockup-horizontal-on-{midnight,arctic}.png` (symbol + “Cap-IT” in Inter SemiBold; cap-height = 38 % of symbol height)'],
    ['App icon plates', 'PROPOSED', '`app-icon/capit-icon-{midnight,arctic}-{16…1024}.png`, `capit-icon-midnight.ico`'],
    ['Social avatar (round 512)', 'PROPOSED', '`app-icon/capit-avatar-{midnight,arctic}-512.png`']])
w('**Anatomy & geometry.** Symbol artwork ratio 0.989 : 1 (1375 × 1391 px). **Clear space** = X on every side, X = diameter of the largest round terminal. **Minimum size:** symbol 16 px (favicon/tray; use the app-icon plate), stacked lockup 200 px symbol height (the wordmark is only 2.4 % of the symbol height and becomes illegible below that), horizontal lockup 24 px, print 8 mm. **Optical alignment:** centre on the circle’s centre, not on the bounding box.')
w('')
w('**Backgrounds.** Colour symbol on Midnight (`#09111D`, `#142633`) and Arctic (`#FFFFFF`, `#F1F5F9`). On saturated brand fields (cyan, teal) use mono white or mono black. Photographs only where the area behind the symbol is flat and calm (≥ 4.5 : 1 average contrast).')
w('')
w('**Incorrect usage (eight):** stretch/squash · rotate · recolour the hues · add shadow or glow · outline/greyscale tricks · reduce opacity · crop the symbol · place on busy imagery. **Accessibility:** decorative next to the name “Cap-IT” (empty alt text); “Cap-IT logo” when alone.')
w('')

# ------------------------------------------------------------------------------------------------ 4
w('## 4. Dual-theme identity — Midnight & Arctic')
w('')
w('### 4.1 What ships today (EXISTING)')
w('')
w('Both a Dark and a Light theme are implemented in `Styles/Colors.axaml` and switched with `RequestedThemeVariant` (System · Light · Dark). Views reference semantic `Brush.*` keys only.')
w('')
keys = ['Brush.BackgroundPrimary', 'Brush.BackgroundSecondary', 'Brush.Surface', 'Brush.SurfaceRaised', 'Brush.BorderSubtle', 'Brush.BorderNormal', 'Brush.BorderStrong',
        'Brush.TextPrimary', 'Brush.TextSecondary', 'Brush.TextMuted', 'Brush.TextDisabled', 'Brush.TextOnAccent', 'Brush.Accent', 'Brush.AccentHover', 'Brush.AccentPressed', 'Brush.AccentText',
        'Brush.Success', 'Brush.Warning', 'Brush.Error', 'Brush.Info', 'Brush.Danger', 'Brush.Recording']
table(['Key', 'Dark', 'Light'], [[f'`{k}`', f'`{DARK["implemented"][k]}`', f'`{LIGHT["implemented"][k]}`'] for k in keys])
w('### 4.2 Accessibility audit of the shipped themes')
w('')
for name, d in (('Dark', DARK), ('Light', LIGHT)):
    w(f'**{name}**')
    w('')
    table(['Pair', 'Ratio', 'Kind', 'Result'], [[f'{a["foreground"].replace("Brush.","")} on {a["background"].replace("Brush.","")}', f'{a["ratio"]:.2f}', a['kind'], a['verdict']] for a in d['implementedAudit']])
w('Findings: the Light primary button label (#FFFFFF on #0A8FA0) is 3.85 : 1; Light Success/Warning/Recording text is 3.4–4.2 : 1; Dark Muted text is 3.9–4.2 : 1; card/input borders are 1.4–1.9 : 1 in both themes (fine for decoration, not for identifying an input — SC 1.4.11 asks 3 : 1). Disabled text is exempt. **Nothing in the application was changed.**')
w('')
w('### 4.3 Proposed Midnight and Arctic tokens (PROPOSED)')
w('')
w('Starting point: the palette in the brief. Anything that missed WCAG 2.2 AA was moved the shortest distance that passes (“adjusted from”). Text-bearing layers: Midnight `bg.app`, `bg.secondary`, `layer1`, `layer2`; Arctic `bg.app`, `bg.secondary`, `layer2`. Layer 3 holds no text.')
w('')
for name, d in (('Midnight Precision (dark)', DARK), ('Arctic Precision (light)', LIGHT)):
    w(f'#### {name}')
    w('')
    rows = []
    for n, e in d['proposed']['tokens'].items():
        con = ''
        if 'contrast' in e:
            con = ', '.join(f'{v:.2f}' for v in e['contrast'].values())
        elif 'onColor' in e:
            con = f'{e["onColor"]["contrast"]:.2f} ({e["onColor"]["token"]})'
        rows.append([f'`{n}`', f'`{e["hex"]}`{alpha(e)}', e['rgb'], e['hsl'], e['cmykApprox'], con or '—', ('was `' + e['adjustedFrom'] + '`') if 'adjustedFrom' in e else '', e['usage']])
    table(['Token', 'HEX', 'RGB', 'HSL', 'CMYK≈', 'Contrast (on text layers)', 'Adjusted', 'Usage'], rows)
w('Accessible foreground pairings: text on a layer uses `fg.primary`/`fg.secondary`/`fg.muted`; a `brand.primary` fill carries `brand.on-primary`; a violet fill carries `brand.secondary.on-solid`. Colour is never the only carrier of status (icon or label always).')
w('')
w('### 4.4 Gradients and elevation (PROPOSED)')
w('')
table(['Name', 'CSS', 'Use'], [[k, f'`{v["css"]}`', v['use']] for k, v in TOK['gradients'].items() if k != 'status'])
table(['Level', 'Midnight', 'Arctic'], [[k, f'`{TOK["elevation"]["dark"][k]}`', f'`{TOK["elevation"]["light"][k]}`'] for k in ('e0', 'e1', 'e2', 'overlay')])
w('### 4.5 Migration path from the shipped tokens')
w('')
table(['Existing key', 'Proposed token', 'Why'], [
    ['`Brush.BackgroundPrimary`', '`bg.app`', 'Navy-tinted dark; white-first light'], ['`Brush.Surface`', '`surface.layer1`', 'Same role, new values'],
    ['`Brush.Accent`', '`brand.primary`', 'Brighter dark; light passes 4.5 : 1'], ['`Brush.TextOnAccent`', '`brand.on-primary`', 'Fixes light button label 3.85 → 5.1 : 1'],
    ['`Brush.TextMuted`', '`fg.muted`', 'Fixes 3.9–4.2 : 1'], ['`Brush.BorderNormal` (inputs)', '`border.control`', 'Inputs reach 3 : 1'],
    ['`Brush.Success/Warning/Error`', '`status.*`', 'Light variants ≥ 4.5 : 1'], ['`Brush.Recording`', '`rec.active`', 'Passes on every text layer'], ['(new)', '`brand.secondary*`', 'Creative accent']])
w('Steps: (1) merge `avalonia/CapIT.Brand.Proposed.axaml` as new `Brand.*` keys; (2) alias one `Brush.*` key at a time; (3) re-run the UI snapshot harness in both themes; (4) retire old values only after sign-off.')
w('')
w('### 4.6 Theme behaviour')
w('')
for t in ['Manual switching (System · Light · Dark) and system sync exist: `App.ApplyTheme` sets `RequestedThemeVariant` (`ThemeVariant.Default` = follow Windows). **EXISTING**',
          'Preference persisted in `settings.json` and applied before the first window is shown. **EXISTING / recommended**',
          'Theme is presentation state only. A search of `CapIT.Infrastructure.Windows` for “Theme” finds nothing, so switching mid-recording only repaints brushes and never reinitialises capture, audio or encoding. **EXISTING (verified)**',
          'Proposed: 180 ms cross-fade of surfaces, none under reduced motion; never animate the live preview, meters or REC indicator. Icons use `currentColor`; use the reversed lockup on Midnight.']:
    w(f'- {t}')
w('')

# ------------------------------------------------------------------------------------------------ 5
w('## 5. Typography')
w('')
w(f'**EXISTING:** Inter (SIL OFL 1.1, via Avalonia.Fonts.Inter) → Segoe UI Variable Text → Segoe UI; mono Cascadia Mono → Consolas → Courier New. One family; hierarchy through size, weight and space.')
w('')
table(['Role', 'Size px', 'Weight', 'Other'], [[k, v['size'], v['weight'], ', '.join(f'{a} {b}' for a, b in v.items() if a not in ('size', 'weight'))] for k, v in TOK['typography']['scale'].items()])
w('**PROPOSED editorial scale** (book, website, marketing):')
w('')
table(['Style', 'Size px', 'Weight', 'Line height', 'Tracking em', 'Notes'], [[k, v['size'], v['weight'], v['lineHeight'], v.get('letterSpacing', 0), v.get('case', v.get('family', ''))] for k, v in TOK['typographyEditorial']['scale'].items()])
for t in ['Capitalisation: sentence case, except overlines (uppercase, +0.14 em) and the product name.', 'Numeric displays (timer, resolution, bitrate) use tabular figures.',
          'Application text does not scale with window width; layout reflows. Headlines wrap, never truncate; single-line values truncate with an ellipsis and tooltip.',
          'Localisation: Inter covers Latin, Greek and Cyrillic. Sinhala, Tamil, CJK and other scripts fall back to Segoe UI Variable / Segoe UI / system fallback — verify per language. Allow 30–40 % text expansion.',
          'Fonts: only Inter (OFL) and Cascadia Mono (OFL) are bundled in `brand/source/fonts`; no restricted font is redistributed.']:
    w(f'- {t}')
w('')

# ------------------------------------------------------------------------------------------------ 6
w('## 6. Layout, grid and spacing (EXISTING)')
w('')
table(['Spacing token', 'px'], [[f'`Space.{k}`', v] for k, v in TOK['spacing']['scale'].items()])
table(['Radius', 'px'], [[k, v] for k, v in TOK['radius']['scale'].items()])
table(['Control height', 'px'], [[k, v] for k, v in TOK['controlHeight']['scale'].items()])
lay = TOK['layout']
table(['Layout metric', 'px'], [['Page content max', lay['pageMax']], ['Form', lay['form']], ['Sidebar', lay['sidebar']], ['Sidebar compact', lay['sidebarCompact']], ['Inspector', lay['inspector']], ['Control column', lay['controlColumn']],
                               ['Page padding', '32 28 32 40'], ['Card padding', '20 (compact 16)'], ['Row padding', '20 14'], ['Dialog padding', 24]])
w('Windows: Main 1360 × 860 (min 1024 × 640); Review 1440 × 900 (min 960 × 600). **PROPOSED breakpoints:** compact < 1280, default 1280–1599, wide ≥ 1600; the sidebar auto-compacts in the compact band. Editorial grid: 12 columns, 96 px margins, 24 px gutters, 8 px baseline on a 1920 canvas. Layout is in device-independent px; reference renders were made at 125 % scaling.')
w('')

# ------------------------------------------------------------------------------------------------ 7/8
w('## 7. Iconography (EXISTING)')
w('')
w(f'{len(json.loads((BRAND / "source" / "icons.json").read_text(encoding="utf-8")))} icons in `Styles/Icons.axaml`, exported 1:1 to `Cap-IT-Brand-Assets/icons/*.svg`. 24 × 24 grid, 1.75 px round-cap/round-join outline strokes (Lucide-compatible), strokes scale with size; record/play/pause/stop are the only solid glyphs. Sizes 14 · 16 · 18 · 20 · 24. Colour: default secondary text, active `brand.primary`, disabled `fg.disabled`; inherit `currentColor`. Pair icon-only buttons with a tooltip and accessible name.')
w('')
w('## 8. Brand graphic language (PROPOSED)')
w('')
w('Capture brackets · focus target · viewport frame · recording indicator (dot + label + timer) · motion trail / camera path · timeline geometry · precision crosshair · layered video frames · technical grid with soft radial illumination (≤ 55 % opacity). SVGs in `Cap-IT-Brand-Assets/graphics/`. Density levels: 0 quiet (application), 1 framed (dialogs, empty states), 2 expressive (marketing). At most two devices per composition; never decoration behind text; the logo’s five hues appear only as a ≤ 6 px spectrum strip.')
w('')

# ------------------------------------------------------------------------------------------------ 9
w('## 9. Motion identity')
w('')
m = TOK['motion']
w(f'**EXISTING:** durations fast {m["duration"]["fast"]} / normal {m["duration"]["normal"]} / slow {m["duration"]["slow"]} ms; `CubicEaseOut` entrances (toast 220 ms, 10 px; overlay card 200 ms, 8 px; scrim 160 ms).')
w('')
st = m['smartTracking']
w(f'**Smart Tracking camera (EXISTING, `VideoCaptureService.cs`):** {st["model"]}. Zoom-in smooth time {st["zoomInSmoothTime"]} s, zoom-out {st["zoomOutSmoothTime"]} s, pan {st["panSmoothTime"]} s; releases after {st["idleReleaseSeconds"]} s idle ({st["clickHoldSeconds"]} s after a click in click-only mode); pan dead zone {st["panDeadZoneFraction"]*100:.0f} % of the crop; zoom levels 125–300 %; animation speed 0–50 %; instant zoom-out option. Camera motion is computed from real elapsed time.')
w('')
w('Principles: purposeful · smooth · predictable · responsive · accessible · performance-conscious. Interface motion uses transform and opacity only; camera motion lives in the rendered video and never overshoots.')
w('')
table(['Interaction (PROPOSED)', 'ms', 'Easing', 'Properties', 'Distance px', 'Scale'], [[s['name'], s['ms'], s['easing'], s['props'], s['distance'] or '—', s['scale'] if s['scale'] != 1 else '—'] for s in TOK['motionProposed']['specs']])
w(f'Easing: standard `{TOK["motionProposed"]["easing"]["standard"]}`, enter `{TOK["motionProposed"]["easing"]["enter"]}`, exit `{TOK["motionProposed"]["easing"]["exit"]}`. Reduced motion: {TOK["motionProposed"]["reducedMotion"]} Keep text sharp: move whole pixels at rest; start at 1× and scale down, never up from below 1×.')
w('')

# ------------------------------------------------------------------------------------------------ 10
w('## 10. Product UI design system (both themes)')
w('')
w('Every component uses semantic tokens only, so each of them exists identically in Midnight and Arctic; the book shows every component in both (chapter 10). Geometry is EXISTING (36 px default control, 8 px radius, 4 px spacing grid). Focus: 2 px `brand.primary` ring with a 2 px offset on every focusable control.')
w('')
comps = [
    ['Application shell', 'Title bar 44 + sidebar 232/68 + page canvas + optional inspector 340', 'title bar `bg.secondary`, sidebar `bg.app`, canvas `bg.secondary`', 'Ctrl+B toggles sidebar', 'Do not float cards on the title bar'],
    ['Sidebar / navigation item', 'icon 18 + label; height 36; radius 8; section overlines', 'default `fg.secondary`; hover `state.hover`; active `state.selected` + 3 px `brand.primary` bar + accent icon', 'Arrow keys, Enter; focus ring', 'Never colour alone for the active item'],
    ['Page heading', 'Display 28/600 + 12.5 px description', '`fg.primary`, `fg.secondary`', '—', 'No decoration behind the title'],
    ['Primary button', 'height 30/36/44, padding 0 16, radius 8, label 13 SemiBold', '`brand.primary` / hover / pressed; label `brand.on-primary`; disabled `surface.layer2` + `fg.disabled`', 'Space/Enter; focus ring', 'One primary per view'],
    ['Secondary / tertiary / danger', 'same geometry; secondary has `border.control`', '`surface.layer1`; tertiary transparent; danger `status.error`', 'same', 'Do not use danger for non-destructive actions'],
    ['Icon button', '30–36 square, icon 16–18', 'tertiary states', 'Tooltip + accessible name', 'No icon-only without a tooltip'],
    ['Text field / numeric / search', 'height 36, padding 12 × 8, radius 8, border 1', 'fill `surface.layer2`, border `border.control`, focus border `brand.primary`, error `status.error`', 'Tab; Esc clears search', 'Placeholder is not a label'],
    ['Slider', 'track 4, thumb 16', 'track `surface.layer3`, value `brand.primary`', 'Arrow keys; Home/End', 'Show the numeric value'],
    ['Switch / checkbox / radio', 'switch 40 × 22; checkbox 18', 'off `border.control`; on `brand.primary`', 'Space toggles', 'Label always visible'],
    ['Dropdown / popover / context menu', 'item padding 10 × 7, radius 8, overlay radius 10–12', '`surface.overlay`, `border.subtle`, overlay shadow; hover `state.hover`; selected `state.selected`', 'Arrow keys; Esc closes', 'No nested menus > 1 level'],
    ['Tooltip', 'padding 10 × 6, radius 8, 12 px', '`surface.overlay` + `border.control`', 'Shows on focus too', 'Not for essential information'],
    ['Dialog', 'padding 24, radius 14, max 380–520', '`surface.overlay`; scrim `surface.scrim`', 'Focus trap; Esc cancels', 'Never stack dialogs'],
    ['Notification / toast', 'padding 12 × 16, radius 12, icon 18', '`surface.overlay`, status icon colour', 'Polite live region where supported', 'Do not auto-dismiss errors'],
    ['Status indicator / badge', 'height 24, pill, dot or icon + label', '`rec.*`, `status.*`', '—', 'Never colour alone'],
    ['Progress bar', 'height 6, radius 3', 'track `surface.layer3`, fill `rec.exporting`', '—', 'Determinate when possible'],
    ['Audio meter', '20 segments, 7 × 14, gap 3', 'green `status.success`, amber `status.warning`, red `status.error`; empty `surface.layer3`', '—', 'Add a text legend (existing “Reading the meters”)'],
    ['Source selection card', 'thumbnail 84 + title + meta, radius 12', 'selected `brand.primary` 1 px + ring', 'Arrow/Enter', 'Always show the live thumbnail'],
    ['Device preview / live preview', '16:9 stage on `surface.layer2`', 'stage never themed over the video', '—', 'Do not overlay controls on the preview'],
    ['Recording controller', 'padding 6, radius 16, grip + timer · transport · toggles · show app', '`surface.layer2`, `border.control`, overlay shadow, `rec.active` dot', 'Global shortcuts', 'Never appears in the recording'],
    ['Media thumbnail', 'grid/list, radius 12, duration chip', '`surface.layer1`', 'Enter opens', 'No personal data in demo captures'],
    ['Timeline / trim', 'height 64, handles 4 px, playhead 2 px', 'selection `state.selected` + `brand.primary` handles', 'I / O set in/out; Space play', 'Keep the original untouched'],
    ['Inspector panel / tabs', 'width 340, icon tabs 36', 'tab selected `state.selected`', 'Ctrl+E export', 'One scroll area'],
    ['Color picker / swatches', 'swatch 24, radius 6', '`border.control` ring on selected', 'Arrow keys', 'Show the hex'],
    ['Empty state', 'icon 48 + title + description + action', '`surface.layer1`, `fg.secondary`', '—', 'Always offer the next step'],
    ['Error state', 'icon + title + cause + retry', '`status.error` border', '—', 'No raw error codes'],
    ['Loading / skeleton', 'bars 12 high, radius 6', '`surface.layer2` → `surface.layer3` shimmer (static if reduced motion)', '—', 'No spinners over the preview'],
]
table(['Component', 'Anatomy / size / spacing', 'Colour tokens & states', 'Keyboard / focus', 'Incorrect usage'], comps)
w('Interaction and motion for every component follow section 9 (hover 120 ms, press 80 ms, dialog 200 ms, toast 220 ms). Typography follows section 5 (13 px body, 12.5 px description, 10.5 px overline).')
w('')

# ------------------------------------------------------------------------------------------------ 11/12
w('## 11. Feature identity (EXISTING names/icons · PROPOSED colours)')
w('')
table(['Feature', 'Icon', 'Colour (Midnight / Arctic)', 'Benefit', 'Microcopy'], [[f['name'], f'`{f["icon"]}`', f'`{f["colorDark"]}` / `{f["colorLight"]}`', f['benefit'], f'“{f["microcopy"]}”'] for f in TOK['features']])
w('Colours come from the logo hues plus brand cyan and violet; they tint icon chips (16 % / 12 % alpha) and small markers only, never labels, and stay subordinate to the master brand. Screenshots: `Cap-IT-Brand-Assets/screenshots/{dark,light}/`.')
w('')
w('## 12. Smart Tracking — signature identity')
w('')
w('Interaction → Focus → Smooth reframing → Clear communication. The camera follows the cursor, or the text caret while typing; modes: cursor & typing, or clicks only; levels 125–300 %; instant zoom-out; speed 0–50 %; keystroke overlay. Microcopy (EXISTING): “A camera that follows the action: it glides toward your cursor and caret, then eases back out.” Violet is its accent colour (PROPOSED).')
w('')
w('**What is and is not verified.** The recording-continuity work is documented in `docs/SMART-TRACKING-RECORDING-FIX.md` (one machine, AMD integrated GPU, 1080p). Still open there: a literal “cut section” was not reproduced; the audio tail can end up to ~0.8 s short; text sharpness while zoomed is not measured; 1440p, 4K, NVENC and QSV are untested. This brand work makes no performance claim beyond that report.')
w('')

# ------------------------------------------------------------------------------------------------ 13..20
w('## 13. Product screens')
w('')
shots = sorted(p.stem for p in (ASSETS / 'screenshots' / 'dark').glob('*.png'))
table(['Screen', 'Midnight (real render)', 'Arctic (real render)'], [[s, f'`screenshots/dark/{s}.png`', f'`screenshots/light/{s}.png`'] for s in shots])
w('Rendered by the application’s own snapshot harness (Debug build, `CAPIT_UI_SNAPSHOT_DIR`, both themes, 1500 × 840 pages and 1280 × 720 editor) with a clean demo configuration (empty library, default settings). The harness was run from a scratch copy of the sources so that no production file changed.')
w('')
w('## 14. Marketing identity')
w('')
table(['Asset', 'Dimensions', 'Template (both themes)'], [
    ['GitHub social preview', '1280 × 640', 'github-social-preview'], ['YouTube thumbnail', '1280 × 720', 'youtube-thumbnail'], ['YouTube banner', '2560 × 1440 (safe 1546 × 423)', 'youtube-banner'],
    ['LinkedIn', '1200 × 627', 'linkedin-post'], ['X / Twitter', '1600 × 900', 'x-post'], ['Instagram portrait', '1080 × 1350', 'instagram-post'], ['Product Hunt gallery', '1270 × 760', 'product-hunt-gallery'],
    ['Email header', '1200 × 400 (@2×)', 'email-header'], ['Release announcement', '1920 × 1080', 'release-announcement'], ['Tutorial title card', '1920 × 1080', 'tutorial-title-card'],
    ['Video outro', '1920 × 1080', 'video-outro'], ['Desktop wallpaper', '3840 × 2160', 'desktop-wallpaper'], ['Download banner', '1600 × 400', 'download-banner']])
w('Platform dimensions are common guidance at the time of writing; confirm before publishing. Templates are CONCEPT compositions built from the proposed tokens, the existing logo and real screenshots.')
w('')
w('## 15. Voice and messaging')
w('')
w('Clear · confident · helpful · precise · modern · human · professional. Avoid hype, unsupported claims (“fastest”, “zero lag”), buzzwords, robotic phrasing and unexplained jargon. Verbs first, sentence case, one idea per line, the real feature name, a next step.')
w('')
table(['Where', 'Preferred', 'Avoid'], [
    ['Homepage headline', 'Capture with intention.', 'The most powerful screen recorder ever built.'], ['Short description', 'A focused, GPU-assisted screen recorder for Windows.', 'Revolutionary AI-powered capture solution.'],
    ['Tooltip', 'Freeze the screen image — audio and timer keep recording.', 'Click to freeze!'], ['Settings help', 'Hardware encoders need a compatible GPU. If a hardware export fails, choose Software.', 'Hardware encoding may not work for some users.'],
    ['Error', 'Couldn’t open this video. Check that the file still exists, then try again.', 'Error 0x80004005. Operation failed.'], ['Export complete', 'Export complete. Open folder', 'Success!!!'],
    ['Update', 'Cap-IT 3.13.1 is available. Update now installs it and reopens Cap-IT.', 'A new version is ready! Don’t miss out!'], ['Release note', 'Smoother Smart Tracking motion and sharper live preview scaling.', 'Various improvements and bug fixes.'],
    ['Support reply', 'Thanks — could you share your Windows version, the capture target and a short description of what happened?', 'Please provide more information.']])
w('## 16. Brand applications')
w('')
w('Desktop app window (both themes) · Windows installer (Inno Setup modern wizard; the script uses only `SetupIconFile` today, side artwork is a concept) · Start Menu and taskbar identity · GitHub repository graphics · website hero · documentation portal · screenshot gallery · social and launch assets · feature spotlights · tutorial title card · video outro · export watermark (suggested default: bottom-right, 4 % of frame height, 55 % opacity plate). All are CONCEPT compositions in the book and PNG templates in the asset pack.')
w('')
w('## 17. Technical product overview (EXISTING)')
w('')
w('C# / .NET 8 · Avalonia UI 11 · CommunityToolkit.Mvvm · Microsoft.Extensions.DependencyInjection · DXGI Desktop Duplication · Windows Graphics Capture · Vortice.Direct3D11 / DXGI · NAudio (WASAPI) · FFmpeg · Media Foundation · GDI / GDI+ · Win32 input hooks.')
w('')
w('Flow: Avalonia shell → view models → `RecordingManager` → `VideoCaptureService` (DXGI for displays, WGC for windows; GPU compose to NV12 when supported, CPU kernels otherwise) + `AudioCaptureService` → `FFmpegEncoderService` (named pipes) → fragmented MP4 → faststart MP4 / MKV → Review & Export (Media Foundation preview, composition renderer) → MP4 / GIF. No future architecture is proposed here.')
w('')
w('## 18. Platform, version and distribution (EXISTING)')
w('')
for t in ['Windows 10 version 2004 (build 19041) or later, 64-bit; Windows 11 recommended.', 'Self-contained installer: no separate .NET runtime, Windows App SDK runtime or manual FFmpeg set-up; Start Menu entry, optional desktop shortcut; uninstall keeps recordings.',
          'Published through GitHub Releases; the app checks on startup and while open; **Update now** downloads the installer, waits for the app to close, installs and relaunches.',
          'Version comes from `Directory.Build.props` (3.13.1 at the time of writing) — never hard-code “latest” in artwork. Version badge: outlined uppercase pill, 13 px, always a text label.',
          'Download button: 44 px, 8 px radius, 14 px SemiBold label; version and OS in muted text beneath.']:
    w(f'- {t}')
w('')
w('## 19. Accessibility and consistency')
w('')
table(['Area', 'Requirement', 'Status'], [
    ['Text contrast', 'Normal ≥ 4.5 : 1, large ≥ 3 : 1', 'Verified for proposed tokens (build gate)'], ['Control boundaries', '≥ 3 : 1 (SC 1.4.11)', '`border.control` passes; shipped borders do not'],
    ['Keyboard focus', '2 px ring, 2 px offset', 'Specified; focus visuals not audited in the app'], ['Colour independence', 'Icon or label with every status', 'Specified'],
    ['Reduced motion', 'Remove travel/scale, keep feedback', 'Specified; app support not verified'], ['Text scaling', 'No fixed-height text containers; allow 30–40 % growth', 'Specified; not tested'],
    ['Screen readers', 'Name icon-only buttons (AutomationProperties.Name) where supported', 'Not verified'], ['High-DPI', 'Device-independent layout; strokes scale', 'Renders verified at 125 %'],
    ['Theme contrast', 'Both themes pass independently', 'Proposed: yes · Shipped: see audit'], ['Touch targets', 'Desktop app 30–44 px; use Large (44) for touch', 'Informational']])
w('Quality checklist: run the snapshot harness in both themes; review focus with the keyboard only; re-run `build_tokens.py` after any colour change; check logo proportions and clear space; confirm every screenshot is a real render or labelled CONCEPT.')
w('')
w('## 20. Brand governance')
w('')
for t in ['**Owner:** the repository maintainer; contributors propose, the owner approves.', '**Versioning:** semantic — patch = fixes, minor = additions, major = identity change. This edition is 1.0 against product v3.13.1.',
          '**Approved logos:** existing stacked lockup and symbol; derived reversed and monochrome; proposed horizontal lockup and app-icon plates (pending approval). Never edit in place; new variant = new file name `capit-<thing>-<variant>-<size>`.',
          '**Tokens:** shipped values live in `Styles/*.axaml`; proposed values in the two theme JSON files, regenerated by `brand/source/build_tokens.py`, which fails if contrast regresses.',
          '**Screenshots:** regenerate with the snapshot harness (both themes, demo configuration) whenever a page changes; archive the previous version folder.', '**Release artwork:** use the templates; headline = product + version; one real screenshot.',
          '**Review process:** propose → measure → review (brand work separate from recording-pipeline commits) → publish with a version bump.',
          '**Do:** semantic tokens; both themes; label proposed/concept; keep “original recording preserved” in export flows. **Don’t:** redraw or recolour the logo; invert dark to get light; claim unverified performance or compatibility.']:
    w(f'- {t}')
w('')
w('## Appendix A — Package inventory')
w('')
def count(sub, pat='*'):
    return len(list((ASSETS / sub).rglob(pat)))
table(['Path', 'Contents'], [
    ['`Cap-IT-Brand-Book.pdf`', 'Editorial brand book, 16 : 9'], ['`Cap-IT-Brand-Guidelines.md`', 'This document'], ['`Cap-IT-Brand-Tokens.json`', 'Shared tokens (spacing, type, motion, gradients, features, logo hues)'],
    ['`Cap-IT-Dark-Theme.json` / `Cap-IT-Light-Theme.json`', 'Shipped values + proposed Midnight / Arctic tokens with measured contrast'], ['`avalonia/CapIT.Brand.Proposed.axaml`', 'Proposed brushes for both themes (not referenced by the app)'],
    ['`Cap-IT-Brand-Assets/logo`', f'{count("logo", "*.png")} PNG + AppIcon.ico (existing / derived / proposed)'], ['`Cap-IT-Brand-Assets/app-icon`', f'{count("app-icon", "*.png")} PNG + .ico'],
    ['`Cap-IT-Brand-Assets/icons`', f'{count("icons", "*.svg")} SVG exported from the app’s icon set'], ['`Cap-IT-Brand-Assets/graphics`', f'{count("graphics", "*.svg")} SVG graphic-language devices'],
    ['`Cap-IT-Brand-Assets/screenshots`', f'{count("screenshots", "*.png")} real renders (Midnight and Arctic)'], ['`Cap-IT-Brand-Assets/templates`', f'{count("templates", "*.png")} PNG marketing templates (both themes)'],
    ['`source/`', 'HTML book, Python builders, fonts (OFL), `README.md`']])
w('## Appendix B — Rebuild')
w('')
w('```powershell')
w('python brand/source/build_assets.py      # logo variants, app icons, icon SVGs')
w('python brand/source/build_tokens.py      # tokens + WCAG gate (fails on regression)')
w('python brand/source/build_graphics.py    # graphic-language SVGs')
w('python brand/source/build_templates.py   # marketing templates (needs Microsoft Edge)')
w('python brand/source/build_book.py        # Cap-IT-Brand-Book.pdf')
w('python brand/source/build_guidelines.py  # this file')
w('```')
w('')
w('## Appendix C — Limitations (not hidden)')
w('')
for t in ['No vector logo master exists in the repository; variants are raster-derived. Request the original vector files before print production.', 'The stacked lockup’s wordmark is only 2.4 % of the symbol height; the horizontal lockup is proposed because of it.',
          'The Midnight / Arctic palettes are recommendations; the application’s colours are unchanged.', 'Not verified in the running application: reduced motion, screen-reader support, text scaling, light-theme focus visuals, non-Latin script rendering.',
          'Platform dimensions in section 14 are guidance at the time of writing.', 'The Smart Tracking recording work is separate (see `docs/SMART-TRACKING-RECORDING-FIX.md`): audio tail, zoomed text sharpness and broader continuity tests remain a separate validation pass.']:
    w(f'- {t}')
w('')

(BRAND / 'Cap-IT-Brand-Guidelines.md').write_text('\n'.join(L), encoding='utf-8')
print('guidelines written:', len(L), 'lines')
