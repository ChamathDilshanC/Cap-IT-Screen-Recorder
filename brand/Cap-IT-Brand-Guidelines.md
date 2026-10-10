# Cap-IT Brand Guidelines

**Edition 1.0 · 10 October 2026 · Product baseline v3.13.1 (Windows 10 / 11 x64)**

> The editable specification behind `Cap-IT-Brand-Book.pdf`. It describes the brand that exists in the repository and recommends how it could grow, in two complete themes: **Midnight Precision** (dark) and **Arctic Precision** (light).

## How to read this document

| Label | Meaning |
|---|---|
| **EXISTING** | Verified in the repository: logo artwork, shipped colour/type/spacing/radius/motion tokens, screenshots, README, capture code. Colour tables are parsed from `src/CapIT.Desktop/Styles/*.axaml` at build time. |
| **DERIVED** | Produced mechanically from existing artwork (crop, recolour, plate). Nothing is redrawn. |
| **PROPOSED** | A recommendation with reasoning and measured contrast. Not applied to the application. |
| **CONCEPT** | Exploratory visual (mock-ups, graphic studies). Illustrative, not a feature promise. |

Files: `Cap-IT-Brand-Tokens.json` (shared tokens) · `Cap-IT-Dark-Theme.json` · `Cap-IT-Light-Theme.json` (each holds `implemented` = shipped values and `proposed` = Midnight/Arctic tokens with measured contrast) · `avalonia/CapIT.Brand.Proposed.axaml` (not referenced by the app) · `Cap-IT-Brand-Assets/`.

The recording pipeline and its separate technical report (`docs/SMART-TRACKING-RECORDING-FIX.md`) were not touched by this work.

## 1. Brand introduction

**Product:** Cap-IT Screen Recorder — a focused, GPU-assisted Windows screen recorder for polished tutorials, demos, bug reports and social clips. Capture, smart camera movement, audio monitoring, annotation, effects, editing and export in one native app. **EXISTING** (README).

**Philosophy (EXISTING):** “The recording should be easy, while the result should look intentional.”

**Tagline (PROPOSED):** *Capture with intention.* Supporting lines: Capture beautifully · Make every recording count · From screen to story · Every detail, captured · Record. Refine. Share.

| Element | Statement |
|---|---|
| Purpose | Remove the distance between doing something on screen and showing it well. |
| Vision | Every tutorial, bug report and walkthrough made on Windows can be captured, refined and shared in one sitting — without a second tool and without a result that looks accidental. |
| Mission | Build the most considered native recorder on Windows: precise capture, a camera that follows the action, and a review workspace that keeps the original safe. |
| Positioning | For people who teach, demonstrate and report on Windows, Cap-IT is the native screen recorder that follows the action and hands back a finished-looking result — without leaving the app. |
| Value proposition | Capture the right thing · Look intentional · Finish in place. |
| Principles | Precision · Motion · Clarity · Creativity · Control. |

**Brand story.** Cap-IT began as a way to make tutorials without juggling a recorder, a zoom plug-in, a webcam tool, an annotation overlay and a video editor. Each release pulled one more of those into the same window. It is deliberately a craft tool, not a platform: native, quick to open, quiet in use and honest about what it does.

**Audience:** software developers · educators and instructors · content creators · UI/UX designers · product teams · technical documentation authors · QA engineers · freelancers · SaaS founders · tutorial creators · marketing and demo teams. No usage numbers are claimed.

## 2. Brand strategy

**Archetype:** the Craftsperson with a steady hand — exact, calm, helpful, quietly proud of a clean result.

| Spectrum | Leans | Position (0 = left, 100 = right) |
|---|---|---|
| Technical ↔ Approachable | slightly approachable | 58 |
| Expressive ↔ Restrained | restrained | 72 |
| Playful ↔ Professional | professional | 68 |
| Premium ↔ Accessible | balanced | 44 |
| Experimental ↔ Dependable | dependable | 80 |

| Customer pain point | Benefit | Evidence in the product |
|---|---|---|
| Recordings look amateur | Smart Tracking follows cursor and text caret and eases back out | Interaction-triggered zoom, critically damped motion |
| Too many tools for one tutorial | Capture, effects, webcam, annotations, trim, compose, export in one window | One native app, one library |
| Unsure what is being recorded | Live source thumbnails and a live preview | DXGI + Windows Graphics Capture previews |
| Edits risk the original | Edits are metadata beside the video | “Original recording preserved” |
| Heavy recorders slow the machine | GPU-assisted processing, hardware encoders with software fallback | NVENC · AMF · QSV · x264 |
| Sharing needs another conversion | MP4 and two-pass GIF export from the same workspace | Review & Export |

**Differentiators:** (1) native Windows workflow · (2) GPU-assisted recording and processing · (3) Smart Tracking zoom · (4) smooth, controlled camera movement · (5) capture and editing in one app · (6) cursor interaction enhancements · (7) customisable composition and backgrounds · (8) hardware-accelerated encoding · (9) integrated audio capture and monitoring · (10) MP4 and GIF delivery.

**Core promises:** you will see what is being recorded · your original is never overwritten · effects can change while you record. **Trust:** native, not a web wrapper; original preserved. **Perception goals:** considered, fast to learn, “my videos look better than I expected”.

| Moment | Feeling | Example (EXISTING copy) |
|---|---|---|
| Onboarding | Welcoming, unhurried | Ready to create your next recording? |
| Recording | Quiet and certain | Ready · Record |
| Editing | Precise and generous | Original recording preserved · edits save automatically |
| Exporting | Satisfying, honest | Export video · Export GIF 720px / 12 fps |

## 3. Logo identity

**EXISTING artwork:** `assets/Logo-CapIT.png` (2000 × 2000, transparent): five rounded arcs with detached round terminals turning around a centre, with a small “CAP IT” wordmark. `assets/Logo-Mark.png` (256 px) is the symbol alone. The repository has **no vector master**; every variant below is a crop or recolour of this artwork.

| Hue | HEX | RGB |
|---|---|---|
| Orange | #FFAA21 | 255, 170, 33 |
| Magenta | #EA0B7F | 234, 11, 127 |
| Purple | #971A8D | 151, 26, 141 |
| Blue | #1AABE3 | 26, 171, 227 |
| Green | #85C536 | 133, 197, 54 |
| Wordmark grey | #3A3939 | 58, 57, 57 |

