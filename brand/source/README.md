# Brand source

Editable source for `Cap-IT-Brand-Book.pdf` and everything in `brand/`. Nothing here is referenced by the application.

| File | Purpose |
|---|---|
| `build_assets.py` | Logo variants (crop/recolour of `assets/Logo-CapIT.png`, nothing redrawn), app-icon plates, icon SVG export from `Styles/Icons.axaml`, `icons.json` |
| `build_tokens.py` | Parses the shipped colours from `Styles/Colors.axaml`/`Tokens.axaml`, defines the proposed Midnight/Arctic tokens, **fails if any claimed pair misses WCAG 2.2 AA**, writes the three JSON files and `avalonia/CapIT.Brand.Proposed.axaml` |
| `contrast.py` | WCAG relative-luminance maths shared by the builders and QA |
| `build_graphics.py` | Graphic-language SVGs |
| `build_templates.py` | Marketing/social/install templates (PNG, both themes) via headless Microsoft Edge |
| `lib.py`, `pages_a.py` … `pages_d.py`, `build_book.py` | The book: theme CSS is generated from the token JSON; `book.html` is the generated, editable HTML; the PDF is printed with headless Edge |
| `build_guidelines.py` | `Cap-IT-Brand-Guidelines.md` (tables rendered from the JSON) |
| `qa_checks.py` | Layout-overflow probe, reference/link/font checks, token ↔ PDF ↔ Markdown agreement, independent contrast recomputation, optional worktree hash check |
| `render_pages.py`, `contact.py` | Render PDF pages to PNG / contact sheets for visual review |
| `fonts/` | Inter (SIL OFL 1.1) and Cascadia Mono (SIL OFL 1.1). Only these two are bundled |

## Rebuild

Requires Python 3.11+ with `Pillow`, `numpy`, `PyMuPDF`, `fontTools`, and Microsoft Edge.

```powershell
python brand/source/build_assets.py
python brand/source/build_tokens.py
python brand/source/build_graphics.py
python brand/source/build_templates.py
python brand/source/build_book.py
python brand/source/build_guidelines.py
python brand/source/qa_checks.py          # add a sha256 snapshot path to also verify the worktree
```

## Screenshots

`Cap-IT-Brand-Assets/screenshots/` are real renders from the application's own snapshot harness (`CAPIT_UI_SNAPSHOT_DIR`, Debug builds only), both themes, with a clean demo configuration (`CAPIT_SETTINGS_DIR`, empty library). They were produced from a **scratch copy** of the sources whose only changes were the single-instance mutex name (so a running copy of the app is left alone) and the UI-state path honouring `CAPIT_SETTINGS_DIR`; no file in `src/` was modified. To regenerate, repeat that approach, or close the running app and run the Debug build with the same environment variables.

## Licences

Inter © The Inter Project Authors, SIL Open Font License 1.1 (<https://openfontlicense.org>). Cascadia Mono © Microsoft Corporation, SIL Open Font License 1.1. The licence text is embedded in each font's name table.
