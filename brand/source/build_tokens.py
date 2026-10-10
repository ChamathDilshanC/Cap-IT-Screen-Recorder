"""Builds the machine-readable Cap-IT brand tokens.

EXISTING values are parsed straight out of src/CapIT.Desktop/Styles/*.axaml so they cannot drift from the
app. PROPOSED values are defined here, then gated: every text/UI pair a token claims is measured with
contrast.py and the build fails if one does not meet WCAG 2.2 AA (4.5:1 text, 3:1 non-text).
Run from anywhere:  python brand/source/build_tokens.py
"""
import colorsys
import json
import re
import sys
from datetime import date
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from contrast import blend, cmyk_str, grade, hsl_str, parse, ratio, rgb_str  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'brand'
STYLES = ROOT / 'src' / 'CapIT.Desktop' / 'Styles'
TODAY = '2026-10-10'


# ------------------------------------------------------------------ EXISTING (parsed from the app)
def parse_existing():
    s = (STYLES / 'Colors.axaml').read_text(encoding='utf-8')
    out = {}
    for theme in ('Dark', 'Light'):
        start = s.index(f'x:Key="{theme}"')
        end = s.index('x:Key="Light"') if theme == 'Dark' else len(s)
        block = s[start:end]
        tokens = {}
        for m in re.finditer(r'x:Key="((?:Brush|Color)\.[A-Za-z.]+)"[^>]*?(?:Color="|>)(#[0-9A-Fa-f]{6,8})', block):
            tokens[m.group(1)] = m.group(2).upper()
        out[theme.lower()] = tokens
    return out


def parse_tokens_axaml():
    s = (STYLES / 'Tokens.axaml').read_text(encoding='utf-8')
    d = {}
    for m in re.finditer(r'<x:Double x:Key="([^"]+)">([^<]+)</x:Double>', s):
        d[m.group(1)] = float(m.group(2))
    for m in re.finditer(r'<CornerRadius x:Key="([^"]+)">([^<]+)</CornerRadius>', s):
        d[m.group(1)] = float(m.group(2))
    return d


def _hex6(h):
    return '#%02X%02X%02X' % tuple(parse(h)[:3])


# ------------------------------------------------------------------ helpers for adjustment
def _tohex(r, g, b):
    return '#%02X%02X%02X' % (round(r * 255), round(g * 255), round(b * 255))


def adjust(hex_, bgs, target, direction):
    """Move lightness in `direction` (+1 lighter, -1 darker) until every bg reaches `target`."""
    r, g, b, _ = parse(hex_)
    h, l, s = colorsys.rgb_to_hls(r / 255, g / 255, b / 255)
    for i in range(400):
        l2 = l + direction * i * 0.0025
        if not 0 <= l2 <= 1:
            break
        c = _tohex(*colorsys.hls_to_rgb(h, l2, s))
        if all(ratio(c, bg) >= target for bg in bgs):
            return c
    raise ValueError(f'cannot reach {target} for {hex_}')