| Variant | Status | File |
|---|---|---|
| Stacked lockup (colour) | EXISTING | `Cap-IT-Brand-Assets/logo/existing/capit-lockup-stacked.png` |
| Symbol only | EXISTING | `logo/existing/capit-symbol.png` · `capit-symbol-256-app.png` · `AppIcon.ico` |
| Stacked, reversed (dark backgrounds) | DERIVED | `logo/derived/capit-lockup-stacked-reversed.png` (wordmark #3A3939 → #F5F9FC) |
| Monochrome / inverted | DERIVED | `logo/derived/capit-symbol-mono-{black,white,cyan-midnight,cyan-arctic}.png` and `capit-lockup-mono-*.png` |
| Horizontal lockup | PROPOSED | `logo/proposed/capit-lockup-horizontal-on-{midnight,arctic}.png` (symbol + “Cap-IT” in Inter SemiBold; cap-height = 38 % of symbol height) |
| App icon plates | PROPOSED | `app-icon/capit-icon-{midnight,arctic}-{16…1024}.png`, `capit-icon-midnight.ico` |
| Social avatar (round 512) | PROPOSED | `app-icon/capit-avatar-{midnight,arctic}-512.png` |

**Anatomy & geometry.** Symbol artwork ratio 0.989 : 1 (1375 × 1391 px). **Clear space** = X on every side, X = diameter of the largest round terminal. **Minimum size:** symbol 16 px (favicon/tray; use the app-icon plate), stacked lockup 200 px symbol height (the wordmark is only 2.4 % of the symbol height and becomes illegible below that), horizontal lockup 24 px, print 8 mm. **Optical alignment:** centre on the circle’s centre, not on the bounding box.

**Backgrounds.** Colour symbol on Midnight (`#09111D`, `#142633`) and Arctic (`#FFFFFF`, `#F1F5F9`). On saturated brand fields (cyan, teal) use mono white or mono black. Photographs only where the area behind the symbol is flat and calm (≥ 4.5 : 1 average contrast).

**Incorrect usage (eight):** stretch/squash · rotate · recolour the hues · add shadow or glow · outline/greyscale tricks · reduce opacity · crop the symbol · place on busy imagery. **Accessibility:** decorative next to the name “Cap-IT” (empty alt text); “Cap-IT logo” when alone.

## 4. Dual-theme identity — Midnight & Arctic

### 4.1 What ships today (EXISTING)

Both a Dark and a Light theme are implemented in `Styles/Colors.axaml` and switched with `RequestedThemeVariant` (System · Light · Dark). Views reference semantic `Brush.*` keys only.

| Key | Dark | Light |
|---|---|---|
| `Brush.BackgroundPrimary` | `#0B0D11` | `#F4F5F7` |
| `Brush.BackgroundSecondary` | `#0E1015` | `#ECEEF1` |
| `Brush.Surface` | `#13161C` | `#FFFFFF` |
| `Brush.SurfaceRaised` | `#191D24` | `#FFFFFF` |
| `Brush.BorderSubtle` | `#1C2129` | `#E4E7EC` |
| `Brush.BorderNormal` | `#272D36` | `#D5DAE1` |
| `Brush.BorderStrong` | `#38404B` | `#B6BEC9` |
| `Brush.TextPrimary` | `#E7EAEF` | `#12161C` |
| `Brush.TextSecondary` | `#A1A9B6` | `#4D5664` |
| `Brush.TextMuted` | `#6C7583` | `#78818F` |
| `Brush.TextDisabled` | `#4A515C` | `#A9B0BB` |
| `Brush.TextOnAccent` | `#03191C` | `#FFFFFF` |
| `Brush.Accent` | `#22CBDD` | `#0A8FA0` |
| `Brush.AccentHover` | `#47D6E5` | `#0B9DB0` |
| `Brush.AccentPressed` | `#1AB2C3` | `#087A89` |
| `Brush.AccentText` | `#5EDCEA` | `#08788A` |
| `Brush.Success` | `#3DD68C` | `#0E9F6E` |
| `Brush.Warning` | `#F5B83D` | `#B7740B` |
| `Brush.Error` | `#F26D6D` | `#D23B3B` |
| `Brush.Info` | `#6AA8FF` | `#2F6FDB` |
| `Brush.Danger` | `#E5484D` | `#D93036` |
| `Brush.Recording` | `#F2555A` | `#E5383E` |

### 4.2 Accessibility audit of the shipped themes

**Dark**

| Pair | Ratio | Kind | Result |
|---|---|---|---|
| TextPrimary on BackgroundPrimary | 16.13 | text | AAA |
| TextSecondary on BackgroundPrimary | 8.21 | text | AAA |
| TextSecondary on Surface | 7.65 | text | AAA |
| TextMuted on BackgroundPrimary | 4.18 | text | FAIL |
| TextMuted on Surface | 3.89 | text | FAIL |
| AccentText on Surface | 11.13 | text | AAA |
| Accent on BackgroundPrimary | 9.87 | non-text | AA |
| Accent on Surface | 9.19 | non-text | AA |
| TextOnAccent on Accent | 9.19 | text | AAA |
| Success on Surface | 9.66 | text | AAA |
| Warning on Surface | 10.18 | text | AAA |
| Error on Surface | 6.20 | text | AA |
| Info on Surface | 7.47 | text | AAA |
| Recording on Surface | 5.37 | text | AA |
| BorderNormal on Surface | 1.31 | non-text | FAIL |
| BorderStrong on Surface | 1.73 | non-text | FAIL |
| TextDisabled on Surface | 2.26 | disabled (exempt) | exempt |

**Light**

| Pair | Ratio | Kind | Result |
|---|---|---|---|
| TextPrimary on BackgroundPrimary | 16.63 | text | AAA |
| TextSecondary on BackgroundPrimary | 6.80 | text | AA |
| TextSecondary on Surface | 7.42 | text | AAA |
| TextMuted on BackgroundPrimary | 3.61 | text | FAIL |
| TextMuted on Surface | 3.94 | text | FAIL |
| AccentText on Surface | 5.17 | text | AA |
| Accent on BackgroundPrimary | 3.53 | non-text | AA |
| Accent on Surface | 3.85 | non-text | AA |
| TextOnAccent on Accent | 3.85 | text | FAIL |
| Success on Surface | 3.39 | text | FAIL |
| Warning on Surface | 3.81 | text | FAIL |
| Error on Surface | 4.74 | text | AA |
| Info on Surface | 4.75 | text | AA |
| Recording on Surface | 4.23 | text | FAIL |
| BorderNormal on Surface | 1.41 | non-text | FAIL |
| BorderStrong on Surface | 1.88 | non-text | FAIL |
| TextDisabled on Surface | 2.18 | disabled (exempt) | exempt |

Findings: the Light primary button label (#FFFFFF on #0A8FA0) is 3.85 : 1; Light Success/Warning/Recording text is 3.4–4.2 : 1; Dark Muted text is 3.9–4.2 : 1; card/input borders are 1.4–1.9 : 1 in both themes (fine for decoration, not for identifying an input — SC 1.4.11 asks 3 : 1). Disabled text is exempt. **Nothing in the application was changed.**

### 4.3 Proposed Midnight and Arctic tokens (PROPOSED)

Starting point: the palette in the brief. Anything that missed WCAG 2.2 AA was moved the shortest distance that passes (“adjusted from”). Text-bearing layers: Midnight `bg.app`, `bg.secondary`, `layer1`, `layer2`; Arctic `bg.app`, `bg.secondary`, `layer2`. Layer 3 holds no text.

#### Midnight Precision (dark)

| Token | HEX | RGB | HSL | CMYK≈ | Contrast (on text layers) | Adjusted | Usage |
|---|---|---|---|---|---|---|---|
| `bg.app` | `#09111D` | 9, 17, 29 | 216, 53%, 7% | 69, 41, 0, 89 | — |  | Window background, sidebar and title bar; marketing and document canvas. |
| `bg.secondary` | `#101C2B` | 16, 28, 43 | 213, 46%, 12% | 63, 35, 0, 83 | — |  | Page canvas behind cards inside the application. |
| `surface.layer1` | `#142633` | 20, 38, 51 | 205, 44%, 14% | 61, 25, 0, 80 | — |  | Cards, panels and the default container surface. |
| `surface.layer2` | `#1B3042` | 27, 48, 66 | 208, 42%, 18% | 59, 27, 0, 74 | — |  | Raised or inset wells: inputs, segmented tracks, hovered rows. |
| `surface.layer3` | `#243D52` | 36, 61, 82 | 207, 39%, 23% | 56, 26, 0, 68 | — |  | Tracks, meters and skeletons. Non-text only: no text is placed on this layer. |
| `fg.primary` | `#F5F9FC` | 245, 249, 252 | 206, 54%, 97% | 3, 1, 0, 1 | 17.88, 16.22, 14.64, 12.82 |  | Headlines, body copy, values and any text that carries meaning. |
| `fg.secondary` | `#93A8B9` | 147, 168, 185 | 207, 21%, 65% | 21, 9, 0, 27 | 7.70, 6.98, 6.31, 5.52 |  | Descriptions, labels, secondary values. |
| `fg.muted` | `#8398A7` | 131, 152, 167 | 205, 17%, 58% | 22, 9, 0, 35 | 6.32, 5.74, 5.18, 4.53 | was `#6F8799` | Captions, placeholders, timestamps and helper text. |
| `fg.disabled` | `#4F6578` | 79, 101, 120 | 208, 21%, 39% | 34, 16, 0, 53 | — |  | Disabled text and icons. Exempt from contrast by WCAG; never use for information that is not available elsewhere. |
| `brand.primary` | `#18DCE8` | 24, 220, 232 | 183, 82%, 50% | 90, 5, 0, 9 | 11.21, 10.17, 9.18, 8.04 |  | The Cap-IT accent: primary button fill, switch and slider fill, active navigation, links, focus ring. |
| `brand.primary.hover` | `#4BE6EE` | 75, 230, 238 | 183, 83%, 61% | 68, 3, 0, 7 | — |  | Hover state of brand.primary fills. |
| `brand.primary.pressed` | `#12B7C2` | 18, 183, 194 | 184, 83%, 42% | 91, 6, 0, 24 | — |  | Pressed state of brand.primary fills. |
| `brand.on-primary` | `#04141A` | 4, 20, 26 | 196, 73%, 6% | 85, 23, 0, 90 | — |  | Label and icon colour placed on a brand.primary fill. |
| `brand.secondary` | `#7C5CFF` | 124, 92, 255 | 252, 100%, 68% | 51, 64, 0, 0 | — |  | Creative accent for graphics, gradients, illustrations and non-text indicators. |
| `brand.secondary.text` | `#9B82FF` | 155, 130, 255 | 252, 100%, 75% | 39, 49, 0, 0 | 6.34, 5.75, 5.19, 4.54 | was `#7C5CFF` | Violet as text or small icon on any text-bearing surface. |
| `brand.secondary.solid` | `#7958FF` | 121, 88, 255 | 252, 100%, 67% | 53, 65, 0, 0 | 4.51 (brand.secondary.on-solid) | was `#7C5CFF` | Violet as a solid fill that carries a label. |
| `brand.secondary.on-solid` | `#FFFFFF` | 255, 255, 255 | 0, 0%, 100% | 0, 0, 0, 0 | — |  | Label colour on brand.secondary.solid. |
| `border.subtle` | `#294050` | 41, 64, 80 | 205, 32%, 24% | 49, 20, 0, 69 | — |  | Decorative card and panel outlines. Not relied on to identify a control. |
| `divider` | `#203443` | 32, 52, 67 | 206, 35%, 19% | 52, 22, 0, 74 | — |  | Hairlines between rows and sections. |
| `border.control` | `#527188` | 82, 113, 136 | 206, 25%, 43% | 40, 17, 0, 47 | 3.67, 3.01 | was `#41596B` | Outline of inputs, dropdowns, checkboxes and sliders: meets the 3:1 non-text rule (SC 1.4.11). |
| `status.info` | `#6AA8FF` | 106, 168, 255 | 215, 100%, 71% | 58, 34, 0, 0 | 7.80, 7.08, 6.39, 5.59 |  | Informational messages and neutral highlights. |
| `status.success` | `#10B981` | 16, 185, 129 | 160, 84%, 39% | 91, 0, 30, 27 | 7.46, 6.77, 6.11, 5.35 |  | Success feedback and completed states. |
| `status.warning` | `#F59E0B` | 245, 158, 11 | 38, 92%, 50% | 0, 36, 96, 4 | 8.81, 8.00, 7.22, 6.32 |  | Warnings and paused or at-risk states. |
| `status.error` | `#F87171` | 248, 113, 113 | 0, 91%, 71% | 0, 54, 54, 3 | 6.84, 6.21, 5.60, 4.90 |  | Errors and destructive confirmations. |
| `rec.active` | `#F3686C` | 243, 104, 108 | 358, 85%, 68% | 0, 57, 56, 5 | 6.32, 5.73, 5.17, 4.53 | was `#F2555A` | The live recording indicator, timer dot and record state. |
| `rec.paused` | `#F59E0B` | 245, 158, 11 | 38, 92%, 50% | 0, 36, 96, 4 | 8.81, 8.00, 7.22, 6.32 |  | Paused recording indicator. |
| `rec.exporting` | `#9B82FF` | 155, 130, 255 | 252, 100%, 75% | 39, 49, 0, 0 | 6.34, 5.75, 5.19, 4.54 | was `#7C5CFF` | Export in progress. |
| `rec.export-complete` | `#10B981` | 16, 185, 129 | 160, 84%, 39% | 91, 0, 30, 27 | 7.46, 6.77, 6.11, 5.35 |  | Export finished successfully. |
| `state.hover` | `#FFFFFF` α0.06 | 255, 255, 255 | 0, 0%, 100% | 0, 0, 0, 0 | — |  | Hover wash over any surface. |
| `state.pressed` | `#FFFFFF` α0.1 | 255, 255, 255 | 0, 0%, 100% | 0, 0, 0, 0 | — |  | Pressed wash over any surface. |
| `state.selected` | `#18DCE8` α0.16 | 24, 220, 232 | 183, 82%, 50% | 90, 5, 0, 9 | — |  | Selected row, tab or navigation item background. |
| `state.focus-ring` | `#18DCE8` | 24, 220, 232 | 183, 82%, 50% | 90, 5, 0, 9 | 11.21, 10.17, 9.18, 8.04 |  | 2px focus ring with a 2px offset on every focusable control. |
| `state.selection-text` | `#18DCE8` α0.3 | 24, 220, 232 | 183, 82%, 50% | 90, 5, 0, 9 | — |  | Selected text highlight. |
| `state.disabled-fill` | `#FFFFFF` α0.04 | 255, 255, 255 | 0, 0%, 100% | 0, 0, 0, 0 | — |  | Disabled control fill. |
| `surface.overlay` | `#1B3042` | 27, 48, 66 | 208, 42%, 18% | 59, 27, 0, 74 | — |  | Popovers, menus, tooltips and dialogs. Pair with the overlay shadow. |
| `surface.scrim` | `#050B14` α0.72 | 5, 11, 20 | 216, 60%, 5% | 75, 45, 0, 92 | — |  | Dim layer behind modal dialogs. |

#### Arctic Precision (light)

| Token | HEX | RGB | HSL | CMYK≈ | Contrast (on text layers) | Adjusted | Usage |
|---|---|---|---|---|---|---|---|
| `bg.app` | `#FFFFFF` | 255, 255, 255 | 0, 0%, 100% | 0, 0, 0, 0 | — |  | Window background, sidebar and title bar; marketing and document canvas. |
| `bg.secondary` | `#F6F8FB` | 246, 248, 251 | 216, 38%, 97% | 2, 1, 0, 2 | — |  | Page canvas behind cards inside the application. |
| `surface.layer1` | `#FFFFFF` | 255, 255, 255 | 0, 0%, 100% | 0, 0, 0, 0 | — |  | Cards, panels and the default container surface. |
| `surface.layer2` | `#F1F5F9` | 241, 245, 249 | 210, 40%, 96% | 3, 2, 0, 2 | — |  | Raised or inset wells: inputs, segmented tracks, hovered rows. |
| `surface.layer3` | `#E8EEF4` | 232, 238, 244 | 210, 35%, 93% | 5, 2, 0, 4 | — |  | Tracks, meters and skeletons. Non-text only: no text is placed on this layer. |
| `fg.primary` | `#111827` | 17, 24, 39 | 221, 39%, 11% | 56, 38, 0, 85 | 17.74, 16.67, 16.19 |  | Headlines, body copy, values and any text that carries meaning. |
| `fg.secondary` | `#475569` | 71, 85, 105 | 215, 19%, 35% | 32, 19, 0, 59 | 7.58, 7.12, 6.92 |  | Descriptions, labels, secondary values. |
| `fg.muted` | `#667182` | 102, 113, 130 | 216, 12%, 45% | 22, 13, 0, 49 | 4.94, 4.65, 4.51 | was `#7B8798` | Captions, placeholders, timestamps and helper text. |
| `fg.disabled` | `#A9B4C2` | 169, 180, 194 | 214, 17%, 71% | 13, 7, 0, 24 | — |  | Disabled text and icons. Exempt from contrast by WCAG; never use for information that is not available elsewhere. |
| `brand.primary` | `#087C88` | 8, 124, 136 | 186, 89%, 28% | 94, 9, 0, 47 | 4.94, 4.65, 4.51 | was `#087F8C` | The Cap-IT accent: primary button fill, switch and slider fill, active navigation, links, focus ring. |
| `brand.primary.hover` | `#066A76` | 6, 106, 118 | 186, 90%, 24% | 95, 10, 0, 54 | — |  | Hover state of brand.primary fills. |
| `brand.primary.pressed` | `#055C66` | 5, 92, 102 | 186, 91%, 21% | 95, 10, 0, 60 | — |  | Pressed state of brand.primary fills. |
| `brand.on-primary` | `#FFFFFF` | 255, 255, 255 | 0, 0%, 100% | 0, 0, 0, 0 | — |  | Label and icon colour placed on a brand.primary fill. |
| `brand.secondary` | `#6941C6` | 105, 65, 198 | 258, 54%, 52% | 47, 67, 0, 22 | — |  | Creative accent for graphics, gradients, illustrations and non-text indicators. |
| `brand.secondary.text` | `#6941C6` | 105, 65, 198 | 258, 54%, 52% | 47, 67, 0, 22 | 6.62, 6.22, 6.04 |  | Violet as text or small icon on any text-bearing surface. |
| `brand.secondary.solid` | `#6941C6` | 105, 65, 198 | 258, 54%, 52% | 47, 67, 0, 22 | 6.62 (brand.secondary.on-solid) |  | Violet as a solid fill that carries a label. |
| `brand.secondary.on-solid` | `#FFFFFF` | 255, 255, 255 | 0, 0%, 100% | 0, 0, 0, 0 | — |  | Label colour on brand.secondary.solid. |
| `border.subtle` | `#E2E8F0` | 226, 232, 240 | 214, 32%, 91% | 6, 3, 0, 6 | — |  | Decorative card and panel outlines. Not relied on to identify a control. |
| `divider` | `#E9EEF3` | 233, 238, 243 | 210, 29%, 93% | 4, 2, 0, 5 | — |  | Hairlines between rows and sections. |
| `border.control` | `#8191A8` | 129, 145, 168 | 215, 18%, 58% | 23, 14, 0, 34 | 3.21, 3.01 | was `#B0BAC8` | Outline of inputs, dropdowns, checkboxes and sliders: meets the 3:1 non-text rule (SC 1.4.11). |
| `status.info` | `#2A6BDA` | 42, 107, 218 | 218, 70%, 51% | 81, 51, 0, 15 | 4.98, 4.68, 4.55 | was `#2F6FDB` | Informational messages and neutral highlights. |
| `status.success` | `#047857` | 4, 120, 87 | 163, 94%, 24% | 97, 0, 27, 53 | 5.48, 5.15, 5.01 |  | Success feedback and completed states. |
| `status.warning` | `#B45309` | 180, 83, 9 | 26, 90%, 37% | 0, 54, 95, 29 | 5.02, 4.72, 4.58 |  | Warnings and paused or at-risk states. |
| `status.error` | `#DA2323` | 218, 35, 35 | 0, 72%, 50% | 0, 84, 84, 15 | 4.95, 4.65, 4.51 | was `#DC2626` | Errors and destructive confirmations. |
| `rec.active` | `#DC1D23` | 220, 29, 35 | 358, 77%, 49% | 0, 87, 84, 14 | 4.95, 4.65, 4.52 | was `#E5383E` | The live recording indicator, timer dot and record state. |
| `rec.paused` | `#B45309` | 180, 83, 9 | 26, 90%, 37% | 0, 54, 95, 29 | 5.02, 4.72, 4.58 |  | Paused recording indicator. |
| `rec.exporting` | `#6941C6` | 105, 65, 198 | 258, 54%, 52% | 47, 67, 0, 22 | 6.62, 6.22, 6.04 |  | Export in progress. |
| `rec.export-complete` | `#047857` | 4, 120, 87 | 163, 94%, 24% | 97, 0, 27, 53 | 5.48, 5.15, 5.01 |  | Export finished successfully. |
| `state.hover` | `#09111D` α0.05 | 9, 17, 29 | 216, 53%, 7% | 69, 41, 0, 89 | — |  | Hover wash over any surface. |
| `state.pressed` | `#09111D` α0.09 | 9, 17, 29 | 216, 53%, 7% | 69, 41, 0, 89 | — |  | Pressed wash over any surface. |
| `state.selected` | `#087C88` α0.1 | 8, 124, 136 | 186, 89%, 28% | 94, 9, 0, 47 | — |  | Selected row, tab or navigation item background. |
| `state.focus-ring` | `#087C88` | 8, 124, 136 | 186, 89%, 28% | 94, 9, 0, 47 | 4.94, 4.65, 4.51 |  | 2px focus ring with a 2px offset on every focusable control. |
| `state.selection-text` | `#087C88` α0.22 | 8, 124, 136 | 186, 89%, 28% | 94, 9, 0, 47 | — |  | Selected text highlight. |
| `state.disabled-fill` | `#09111D` α0.04 | 9, 17, 29 | 216, 53%, 7% | 69, 41, 0, 89 | — |  | Disabled control fill. |
| `surface.overlay` | `#FFFFFF` | 255, 255, 255 | 0, 0%, 100% | 0, 0, 0, 0 | — |  | Popovers, menus, tooltips and dialogs. Pair with the overlay shadow. |
| `surface.scrim` | `#09111D` α0.4 | 9, 17, 29 | 216, 53%, 7% | 69, 41, 0, 89 | — |  | Dim layer behind modal dialogs. |

Accessible foreground pairings: text on a layer uses `fg.primary`/`fg.secondary`/`fg.muted`; a `brand.primary` fill carries `brand.on-primary`; a violet fill carries `brand.secondary.on-solid`. Colour is never the only carrier of status (icon or label always).

### 4.4 Gradients and elevation (PROPOSED)

| Name | CSS | Use |
|---|---|---|
| identity | `linear-gradient(135deg, #18DCE8 0%, #7C5CFF 100%)` | Illustration, hero accents, progress highlights. Never behind body text. |
| spotlightDark | `radial-gradient(60% 55% at 50% 0%, rgba(24,220,232,0.20) 0%, rgba(24,220,232,0) 70%)` | Cover and chapter openers on Midnight. |
| spotlightLight | `radial-gradient(60% 55% at 50% 0%, rgba(8,127,140,0.12) 0%, rgba(8,127,140,0) 70%)` | Cover and chapter openers on Arctic. |
| midnightSurface | `linear-gradient(180deg, #101C2B 0%, #09111D 100%)` | Window and page backgrounds. |
| arcticSurface | `linear-gradient(180deg, #FFFFFF 0%, #F6F8FB 100%)` | Window and page backgrounds. |
| recording | `radial-gradient(120% 90% at 50% 120%, rgba(242,85,90,0.22) 0%, rgba(242,85,90,0) 60%)` | A restrained glow at the edge of the live preview while recording. Always paired with the REC label. |
| accentSubtle | `linear-gradient(90deg, rgba(24,220,232,0) 0%, rgba(24,220,232,0.16) 100%)` | Highlight behind selected items. |
| spectrum | `linear-gradient(90deg, #FFAA21, #EA0B7F, #971A8D, #1AABE3, #85C536)` | EXISTING-derived: the five logo hues as a thin brand strip (≤ 6 px). Decorative only. |

| Level | Midnight | Arctic |
|---|---|---|
| e0 | `none` | `none` |
| e1 | `0 1px 0 rgba(255,255,255,0.04) inset, 0 1px 2px rgba(0,0,0,0.40)` | `0 1px 2px rgba(16,24,40,0.06)` |
| e2 | `0 8px 24px rgba(0,0,0,0.45)` | `0 6px 20px rgba(16,24,40,0.10)` |
| overlay | `0 16px 48px rgba(0,0,0,0.55)` | `0 16px 40px rgba(16,24,40,0.16)` |

### 4.5 Migration path from the shipped tokens

| Existing key | Proposed token | Why |
|---|---|---|
| `Brush.BackgroundPrimary` | `bg.app` | Navy-tinted dark; white-first light |
| `Brush.Surface` | `surface.layer1` | Same role, new values |
| `Brush.Accent` | `brand.primary` | Brighter dark; light passes 4.5 : 1 |
| `Brush.TextOnAccent` | `brand.on-primary` | Fixes light button label 3.85 → 5.1 : 1 |
| `Brush.TextMuted` | `fg.muted` | Fixes 3.9–4.2 : 1 |
| `Brush.BorderNormal` (inputs) | `border.control` | Inputs reach 3 : 1 |
| `Brush.Success/Warning/Error` | `status.*` | Light variants ≥ 4.5 : 1 |
| `Brush.Recording` | `rec.active` | Passes on every text layer |
| (new) | `brand.secondary*` | Creative accent |

Steps: (1) merge `avalonia/CapIT.Brand.Proposed.axaml` as new `Brand.*` keys; (2) alias one `Brush.*` key at a time; (3) re-run the UI snapshot harness in both themes; (4) retire old values only after sign-off.

### 4.6 Theme behaviour

- Manual switching (System · Light · Dark) and system sync exist: `App.ApplyTheme` sets `RequestedThemeVariant` (`ThemeVariant.Default` = follow Windows). **EXISTING**
- Preference persisted in `settings.json` and applied before the first window is shown. **EXISTING / recommended**
- Theme is presentation state only. A search of `CapIT.Infrastructure.Windows` for “Theme” finds nothing, so switching mid-recording only repaints brushes and never reinitialises capture, audio or encoding. **EXISTING (verified)**
- Proposed: 180 ms cross-fade of surfaces, none under reduced motion; never animate the live preview, meters or REC indicator. Icons use `currentColor`; use the reversed lockup on Midnight.

## 5. Typography

**EXISTING:** Inter (SIL OFL 1.1, via Avalonia.Fonts.Inter) → Segoe UI Variable Text → Segoe UI; mono Cascadia Mono → Consolas → Courier New. One family; hierarchy through size, weight and space.

| Role | Size px | Weight | Other |
|---|---|---|---|
| display | 28 | 600 | letterSpacing -0.5 |
| title | 20 | 600 | letterSpacing -0.25 |
| section | 16 | 600 |  |
| cardTitle | 14 | 600 |  |
| body | 13 | 400 |  |
| rowTitle | 13 | 500 |  |
| secondary | 12.5 | 400 | lineHeight 18 |
| caption | 11.5 | 400 |  |
| overline | 10.5 | 600 | letterSpacing 0.8, case upper |

**PROPOSED editorial scale** (book, website, marketing):

| Style | Size px | Weight | Line height | Tracking em | Notes |
|---|---|---|---|---|---|
| hero | 120 | 700 | 1.0 | -0.04 |  |
| display | 72 | 700 | 1.04 | -0.035 |  |
| h1 | 48 | 650 | 1.1 | -0.03 |  |
| h2 | 32 | 600 | 1.18 | -0.02 |  |
| h3 | 24 | 600 | 1.25 | -0.012 |  |
| h4 | 20 | 600 | 1.3 | -0.008 |  |
| h5 | 16 | 600 | 1.4 | 0 |  |
| h6 | 14 | 600 | 1.4 | 0 |  |
| subtitle | 22 | 400 | 1.45 | 0 |  |
| bodyLarge | 18 | 400 | 1.6 | 0 |  |
| body | 15 | 400 | 1.6 | 0 |  |
| caption | 12 | 400 | 1.5 | 0 |  |
| overline | 11 | 600 | 1.2 | 0.14 | upper |
| button | 13 | 600 | 1 | 0 |  |
| code | 13 | 400 | 1.55 | 0 | mono |

- Capitalisation: sentence case, except overlines (uppercase, +0.14 em) and the product name.
- Numeric displays (timer, resolution, bitrate) use tabular figures.
- Application text does not scale with window width; layout reflows. Headlines wrap, never truncate; single-line values truncate with an ellipsis and tooltip.
- Localisation: Inter covers Latin, Greek and Cyrillic. Sinhala, Tamil, CJK and other scripts fall back to Segoe UI Variable / Segoe UI / system fallback — verify per language. Allow 30–40 % text expansion.
- Fonts: only Inter (OFL) and Cascadia Mono (OFL) are bundled in `brand/source/fonts`; no restricted font is redistributed.

## 6. Layout, grid and spacing (EXISTING)

| Spacing token | px |
|---|---|
| `Space.1` | 4.0 |
| `Space.2` | 8.0 |
| `Space.3` | 12.0 |
| `Space.4` | 16.0 |
| `Space.5` | 20.0 |
| `Space.6` | 24.0 |
| `Space.8` | 32.0 |
| `Space.10` | 40.0 |
| `Space.12` | 48.0 |

| Radius | px |
|---|---|
| xsmall | 4.0 |
| small | 6.0 |
| medium | 8.0 |
| large | 12.0 |
| xlarge | 14.0 |
| pill | 999.0 |

| Control height | px |
|---|---|
| small | 30.0 |
| default | 36.0 |
| large | 44.0 |
| titlebar | 44.0 |
| navitem | 36.0 |

| Layout metric | px |
|---|---|
| Page content max | 1440.0 |
| Form | 880.0 |
| Sidebar | 232.0 |
| Sidebar compact | 68.0 |
| Inspector | 340.0 |
| Control column | 240.0 |
| Page padding | 32 28 32 40 |
| Card padding | 20 (compact 16) |
| Row padding | 20 14 |
| Dialog padding | 24 |

Windows: Main 1360 × 860 (min 1024 × 640); Review 1440 × 900 (min 960 × 600). **PROPOSED breakpoints:** compact < 1280, default 1280–1599, wide ≥ 1600; the sidebar auto-compacts in the compact band. Editorial grid: 12 columns, 96 px margins, 24 px gutters, 8 px baseline on a 1920 canvas. Layout is in device-independent px; reference renders were made at 125 % scaling.

## 7. Iconography (EXISTING)

87 icons in `Styles/Icons.axaml`, exported 1:1 to `Cap-IT-Brand-Assets/icons/*.svg`. 24 × 24 grid, 1.75 px round-cap/round-join outline strokes (Lucide-compatible), strokes scale with size; record/play/pause/stop are the only solid glyphs. Sizes 14 · 16 · 18 · 20 · 24. Colour: default secondary text, active `brand.primary`, disabled `fg.disabled`; inherit `currentColor`. Pair icon-only buttons with a tooltip and accessible name.

## 8. Brand graphic language (PROPOSED)

Capture brackets · focus target · viewport frame · recording indicator (dot + label + timer) · motion trail / camera path · timeline geometry · precision crosshair · layered video frames · technical grid with soft radial illumination (≤ 55 % opacity). SVGs in `Cap-IT-Brand-Assets/graphics/`. Density levels: 0 quiet (application), 1 framed (dialogs, empty states), 2 expressive (marketing). At most two devices per composition; never decoration behind text; the logo’s five hues appear only as a ≤ 6 px spectrum strip.

## 9. Motion identity

**EXISTING:** durations fast 120 / normal 180 / slow 220 ms; `CubicEaseOut` entrances (toast 220 ms, 10 px; overlay card 200 ms, 8 px; scrim 160 ms).

**Smart Tracking camera (EXISTING, `VideoCaptureService.cs`):** critically damped spring (SmoothDamp); no overshoot; framerate independent. Zoom-in smooth time 0.5 s, zoom-out 0.75 s, pan 0.32 s; releases after 1.5 s idle (2.5 s after a click in click-only mode); pan dead zone 12 % of the crop; zoom levels 125–300 %; animation speed 0–50 %; instant zoom-out option. Camera motion is computed from real elapsed time.

Principles: purposeful · smooth · predictable · responsive · accessible · performance-conscious. Interface motion uses transform and opacity only; camera motion lives in the rendered video and never overshoots.

| Interaction (PROPOSED) | ms | Easing | Properties | Distance px | Scale |
|---|---|---|---|---|---|
| Hover | 120 | standard | background-color, border-color | — | — |
| Press | 80 | standard | background-color, scale | — | 0.98 |
| Focus ring | 120 | standard | opacity | — | — |
| Tab switch | 180 | standard | opacity, translateX | 8 | — |
| Sidebar collapse | 220 | standard | width (232 → 68) | 164 | — |
| Dropdown | 160 | enter | opacity, translateY | 6 | — |
| Dialog enter | 200 | enter | opacity, translateY | 8 | — |
| Dialog exit | 140 | exit | opacity | — | — |
| Toast | 220 | enter | opacity, translateY | 10 | — |
| Record start | 180 | standard | indicator opacity, scale 0.8 → 1 | — | — |
| Export progress | 220 | standard | width (determinate) | — | — |
| Smart Tracking camera | 500 | camera | crop rectangle (rendered video, not UI) | — | — |

Easing: standard `cubic-bezier(0.22, 1, 0.36, 1)`, enter `cubic-bezier(0.16, 1, 0.3, 1)`, exit `cubic-bezier(0.4, 0, 1, 1)`. Reduced motion: Replace transforms with a 0-80 ms opacity change; never remove state feedback. Keep text sharp: move whole pixels at rest; start at 1× and scale down, never up from below 1×.

## 10. Product UI design system (both themes)

Every component uses semantic tokens only, so each of them exists identically in Midnight and Arctic; the book shows every component in both (chapter 10). Geometry is EXISTING (36 px default control, 8 px radius, 4 px spacing grid). Focus: 2 px `brand.primary` ring with a 2 px offset on every focusable control.

| Component | Anatomy / size / spacing | Colour tokens & states | Keyboard / focus | Incorrect usage |
|---|---|---|---|---|
| Application shell | Title bar 44 + sidebar 232/68 + page canvas + optional inspector 340 | title bar `bg.secondary`, sidebar `bg.app`, canvas `bg.secondary` | Ctrl+B toggles sidebar | Do not float cards on the title bar |
| Sidebar / navigation item | icon 18 + label; height 36; radius 8; section overlines | default `fg.secondary`; hover `state.hover`; active `state.selected` + 3 px `brand.primary` bar + accent icon | Arrow keys, Enter; focus ring | Never colour alone for the active item |
| Page heading | Display 28/600 + 12.5 px description | `fg.primary`, `fg.secondary` | — | No decoration behind the title |
| Primary button | height 30/36/44, padding 0 16, radius 8, label 13 SemiBold | `brand.primary` / hover / pressed; label `brand.on-primary`; disabled `surface.layer2` + `fg.disabled` | Space/Enter; focus ring | One primary per view |
| Secondary / tertiary / danger | same geometry; secondary has `border.control` | `surface.layer1`; tertiary transparent; danger `status.error` | same | Do not use danger for non-destructive actions |
| Icon button | 30–36 square, icon 16–18 | tertiary states | Tooltip + accessible name | No icon-only without a tooltip |
| Text field / numeric / search | height 36, padding 12 × 8, radius 8, border 1 | fill `surface.layer2`, border `border.control`, focus border `brand.primary`, error `status.error` | Tab; Esc clears search | Placeholder is not a label |
| Slider | track 4, thumb 16 | track `surface.layer3`, value `brand.primary` | Arrow keys; Home/End | Show the numeric value |
| Switch / checkbox / radio | switch 40 × 22; checkbox 18 | off `border.control`; on `brand.primary` | Space toggles | Label always visible |
| Dropdown / popover / context menu | item padding 10 × 7, radius 8, overlay radius 10–12 | `surface.overlay`, `border.subtle`, overlay shadow; hover `state.hover`; selected `state.selected` | Arrow keys; Esc closes | No nested menus > 1 level |
| Tooltip | padding 10 × 6, radius 8, 12 px | `surface.overlay` + `border.control` | Shows on focus too | Not for essential information |
| Dialog | padding 24, radius 14, max 380–520 | `surface.overlay`; scrim `surface.scrim` | Focus trap; Esc cancels | Never stack dialogs |
| Notification / toast | padding 12 × 16, radius 12, icon 18 | `surface.overlay`, status icon colour | Polite live region where supported | Do not auto-dismiss errors |
| Status indicator / badge | height 24, pill, dot or icon + label | `rec.*`, `status.*` | — | Never colour alone |
| Progress bar | height 6, radius 3 | track `surface.layer3`, fill `rec.exporting` | — | Determinate when possible |
| Audio meter | 20 segments, 7 × 14, gap 3 | green `status.success`, amber `status.warning`, red `status.error`; empty `surface.layer3` | — | Add a text legend (existing “Reading the meters”) |
| Source selection card | thumbnail 84 + title + meta, radius 12 | selected `brand.primary` 1 px + ring | Arrow/Enter | Always show the live thumbnail |
| Device preview / live preview | 16:9 stage on `surface.layer2` | stage never themed over the video | — | Do not overlay controls on the preview |
| Recording controller | padding 6, radius 16, grip + timer · transport · toggles · show app | `surface.layer2`, `border.control`, overlay shadow, `rec.active` dot | Global shortcuts | Never appears in the recording |
| Media thumbnail | grid/list, radius 12, duration chip | `surface.layer1` | Enter opens | No personal data in demo captures |
| Timeline / trim | height 64, handles 4 px, playhead 2 px | selection `state.selected` + `brand.primary` handles | I / O set in/out; Space play | Keep the original untouched |
| Inspector panel / tabs | width 340, icon tabs 36 | tab selected `state.selected` | Ctrl+E export | One scroll area |
| Color picker / swatches | swatch 24, radius 6 | `border.control` ring on selected | Arrow keys | Show the hex |
| Empty state | icon 48 + title + description + action | `surface.layer1`, `fg.secondary` | — | Always offer the next step |
| Error state | icon + title + cause + retry | `status.error` border | — | No raw error codes |
| Loading / skeleton | bars 12 high, radius 6 | `surface.layer2` → `surface.layer3` shimmer (static if reduced motion) | — | No spinners over the preview |

Interaction and motion for every component follow section 9 (hover 120 ms, press 80 ms, dialog 200 ms, toast 220 ms). Typography follows section 5 (13 px body, 12.5 px description, 10.5 px overline).

## 11. Feature identity (EXISTING names/icons · PROPOSED colours)

| Feature | Icon | Colour (Midnight / Arctic) | Benefit | Microcopy |
|---|---|---|---|---|
| Capture | `Icon.Monitor` | `#18DCE8` / `#087F8C` | Pick exactly what gets recorded — a display or a single window. | “Choose source” |
| Smart Tracking | `Icon.Focus` | `#7C5CFF` / `#6941C6` | A camera that follows your cursor and caret, then eases back out. | “A camera that follows the action” |
| Audio | `Icon.Volume` | `#85C536` / `#4E7F0C` | See system and microphone levels before you press record. | “System audio · Microphone” |
| Webcam | `Icon.Video` | `#EA0B7F` / `#C4046A` | Put yourself in the frame with a picture-in-picture template. | “Picture-in-picture” |
| Effects | `Icon.Sparkles` | `#FFAA21` / `#A15F00` | Spotlight, click ripples and click sounds that make actions readable. | “Make every click visible” |
| Annotations | `Icon.Pen` | `#6AA8FF` / `#2567D6` | Draw over your desktop while you record. | “Draw while you record” |
| Review & Export | `Icon.Film` | `#18DCE8` / `#087F8C` | Trim, compose and deliver an MP4 or GIF without leaving the app. | “Original recording preserved” |
| Recordings | `Icon.Film` | `#93A8B9` / `#475569` | Find any recording by name, date or thumbnail. | “Search recordings” |
| Settings | `Icon.Settings` | `#93A8B9` / `#475569` | Preferences, theme and update behaviour. | “Settings” |

Colours come from the logo hues plus brand cyan and violet; they tint icon chips (16 % / 12 % alpha) and small markers only, never labels, and stay subordinate to the master brand. Screenshots: `Cap-IT-Brand-Assets/screenshots/{dark,light}/`.

## 12. Smart Tracking — signature identity

Interaction → Focus → Smooth reframing → Clear communication. The camera follows the cursor, or the text caret while typing; modes: cursor & typing, or clicks only; levels 125–300 %; instant zoom-out; speed 0–50 %; keystroke overlay. Microcopy (EXISTING): “A camera that follows the action: it glides toward your cursor and caret, then eases back out.” Violet is its accent colour (PROPOSED).

**What is and is not verified.** The recording-continuity work is documented in `docs/SMART-TRACKING-RECORDING-FIX.md` (one machine, AMD integrated GPU, 1080p). Still open there: a literal “cut section” was not reproduced; the audio tail can end up to ~0.8 s short; text sharpness while zoomed is not measured; 1440p, 4K, NVENC and QSV are untested. This brand work makes no performance claim beyond that report.

## 13. Product screens

| Screen | Midnight (real render) | Arctic (real render) |
|---|---|---|
| annotations | `screenshots/dark/annotations.png` | `screenshots/light/annotations.png` |
| audio | `screenshots/dark/audio.png` | `screenshots/light/audio.png` |
| capture | `screenshots/dark/capture.png` | `screenshots/light/capture.png` |
| effects | `screenshots/dark/effects.png` | `screenshots/light/effects.png` |
| home | `screenshots/dark/home.png` | `screenshots/light/home.png` |
| recordings | `screenshots/dark/recordings.png` | `screenshots/light/recordings.png` |
| review-background | `screenshots/dark/review-background.png` | `screenshots/light/review-background.png` |
| review-canvas | `screenshots/dark/review-canvas.png` | `screenshots/light/review-canvas.png` |
| review-export | `screenshots/dark/review-export.png` | `screenshots/light/review-export.png` |
| review-frame | `screenshots/dark/review-frame.png` | `screenshots/light/review-frame.png` |
| review-style | `screenshots/dark/review-style.png` | `screenshots/light/review-style.png` |
| review-text | `screenshots/dark/review-text.png` | `screenshots/light/review-text.png` |
| review-video | `screenshots/dark/review-video.png` | `screenshots/light/review-video.png` |
| review-watermark | `screenshots/dark/review-watermark.png` | `screenshots/light/review-watermark.png` |
| settings | `screenshots/dark/settings.png` | `screenshots/light/settings.png` |
| smart-tracking | `screenshots/dark/smart-tracking.png` | `screenshots/light/smart-tracking.png` |
| webcam | `screenshots/dark/webcam.png` | `screenshots/light/webcam.png` |

Rendered by the application’s own snapshot harness (Debug build, `CAPIT_UI_SNAPSHOT_DIR`, both themes, 1500 × 840 pages and 1280 × 720 editor) with a clean demo configuration (empty library, default settings). The harness was run from a scratch copy of the sources so that no production file changed.

## 14. Marketing identity

| Asset | Dimensions | Template (both themes) |
|---|---|---|
| GitHub social preview | 1280 × 640 | github-social-preview |
| YouTube thumbnail | 1280 × 720 | youtube-thumbnail |
| YouTube banner | 2560 × 1440 (safe 1546 × 423) | youtube-banner |
| LinkedIn | 1200 × 627 | linkedin-post |
| X / Twitter | 1600 × 900 | x-post |
| Instagram portrait | 1080 × 1350 | instagram-post |
| Product Hunt gallery | 1270 × 760 | product-hunt-gallery |
| Email header | 1200 × 400 (@2×) | email-header |
| Release announcement | 1920 × 1080 | release-announcement |
| Tutorial title card | 1920 × 1080 | tutorial-title-card |
| Video outro | 1920 × 1080 | video-outro |
| Desktop wallpaper | 3840 × 2160 | desktop-wallpaper |
| Download banner | 1600 × 400 | download-banner |

Platform dimensions are common guidance at the time of writing; confirm before publishing. Templates are CONCEPT compositions built from the proposed tokens, the existing logo and real screenshots.

## 15. Voice and messaging

Clear · confident · helpful · precise · modern · human · professional. Avoid hype, unsupported claims (“fastest”, “zero lag”), buzzwords, robotic phrasing and unexplained jargon. Verbs first, sentence case, one idea per line, the real feature name, a next step.

| Where | Preferred | Avoid |
|---|---|---|
| Homepage headline | Capture with intention. | The most powerful screen recorder ever built. |
| Short description | A focused, GPU-assisted screen recorder for Windows. | Revolutionary AI-powered capture solution. |
| Tooltip | Freeze the screen image — audio and timer keep recording. | Click to freeze! |
| Settings help | Hardware encoders need a compatible GPU. If a hardware export fails, choose Software. | Hardware encoding may not work for some users. |
| Error | Couldn’t open this video. Check that the file still exists, then try again. | Error 0x80004005. Operation failed. |
| Export complete | Export complete. Open folder | Success!!! |
| Update | Cap-IT 3.13.1 is available. Update now installs it and reopens Cap-IT. | A new version is ready! Don’t miss out! |
| Release note | Smoother Smart Tracking motion and sharper live preview scaling. | Various improvements and bug fixes. |
| Support reply | Thanks — could you share your Windows version, the capture target and a short description of what happened? | Please provide more information. |

## 16. Brand applications

Desktop app window (both themes) · Windows installer (Inno Setup modern wizard; the script uses only `SetupIconFile` today, side artwork is a concept) · Start Menu and taskbar identity · GitHub repository graphics · website hero · documentation portal · screenshot gallery · social and launch assets · feature spotlights · tutorial title card · video outro · export watermark (suggested default: bottom-right, 4 % of frame height, 55 % opacity plate). All are CONCEPT compositions in the book and PNG templates in the asset pack.

## 17. Technical product overview (EXISTING)

C# / .NET 8 · Avalonia UI 11 · CommunityToolkit.Mvvm · Microsoft.Extensions.DependencyInjection · DXGI Desktop Duplication · Windows Graphics Capture · Vortice.Direct3D11 / DXGI · NAudio (WASAPI) · FFmpeg · Media Foundation · GDI / GDI+ · Win32 input hooks.

Flow: Avalonia shell → view models → `RecordingManager` → `VideoCaptureService` (DXGI for displays, WGC for windows; GPU compose to NV12 when supported, CPU kernels otherwise) + `AudioCaptureService` → `FFmpegEncoderService` (named pipes) → fragmented MP4 → faststart MP4 / MKV → Review & Export (Media Foundation preview, composition renderer) → MP4 / GIF. No future architecture is proposed here.

## 18. Platform, version and distribution (EXISTING)

- Windows 10 version 2004 (build 19041) or later, 64-bit; Windows 11 recommended.
- Self-contained installer: no separate .NET runtime, Windows App SDK runtime or manual FFmpeg set-up; Start Menu entry, optional desktop shortcut; uninstall keeps recordings.
- Published through GitHub Releases; the app checks on startup and while open; **Update now** downloads the installer, waits for the app to close, installs and relaunches.
- Version comes from `Directory.Build.props` (3.13.1 at the time of writing) — never hard-code “latest” in artwork. Version badge: outlined uppercase pill, 13 px, always a text label.
- Download button: 44 px, 8 px radius, 14 px SemiBold label; version and OS in muted text beneath.

## 19. Accessibility and consistency

| Area | Requirement | Status |
|---|---|---|
| Text contrast | Normal ≥ 4.5 : 1, large ≥ 3 : 1 | Verified for proposed tokens (build gate) |
| Control boundaries | ≥ 3 : 1 (SC 1.4.11) | `border.control` passes; shipped borders do not |
| Keyboard focus | 2 px ring, 2 px offset | Specified; focus visuals not audited in the app |
| Colour independence | Icon or label with every status | Specified |
| Reduced motion | Remove travel/scale, keep feedback | Specified; app support not verified |
| Text scaling | No fixed-height text containers; allow 30–40 % growth | Specified; not tested |
| Screen readers | Name icon-only buttons (AutomationProperties.Name) where supported | Not verified |
| High-DPI | Device-independent layout; strokes scale | Renders verified at 125 % |
| Theme contrast | Both themes pass independently | Proposed: yes · Shipped: see audit |
| Touch targets | Desktop app 30–44 px; use Large (44) for touch | Informational |

Quality checklist: run the snapshot harness in both themes; review focus with the keyboard only; re-run `build_tokens.py` after any colour change; check logo proportions and clear space; confirm every screenshot is a real render or labelled CONCEPT.

## 20. Brand governance

- **Owner:** the repository maintainer; contributors propose, the owner approves.
- **Versioning:** semantic — patch = fixes, minor = additions, major = identity change. This edition is 1.0 against product v3.13.1.
- **Approved logos:** existing stacked lockup and symbol; derived reversed and monochrome; proposed horizontal lockup and app-icon plates (pending approval). Never edit in place; new variant = new file name `capit-<thing>-<variant>-<size>`.
- **Tokens:** shipped values live in `Styles/*.axaml`; proposed values in the two theme JSON files, regenerated by `brand/source/build_tokens.py`, which fails if contrast regresses.
- **Screenshots:** regenerate with the snapshot harness (both themes, demo configuration) whenever a page changes; archive the previous version folder.
- **Release artwork:** use the templates; headline = product + version; one real screenshot.
- **Review process:** propose → measure → review (brand work separate from recording-pipeline commits) → publish with a version bump.
- **Do:** semantic tokens; both themes; label proposed/concept; keep “original recording preserved” in export flows. **Don’t:** redraw or recolour the logo; invert dark to get light; claim unverified performance or compatibility.

## Appendix A — Package inventory

| Path | Contents |
|---|---|
| `Cap-IT-Brand-Book.pdf` | Editorial brand book, 16 : 9 |
| `Cap-IT-Brand-Guidelines.md` | This document |
| `Cap-IT-Brand-Tokens.json` | Shared tokens (spacing, type, motion, gradients, features, logo hues) |
| `Cap-IT-Dark-Theme.json` / `Cap-IT-Light-Theme.json` | Shipped values + proposed Midnight / Arctic tokens with measured contrast |
| `avalonia/CapIT.Brand.Proposed.axaml` | Proposed brushes for both themes (not referenced by the app) |
| `Cap-IT-Brand-Assets/logo` | 14 PNG + AppIcon.ico (existing / derived / proposed) |
| `Cap-IT-Brand-Assets/app-icon` | 18 PNG + .ico |
| `Cap-IT-Brand-Assets/icons` | 87 SVG exported from the app’s icon set |
| `Cap-IT-Brand-Assets/graphics` | 9 SVG graphic-language devices |
| `Cap-IT-Brand-Assets/screenshots` | 34 real renders (Midnight and Arctic) |
| `Cap-IT-Brand-Assets/templates` | 26 PNG marketing templates (both themes) |
| `source/` | HTML book, Python builders, fonts (OFL), `README.md` |

## Appendix B — Rebuild

```powershell
python brand/source/build_assets.py      # logo variants, app icons, icon SVGs
python brand/source/build_tokens.py      # tokens + WCAG gate (fails on regression)
python brand/source/build_graphics.py    # graphic-language SVGs
python brand/source/build_templates.py   # marketing templates (needs Microsoft Edge)
python brand/source/build_book.py        # Cap-IT-Brand-Book.pdf
python brand/source/build_guidelines.py  # this file
```

## Appendix C — Limitations (not hidden)

- No vector logo master exists in the repository; variants are raster-derived. Request the original vector files before print production.
- The stacked lockup’s wordmark is only 2.4 % of the symbol height; the horizontal lockup is proposed because of it.
- The Midnight / Arctic palettes are recommendations; the application’s colours are unchanged.
- Not verified in the running application: reduced motion, screen-reader support, text scaling, light-theme focus visuals, non-Latin script rendering.
- Platform dimensions in section 14 are guidance at the time of writing.
- The Smart Tracking recording work is separate (see `docs/SMART-TRACKING-RECORDING-FIX.md`): audio tail, zoomed text sharpness and broader continuity tests remain a separate validation pass.