# ------------------------------------------------------------------ PROPOSED palettes
# Starting points are the user's proposed table; anything that failed measurement is adjusted and the
# original is recorded in `adjustedFrom` so the book can show what changed and why.
PROPOSED = {
    'dark': {
        'name': 'Midnight Precision',
        'bg': ['#09111D', '#101C2B', '#142633', '#1B3042'],        # text-bearing backgrounds
        'base': {
            'bg.app': '#09111D', 'bg.secondary': '#101C2B',
            'surface.layer1': '#142633', 'surface.layer2': '#1B3042', 'surface.layer3': '#243D52',
            'fg.primary': '#F5F9FC', 'fg.secondary': '#93A8B9', 'fg.muted': '#6F8799', 'fg.disabled': '#4F6578',
            'brand.primary': '#18DCE8', 'brand.primary.hover': '#4BE6EE', 'brand.primary.pressed': '#12B7C2',
            'brand.on-primary': '#04141A',
            'brand.secondary': '#7C5CFF', 'brand.secondary.text': '#7C5CFF', 'brand.secondary.solid': '#7C5CFF',
            'brand.secondary.on-solid': '#FFFFFF',
            'border.subtle': '#294050', 'divider': '#203443', 'border.control': '#41596B',
            'status.info': '#6AA8FF', 'status.success': '#10B981', 'status.warning': '#F59E0B', 'status.error': '#F87171',
            'rec.active': '#F2555A', 'rec.paused': '#F59E0B', 'rec.exporting': '#7C5CFF', 'rec.export-complete': '#10B981',
        },
    },
    'light': {
        'name': 'Arctic Precision',
        'bg': ['#FFFFFF', '#F6F8FB', '#F1F5F9'],
        'base': {
            'bg.app': '#FFFFFF', 'bg.secondary': '#F6F8FB',
            'surface.layer1': '#FFFFFF', 'surface.layer2': '#F1F5F9', 'surface.layer3': '#E8EEF4',
            'fg.primary': '#111827', 'fg.secondary': '#475569', 'fg.muted': '#7B8798', 'fg.disabled': '#A9B4C2',
            'brand.primary': '#087F8C', 'brand.primary.hover': '#066A76', 'brand.primary.pressed': '#055C66',
            'brand.on-primary': '#FFFFFF',
            'brand.secondary': '#6941C6', 'brand.secondary.text': '#6941C6', 'brand.secondary.solid': '#6941C6',
            'brand.secondary.on-solid': '#FFFFFF',
            'border.subtle': '#E2E8F0', 'divider': '#E9EEF3', 'border.control': '#B0BAC8',
            'status.info': '#2F6FDB', 'status.success': '#047857', 'status.warning': '#B45309', 'status.error': '#DC2626',
            'rec.active': '#E5383E', 'rec.paused': '#B45309', 'rec.exporting': '#6941C6', 'rec.export-complete': '#047857',
        },
    },
}

USAGE = {
    'bg.app': 'Window background, sidebar and title bar; marketing and document canvas.',
    'bg.secondary': 'Page canvas behind cards inside the application.',
    'surface.layer1': 'Cards, panels and the default container surface.',
    'surface.layer2': 'Raised or inset wells: inputs, segmented tracks, hovered rows.',
    'surface.layer3': 'Tracks, meters and skeletons. Non-text only: no text is placed on this layer.',
    'fg.primary': 'Headlines, body copy, values and any text that carries meaning.',
    'fg.secondary': 'Descriptions, labels, secondary values.',
    'fg.muted': 'Captions, placeholders, timestamps and helper text.',
    'fg.disabled': 'Disabled text and icons. Exempt from contrast by WCAG; never use for information that is not available elsewhere.',
    'brand.primary': 'The Cap-IT accent: primary button fill, switch and slider fill, active navigation, links, focus ring.',
    'brand.primary.hover': 'Hover state of brand.primary fills.',
    'brand.primary.pressed': 'Pressed state of brand.primary fills.',
    'brand.on-primary': 'Label and icon colour placed on a brand.primary fill.',
    'brand.secondary': 'Creative accent for graphics, gradients, illustrations and non-text indicators.',
    'brand.secondary.text': 'Violet as text or small icon on any text-bearing surface.',
    'brand.secondary.solid': 'Violet as a solid fill that carries a label.',
    'brand.secondary.on-solid': 'Label colour on brand.secondary.solid.',
    'border.subtle': 'Decorative card and panel outlines. Not relied on to identify a control.',
    'divider': 'Hairlines between rows and sections.',
    'border.control': 'Outline of inputs, dropdowns, checkboxes and sliders: meets the 3:1 non-text rule (SC 1.4.11).',
    'status.info': 'Informational messages and neutral highlights.',
    'status.success': 'Success feedback and completed states.',
    'status.warning': 'Warnings and paused or at-risk states.',
    'status.error': 'Errors and destructive confirmations.',
    'rec.active': 'The live recording indicator, timer dot and record state.',
    'rec.paused': 'Paused recording indicator.',
    'rec.exporting': 'Export in progress.',
    'rec.export-complete': 'Export finished successfully.',
}

TOKEN_FAMILY = {
    'bg': 'background', 'surface': 'surface', 'fg': 'foreground', 'brand': 'brand', 'border': 'border',
    'divider': 'border', 'status': 'status', 'rec': 'recording',
}


def alpha_tokens(theme, brand):
    """Translucent state tokens: {name: (hex, alpha, usage)}. `brand` is the resolved brand.primary of the theme."""
    if theme == 'dark':
        return {
            'state.hover': ('#FFFFFF', 0.06, 'Hover wash over any surface.'),
            'state.pressed': ('#FFFFFF', 0.10, 'Pressed wash over any surface.'),
            'state.selected': (brand, 0.16, 'Selected row, tab or navigation item background.'),
            'state.focus-ring': (brand, 1.0, '2px focus ring with a 2px offset on every focusable control.'),
            'state.selection-text': (brand, 0.30, 'Selected text highlight.'),
            'state.disabled-fill': ('#FFFFFF', 0.04, 'Disabled control fill.'),
            'surface.overlay': ('#1B3042', 1.0, 'Popovers, menus, tooltips and dialogs. Pair with the overlay shadow.'),
            'surface.scrim': ('#050B14', 0.72, 'Dim layer behind modal dialogs.'),
        }
    return {
        'state.hover': ('#09111D', 0.05, 'Hover wash over any surface.'),
        'state.pressed': ('#09111D', 0.09, 'Pressed wash over any surface.'),
        'state.selected': (brand, 0.10, 'Selected row, tab or navigation item background.'),
        'state.focus-ring': (brand, 1.0, '2px focus ring with a 2px offset on every focusable control.'),
        'state.selection-text': (brand, 0.22, 'Selected text highlight.'),
        'state.disabled-fill': ('#09111D', 0.04, 'Disabled control fill.'),
        'surface.overlay': ('#FFFFFF', 1.0, 'Popovers, menus, tooltips and dialogs. Pair with the overlay shadow.'),
        'surface.scrim': ('#09111D', 0.40, 'Dim layer behind modal dialogs.'),
    }


def resolve_theme(key):
    spec = PROPOSED[key]
    base = dict(spec['base'])
    bgs = spec['bg']
    direction = +1 if key == 'dark' else -1
    adjusted = {}

    def fix(name, target, bg_list=bgs, dirn=direction):
        before = base[name]
        if all(ratio(before, bg) >= target for bg in bg_list):
            return
        after = adjust(before, bg_list, target, dirn)
        base[name] = after
        adjusted[name] = before

    # Text-bearing foregrounds: 4.5:1 on every text-bearing layer.
    for n in ('fg.primary', 'fg.secondary', 'fg.muted', 'brand.secondary.text', 'status.info', 'status.success',
              'status.warning', 'status.error', 'rec.active', 'rec.paused', 'rec.exporting', 'rec.export-complete'):
        # secondary text starts from the brand violet but needs a lighter/darker variant for text use
        if n == 'brand.secondary.text' and key == 'dark':
            base[n] = base['brand.secondary']
        fix(n, 4.5)
    if key == 'dark':
        # a violet that carries a white label must itself reach 4.5 against white
        before = base['brand.secondary.solid']
        if ratio('#FFFFFF', before) < 4.5:
            base['brand.secondary.solid'] = adjust(before, ['#FFFFFF'], 4.5, -1)
            adjusted['brand.secondary.solid'] = before
    # Brand primary: as a text/link colour it needs 4.5 on every text-bearing layer; its fill must carry its label.
    fix('brand.primary', 4.5)
    if key == 'light':
        # darken hover/pressed in lockstep so the white label keeps passing
        pass
    # Control outlines: 3:1 against the layers they sit on (SC 1.4.11).
    ui_bgs = [spec['bg'][0], spec['bg'][2]] if key == 'dark' else [bgs[0], bgs[1]]
    fix('border.control', 3.0, ui_bgs, direction)
    return base, adjusted


# ------------------------------------------------------------------ feature identity (PROPOSED)
LOGO = {  # EXISTING: sampled from assets/Logo-CapIT.png
    'green': '#85C536', 'magenta': '#EA0B7F', 'purple': '#971A8D', 'orange': '#FFAA21', 'blue': '#1AABE3',
    'wordmark': '#3A3939',
}

FEATURES = [
    # key, label, icon, hue source dark/light starting colours, benefit, microcopy
    ('capture', 'Capture', 'Icon.Monitor', '#18DCE8', '#087F8C',
     'Pick exactly what gets recorded — a display or a single window.', 'Choose source'),
    ('smart-tracking', 'Smart Tracking', 'Icon.Focus', '#7C5CFF', '#6941C6',
     'A camera that follows your cursor and caret, then eases back out.', 'A camera that follows the action'),
    ('audio', 'Audio', 'Icon.Volume', '#85C536', '#4E7F0C',
     'See system and microphone levels before you press record.', 'System audio · Microphone'),
    ('webcam', 'Webcam', 'Icon.Video', '#EA0B7F', '#C4046A',
     'Put yourself in the frame with a picture-in-picture template.', 'Picture-in-picture'),
    ('effects', 'Effects', 'Icon.Sparkles', '#FFAA21', '#A15F00',
     'Spotlight, click ripples and click sounds that make actions readable.', 'Make every click visible'),
    ('annotations', 'Annotations', 'Icon.Pen', '#6AA8FF', '#2567D6',
     'Draw over your desktop while you record.', 'Draw while you record'),
    ('review-export', 'Review & Export', 'Icon.Film', '#18DCE8', '#087F8C',
     'Trim, compose and deliver an MP4 or GIF without leaving the app.', 'Original recording preserved'),
    ('recordings', 'Recordings', 'Icon.Film', '#93A8B9', '#475569',
     'Find any recording by name, date or thumbnail.', 'Search recordings'),
    ('settings', 'Settings', 'Icon.Settings', '#93A8B9', '#475569',
     'Preferences, theme and update behaviour.', 'Settings'),
]


def build():
    existing = parse_existing()
    tok = parse_tokens_axaml()
    themes = {}
    report = []

    for key in ('dark', 'light'):
        base, adjusted = resolve_theme(key)
        spec = PROPOSED[key]
        bgs = spec['bg']
        tokens = {}

        def entry(name, hex_, alpha=1.0, usage=''):
            e = {
                'token': name, 'hex': hex_, 'alpha': alpha, 'rgb': rgb_str(hex_), 'hsl': hsl_str(hex_),
                'cmykApprox': cmyk_str(hex_), 'usage': usage,
            }
            tokens[name] = e
            return e

        for name, hex_ in base.items():
            e = entry(name, hex_, 1.0, USAGE[name])
            if name in adjusted:
                e['adjustedFrom'] = adjusted[name]
        for name, (hex_, a, usage) in alpha_tokens(key, base['brand.primary']).items():
            entry(name, hex_, a, usage)

        # Measured contrast for every token that is used as text or as a non-text indicator.
        text_tokens = ['fg.primary', 'fg.secondary', 'fg.muted', 'brand.primary', 'brand.secondary.text', 'status.info',
                       'status.success', 'status.warning', 'status.error', 'rec.active', 'rec.paused', 'rec.exporting',
                       'rec.export-complete']
        for n in text_tokens:
            tokens[n]['contrast'] = {bg: round(ratio(base[n], bg), 2) for bg in bgs}
            tokens[n]['wcag'] = grade(min(tokens[n]['contrast'].values()))
            tokens[n]['accessibleOn'] = list(bgs)
        for fill, on in (('brand.primary', 'brand.on-primary'), ('brand.secondary.solid', 'brand.secondary.on-solid')):
            r = ratio(base[on], base[fill])
            tokens[fill]['onColor'] = {'token': on, 'hex': base[on], 'contrast': round(r, 2), 'wcag': grade(r)}
            if r < 4.5:
                raise SystemExit(f'{key}: {on} on {fill} = {r:.2f} < 4.5')
        for n, bg_list in (('border.control', [bgs[0], bgs[-2] if key == 'dark' else bgs[1]]),):
            tokens[n]['contrast'] = {bg: round(ratio(base[n], bg), 2) for bg in bg_list}
            tokens[n]['wcag'] = 'AA (non-text 3:1)' if all(v >= 3 for v in tokens[n]['contrast'].values()) else 'FAIL'
        for n in ('brand.primary',):
            ui = {bg: round(ratio(base[n], bg), 2) for bg in bgs}
            tokens[n]['nonTextContrast'] = ui
        tokens['state.focus-ring']['contrast'] = {bg: round(ratio(base['brand.primary'], bg), 2) for bg in bgs}

        # hard gate
        for n in text_tokens:
            if min(tokens[n]['contrast'].values()) < 4.5:
                raise SystemExit(f'{key}: {n} fails 4.5:1 -> {tokens[n]["contrast"]}')
        if 'FAIL' in tokens['border.control']['wcag']:
            raise SystemExit(f'{key}: border.control fails 3:1')

        impl = existing[key]
        implemented = {}
        canon_bg = impl['Brush.Surface']
        for k, v in impl.items():
            implemented[k] = v
        # audit of the implemented theme: the pairs a user actually reads
        pairs = [
            ('Brush.TextPrimary', 'Brush.BackgroundPrimary'), ('Brush.TextSecondary', 'Brush.BackgroundPrimary'),
            ('Brush.TextSecondary', 'Brush.Surface'), ('Brush.TextMuted', 'Brush.BackgroundPrimary'),
            ('Brush.TextMuted', 'Brush.Surface'), ('Brush.AccentText', 'Brush.Surface'),
            ('Brush.Accent', 'Brush.BackgroundPrimary'), ('Brush.Accent', 'Brush.Surface'),
            ('Brush.TextOnAccent', 'Brush.Accent'), ('Brush.Success', 'Brush.Surface'), ('Brush.Warning', 'Brush.Surface'),
            ('Brush.Error', 'Brush.Surface'), ('Brush.Info', 'Brush.Surface'), ('Brush.Recording', 'Brush.Surface'),
            ('Brush.BorderNormal', 'Brush.Surface'), ('Brush.BorderStrong', 'Brush.Surface'),
            ('Brush.TextDisabled', 'Brush.Surface'),
        ]
        audit = []
        for fg, bg in pairs:
            r = ratio(impl[fg], impl[bg])
            non_text = fg.startswith('Brush.Border') or fg == 'Brush.Accent'
            audit.append({
                'foreground': fg, 'background': bg, 'fgHex': _hex6(impl[fg]), 'bgHex': _hex6(impl[bg]),
                'ratio': round(r, 2),
                'kind': 'non-text' if non_text else ('disabled (exempt)' if fg == 'Brush.TextDisabled' else 'text'),
                'verdict': ('AA' if r >= 3 else 'FAIL') if non_text else ('exempt' if fg == 'Brush.TextDisabled' else grade(r)),
            })
        themes[key] = {
            '$schema': 'cap-it-theme/1',
            'name': spec['name'],
            'theme': key,
            'generated': TODAY,
            'status': {
                'implemented': 'EXISTING — parsed from src/CapIT.Desktop/Styles/Colors.axaml (Dictionary "%s"). Do not edit by hand.' % key.capitalize(),
                'proposed': 'PROPOSED — design recommendation, verified against WCAG 2.2 AA. Not applied to the application.',
            },
            'implemented': implemented,
            'implementedAudit': audit,
            'proposed': {
                'textBackgrounds': bgs,
                'tokens': tokens,
            },
        }
        report.append((key, adjusted))

    # ---------------- shared tokens
    feature_tokens = []
    for fkey, label, icon, dk, lt, benefit, micro in FEATURES:
        dk_text = dk if all(ratio(dk, b) >= 3 for b in PROPOSED['dark']['bg'][:3]) else adjust(dk, PROPOSED['dark']['bg'][:3], 3, +1)
        lt_text = lt if all(ratio(lt, b) >= 3 for b in PROPOSED['light']['bg'][:2]) else adjust(lt, PROPOSED['light']['bg'][:2], 3, -1)
        feature_tokens.append({
            'key': fkey, 'name': label, 'icon': icon, 'benefit': benefit, 'microcopy': micro,
            'colorDark': dk_text, 'colorLight': lt_text,
            'chipDark': {'hex': dk_text, 'alpha': 0.16}, 'chipLight': {'hex': lt_text, 'alpha': 0.12},
            'status': 'PROPOSED',
        })

    shared = {
        '$schema': 'cap-it-brand/1',
        'name': 'Cap-IT Brand Tokens', 'generated': TODAY,
        'legend': {'EXISTING': 'verified in the repository', 'PROPOSED': 'new recommendation, not applied',
                   'CONCEPT': 'exploratory, not part of the system'},
        'sources': ['src/CapIT.Desktop/Styles/Tokens.axaml', 'src/CapIT.Desktop/Styles/Colors.axaml',
                    'src/CapIT.Desktop/Styles/Typography.axaml', 'src/CapIT.Desktop/Styles/Motion.axaml',
                    'src/CapIT.Desktop/Styles/Icons.axaml', 'assets/Logo-CapIT.png', 'assets/Logo-Mark.png',
                    'src/CapIT.Infrastructure.Windows/Services/Capture/VideoCaptureService.cs'],
        'logo': {
            'status': 'EXISTING',
            'source': 'assets/Logo-CapIT.png (2000x2000 RGBA, transparent background)',
            'colors': LOGO,
            'note': 'Raster only: the repository has no vector master. All variants in Cap-IT-Brand-Assets are derived by recolouring or cropping this artwork — nothing is redrawn.',
        },
        'spacing': {'status': 'EXISTING', 'unit': 'px', 'base': 4,
                    'scale': {k.split('.')[1]: tok[k] for k in tok if k.startswith('Space.')}},
        'radius': {'status': 'EXISTING', 'unit': 'px',
                   'scale': {k.split('.')[1].lower(): tok[k] for k in tok if k.startswith('Radius.')}},
        'controlHeight': {'status': 'EXISTING', 'unit': 'px',
                          'scale': {k.split('.')[1].lower(): tok[k] for k in tok if k.startswith('Height.')}},
        'layout': {'status': 'EXISTING', 'unit': 'px',
                   'pageMax': tok['Width.Page'], 'form': tok['Width.Form'], 'sidebar': tok['Width.Sidebar'],
                   'sidebarCompact': tok['Width.SidebarCompact'], 'inspector': tok['Width.Inspector'],
                   'controlColumn': tok['Width.ControlColumn'],
                   'padding': {'page': [32, 28, 32, 40], 'card': 20, 'cardCompact': 16, 'row': [20, 14], 'dialog': 24, 'panel': 16}},
        'typography': {
            'status': 'EXISTING',
            'families': {'ui': 'Inter, Segoe UI Variable Text, Segoe UI', 'mono': 'Cascadia Mono, Consolas, Courier New'},
            'license': 'Inter is licensed under the SIL Open Font License 1.1 (bundled via Avalonia.Fonts.Inter).',
            'scale': {
                'display': {'size': 28, 'weight': 600, 'letterSpacing': -0.5},
                'title': {'size': 20, 'weight': 600, 'letterSpacing': -0.25},
                'section': {'size': 16, 'weight': 600},
                'cardTitle': {'size': 14, 'weight': 600},
                'body': {'size': 13, 'weight': 400},
                'rowTitle': {'size': 13, 'weight': 500},
                'secondary': {'size': 12.5, 'weight': 400, 'lineHeight': 18},
                'caption': {'size': 11.5, 'weight': 400},
                'overline': {'size': 10.5, 'weight': 600, 'letterSpacing': 0.8, 'case': 'upper'},
            },
        },
        'typographyEditorial': {
            'status': 'PROPOSED', 'note': 'For the brand book, website and marketing; the application keeps its own scale.',
            'scale': {
                'hero': {'size': 120, 'weight': 700, 'lineHeight': 1.0, 'letterSpacing': -0.04},
                'display': {'size': 72, 'weight': 700, 'lineHeight': 1.04, 'letterSpacing': -0.035},
                'h1': {'size': 48, 'weight': 650, 'lineHeight': 1.1, 'letterSpacing': -0.03},
                'h2': {'size': 32, 'weight': 600, 'lineHeight': 1.18, 'letterSpacing': -0.02},
                'h3': {'size': 24, 'weight': 600, 'lineHeight': 1.25, 'letterSpacing': -0.012},
                'h4': {'size': 20, 'weight': 600, 'lineHeight': 1.3, 'letterSpacing': -0.008},
                'h5': {'size': 16, 'weight': 600, 'lineHeight': 1.4},
                'h6': {'size': 14, 'weight': 600, 'lineHeight': 1.4},
                'subtitle': {'size': 22, 'weight': 400, 'lineHeight': 1.45},
                'bodyLarge': {'size': 18, 'weight': 400, 'lineHeight': 1.6},
                'body': {'size': 15, 'weight': 400, 'lineHeight': 1.6},
                'caption': {'size': 12, 'weight': 400, 'lineHeight': 1.5},
                'overline': {'size': 11, 'weight': 600, 'lineHeight': 1.2, 'letterSpacing': 0.14, 'case': 'upper'},
                'button': {'size': 13, 'weight': 600, 'lineHeight': 1},
                'code': {'size': 13, 'weight': 400, 'lineHeight': 1.55, 'family': 'mono'},
            },
        },
        'icons': {'status': 'EXISTING', 'grid': 24, 'strokeWidth': 1.75, 'cap': 'round', 'join': 'round',
                  'sizes': {'small': 14, 'default': 16, 'medium': 18, 'large': 20, 'xlarge': 24},
                  'style': 'Lucide-compatible outline strokes; solid glyphs (record, play, pause, stop) are filled'},
        'motion': {
            'status': 'EXISTING',
            'duration': {'fast': 120, 'normal': 180, 'slow': 220, 'unit': 'ms'},
            'easing': 'CubicEaseOut',
            'entrance': {'toast': {'ms': 220, 'translateY': 10}, 'overlayCard': {'ms': 200, 'translateY': 8}, 'scrim': {'ms': 160}},
            'smartTracking': {
                'model': 'critically damped spring (SmoothDamp); no overshoot; framerate independent',
                'zoomInSmoothTime': 0.50, 'zoomOutSmoothTime': 0.75, 'panSmoothTime': 0.32,
                'idleReleaseSeconds': 1.5, 'clickHoldSeconds': 2.5, 'panDeadZoneFraction': 0.12,
                'animationSpeedRangePercent': [0, 50], 'zoomLevels': [1.25, 1.5, 1.75, 2.0, 3.0],
                'unit': 'seconds', 'source': 'VideoCaptureService.cs',
            },
        },
        'motionProposed': {
            'status': 'PROPOSED',
            'easing': {
                'standard': 'cubic-bezier(0.22, 1, 0.36, 1)', 'enter': 'cubic-bezier(0.16, 1, 0.3, 1)',
                'exit': 'cubic-bezier(0.4, 0, 1, 1)', 'camera': 'critically damped spring (no overshoot)',
            },
            'duration': {'instant': 80, 'fast': 120, 'normal': 180, 'slow': 220, 'camera': 500, 'unit': 'ms'},
            'reducedMotion': 'Replace transforms with a 0-80 ms opacity change; never remove state feedback.',
            'specs': [
                {'name': 'Hover', 'ms': 120, 'easing': 'standard', 'props': 'background-color, border-color', 'distance': 0, 'scale': 1},
                {'name': 'Press', 'ms': 80, 'easing': 'standard', 'props': 'background-color, scale', 'distance': 0, 'scale': 0.98},
                {'name': 'Focus ring', 'ms': 120, 'easing': 'standard', 'props': 'opacity', 'distance': 0, 'scale': 1},
                {'name': 'Tab switch', 'ms': 180, 'easing': 'standard', 'props': 'opacity, translateX', 'distance': 8, 'scale': 1},
                {'name': 'Sidebar collapse', 'ms': 220, 'easing': 'standard', 'props': 'width (232 → 68)', 'distance': 164, 'scale': 1},
                {'name': 'Dropdown', 'ms': 160, 'easing': 'enter', 'props': 'opacity, translateY', 'distance': 6, 'scale': 1},
                {'name': 'Dialog enter', 'ms': 200, 'easing': 'enter', 'props': 'opacity, translateY', 'distance': 8, 'scale': 1},
                {'name': 'Dialog exit', 'ms': 140, 'easing': 'exit', 'props': 'opacity', 'distance': 0, 'scale': 1},
                {'name': 'Toast', 'ms': 220, 'easing': 'enter', 'props': 'opacity, translateY', 'distance': 10, 'scale': 1},
                {'name': 'Record start', 'ms': 180, 'easing': 'standard', 'props': 'indicator opacity, scale 0.8 → 1', 'distance': 0, 'scale': 1},
                {'name': 'Export progress', 'ms': 220, 'easing': 'standard', 'props': 'width (determinate)', 'distance': 0, 'scale': 1},
                {'name': 'Smart Tracking camera', 'ms': 500, 'easing': 'camera', 'props': 'crop rectangle (rendered video, not UI)', 'distance': 0, 'scale': 1},
            ],
        },
        'elevation': {
            'status': 'PROPOSED',
            'dark': {'e0': 'none', 'e1': '0 1px 0 rgba(255,255,255,0.04) inset, 0 1px 2px rgba(0,0,0,0.40)',
                     'e2': '0 8px 24px rgba(0,0,0,0.45)', 'overlay': '0 16px 48px rgba(0,0,0,0.55)'},
            'light': {'e0': 'none', 'e1': '0 1px 2px rgba(16,24,40,0.06)', 'e2': '0 6px 20px rgba(16,24,40,0.10)',
                      'overlay': '0 16px 40px rgba(16,24,40,0.16)'},
        },
        'gradients': {
            'status': 'PROPOSED',
            'identity': {'css': 'linear-gradient(135deg, #18DCE8 0%, #7C5CFF 100%)', 'use': 'Illustration, hero accents, progress highlights. Never behind body text.'},
            'spotlightDark': {'css': 'radial-gradient(60% 55% at 50% 0%, rgba(24,220,232,0.20) 0%, rgba(24,220,232,0) 70%)', 'use': 'Cover and chapter openers on Midnight.'},
            'spotlightLight': {'css': 'radial-gradient(60% 55% at 50% 0%, rgba(8,127,140,0.12) 0%, rgba(8,127,140,0) 70%)', 'use': 'Cover and chapter openers on Arctic.'},
            'midnightSurface': {'css': 'linear-gradient(180deg, #101C2B 0%, #09111D 100%)', 'use': 'Window and page backgrounds.'},
            'arcticSurface': {'css': 'linear-gradient(180deg, #FFFFFF 0%, #F6F8FB 100%)', 'use': 'Window and page backgrounds.'},
            'recording': {'css': 'radial-gradient(120% 90% at 50% 120%, rgba(242,85,90,0.22) 0%, rgba(242,85,90,0) 60%)', 'use': 'A restrained glow at the edge of the live preview while recording. Always paired with the REC label.'},
            'accentSubtle': {'css': 'linear-gradient(90deg, rgba(24,220,232,0) 0%, rgba(24,220,232,0.16) 100%)', 'use': 'Highlight behind selected items.'},
            'spectrum': {'css': 'linear-gradient(90deg, #FFAA21, #EA0B7F, #971A8D, #1AABE3, #85C536)', 'use': 'EXISTING-derived: the five logo hues as a thin brand strip (≤ 6 px). Decorative only.'},
        },
        'breakpoints': {'status': 'PROPOSED', 'unit': 'px', 'compact': 960, 'default': 1280, 'wide': 1600, 'ultra': 1920,
                        'note': 'Application windows. The sidebar auto-compacts from 232 to 68 below "default".'},
        'features': feature_tokens,
    }
    return shared, themes, report


def write_json(path, obj):
    path.write_text(json.dumps(obj, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')


def avalonia_proposed(themes):
    """A ready-to-paste ResourceDictionary using Brand.* keys. NOT referenced by the application."""
    def brush(name, e):
        r, g, b, _ = parse(e['hex'])
        a = round(e['alpha'] * 255)
        argb = '#%02X%02X%02X%02X' % (a, r, g, b) if e['alpha'] < 1 else e['hex']
        key = 'Brand.' + ''.join(p.capitalize() for p in re.split(r'[.\-]', name))
        return f'      <SolidColorBrush x:Key="{key}" Color="{argb}" />'
    lines = [
        '<!--',
        '  PROPOSED brand tokens (Midnight / Arctic Precision) — generated by brand/source/build_tokens.py.',
        '  NOT referenced by the application. To trial: merge into Styles/Colors.axaml under the matching',
        '  ThemeDictionaries key and point individual Brush.* aliases at the Brand.* brushes one at a time.',
        '-->',
        '<ResourceDictionary xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">',
        '  <ResourceDictionary.ThemeDictionaries>',
    ]
    for key, label in (('dark', 'Dark'), ('light', 'Light')):
        lines.append(f'    <ResourceDictionary x:Key="{label}">')
        for name, e in themes[key]['proposed']['tokens'].items():
            lines.append(brush(name, e))
        lines.append('    </ResourceDictionary>')
    lines += ['  </ResourceDictionary.ThemeDictionaries>', '</ResourceDictionary>', '']
    return '\n'.join(lines)


if __name__ == '__main__':
    shared, themes, report = build()
    write_json(OUT / 'Cap-IT-Brand-Tokens.json', shared)
    write_json(OUT / 'Cap-IT-Dark-Theme.json', themes['dark'])
    write_json(OUT / 'Cap-IT-Light-Theme.json', themes['light'])
    (OUT / 'avalonia').mkdir(exist_ok=True)
    (OUT / 'avalonia' / 'CapIT.Brand.Proposed.axaml').write_text(avalonia_proposed(themes), encoding='utf-8')
    for key, adj in report:
        print(key, 'adjusted from the starting palette:', {k: (v, themes[key]['proposed']['tokens'][k]['hex']) for k, v in adj.items()})
    print('tokens written')
