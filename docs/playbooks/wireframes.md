# Wireframe Playbook — Component Kit & Screen Wireframes

> **Navigation**: [← docs/README.md](../README.md) · [← CLAUDE.md](../../CLAUDE.md)

This playbook covers everything needed to work with the wireframe generation system:
`components.mjs` (shared library), `generate-template.mjs` (component kit), and `generate-screens.mjs` (screen wireframes).

---

## File map

| File | Purpose |
|---|---|
| `docs/wireframes/components.mjs` | **Single source of truth** — primitives, colors, layout constants, helpers |
| `docs/wireframes/generate-template.mjs` | 34-section component kit — imports from `components.mjs`, exports all builders |
| `docs/wireframes/generate-screens.mjs` | 15 screen wireframes — imports builders from the template, places them via `component()` |
| `docs/wireframes/_template.excalidraw` | Generated output of `generate-template.mjs` |
| `docs/wireframes/{E0N-*}/*.excalidraw` | Generated outputs of `generate-screens.mjs` |

**Regeneration commands:**

```powershell
# Regenerate the component kit
node docs/wireframes/generate-template.mjs
docs/scripts/generate-wireframes.ps1 -Filter _template

# Regenerate all screen wireframes
node docs/wireframes/generate-screens.mjs
docs/scripts/generate-wireframes.ps1
```

---

## Architecture: shared component library

`components.mjs` is the foundation. Nothing in the wireframe system defines primitives or colors anywhere else.

### What lives in `components.mjs`

| Export | Type | Description |
|---|---|---|
| `rect`, `ellipse`, `text`, `hline`, `vline`, `arrow`, `sectionHeader` | functions | All primitive Excalidraw element builders |
| `C` | object | Industrial Calm color palette |
| `SB`, `HDR`, `CX`, `CY` | constants | Layout: sidebar 230px, header 60px |
| `translate(els, dx, dy)` | function | Shift all elements by (dx, dy) |
| `component(builderFn, x, y, contentDy?)` | function | Place a template section at screen coordinates |
| `appShell(prefix, W, H, navItems, activeIdx, pageTitle)` | function | Parameterized app shell (matches S18 exactly) |
| `writeExcalidraw(filePath, elements)` | function | Write `.excalidraw` JSON to disk |
| `btn`, `inputField`, `selectField`, `badge`, `searchBar`, `pageHeader` | functions | Convenience UI builders with canonical dimensions |

### Import pattern

**`generate-template.mjs`:**
```js
import { fileURLToPath } from 'url';
import {
  nextSeed, BASE,
  rect, ellipse, text, hline, vline, arrow, sectionHeader,
  C,
  writeExcalidraw,
} from './components.mjs';
```

**`generate-screens.mjs`:**
```js
import {
  C, SB, HDR, CX, CY,
  rect, ellipse, text, hline, vline, arrow,
  btn, inputField, selectField, badge, searchBar, pageHeader,
  appShell, component, translate, writeExcalidraw,
} from './components.mjs';

import {
  buildWorkflowCanvas,
  buildBuilderLayout,
  buildExecutionTimeline,
  buildModal,
  buildSideSheet,
  buildTable,
  // ...add more as needed
} from './generate-template.mjs';
```

---

## The `component()` helper

`component(builderFn, targetX, targetY, contentDy = 48)` is the core reuse mechanism. It calls a template builder at `y0=0`, strips the 2-element section header (label + horizontal rule), then translates the content so it lands at `(targetX, targetY)`.

**How template builders are structured:**
- Section header (2 elements): label text + hline at `y0` — always stripped
- Content origin: `x=50` (left margin), `yC = y0 + contentDy` (usually `y0+48`, or `y0+68` for sub-label sections)

**Translation math:** `dx = targetX − 50`, `dy = targetY − contentDy`

**Example usage in `generate-screens.mjs`:**

```js
// Place workflow canvas starting at content area origin (cx, cy)
const canvasEls = component(buildWorkflowCanvas, cx, cy);

// Place execution timeline 40px below the content origin
const timelineEls = component(buildExecutionTimeline, cx, cy + 40);

// Place builder layout at content origin
const builderEls = component(buildBuilderLayout, cx, cy);

// Place side sheet 520px into the content area
const sideEls = component(buildSideSheet, cx + 520, cy);
```

**Hard rules:**
- Always use `component()` when a template builder matches the needed visual — never recreate it from scratch
- If a builder uses `contentDy = 68` (sub-label sections like S04, S16, S19, S22, S23, S25, S26, S27), pass `contentDy = 68` explicitly
- ID collisions: each builder type must appear at most once per screen file (IDs are fixed in builders); if you need two copies, create a variant builder with a distinct prefix

---

## The `appShell()` helper

`appShell(prefix, W, H, navItems, activeIdx, pageTitle)` renders the full app frame matching S18 exactly — sidebar, logo, nav, header strip. Use it on every authenticated screen.

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `prefix` | string | ID prefix (e.g. `'dm'`) — must be unique per screen |
| `W` | number | Total screen width (use `1100`) |
| `H` | number | Total screen height (use `700`) |
| `navItems` | string[] | Nav label strings |
| `activeIdx` | number | 0-based index of the active nav item |
| `pageTitle` | string | Title shown in the header bar |

**Standard values:**
```js
const W   = 1100;
const H   = 700;
const NAV = ['Data Models', 'Workflows', 'Forms', 'Executions', 'Settings'];
```

**Content area after appShell:**
- Content starts at `x = CX + PAD = 250`, `y = CY + PAD = 80` (where `PAD = 20`)
- Usable width: `W - CX - PAD * 2 = 830`

**Dimensions guaranteed to match S18:**
- Sidebar: `230×H`, `C.white` bg
- Logo area: `230×60`, `C.gray50` bg, `'⬡  Axis'` 18px `C.primary` at `(30, 18)`
- Nav items: `214×36`, `8px` x-inset, starting `y = 72`, spaced `44px`
- Active item: `C.infoBg` bg + `C.infoBorder` stroke + 3px left accent bar + `C.primary` text
- Header: from `x=230`, `h=60`, `C.white` bg

---

## Convenience UI builders

All canonical dimensions from the template (S03, S04, S09) — never invent sizes.

### `btn(prefix, x, y, label, variant?)`

| Variant | Stroke | Bg | Text | sw |
|---|---|---|---|---|
| `'primary'` (default) | `C.accentDark` | `C.accent` | `C.white` | 2 |
| `'secondary'` | `C.primary` | `C.infoBg` | `C.primary` | 1 |
| `'ghost'` | `C.gray300` | `C.white` | `C.gray700` | 1 |
| `'danger'` | `C.dangerDark` | `C.danger` | `C.white` | 2 |

Width auto-sized: `label.length × 8 + 32`. Height: **36px**. Text at `y+10`, 13px, centered.

### `inputField(prefix, x, y, w, placeholder?)`

Height: **40px**, `C.gray300` border, rounded. Placeholder at `y+11`, 13px, `C.gray500`.

### `selectField(prefix, x, y, w, placeholder?)`

Same as `inputField` + `▾` arrow at `x+w-22`.

### `badge(prefix, x, y, label, variant?)`

Height: **28px**, rounded. Width: `label.length × 8 + 24`. Text at `y+6`, 12px, centered.

| Variant | Stroke | Bg | Text |
|---|---|---|---|
| `'active'` (default) | `C.primaryDark` | `C.primary` | `C.white` |
| `'draft'` | `C.gray300` | `C.gray50` | `C.gray700` |
| `'success'` | `C.successBorder` | `C.successBg` | `C.success` |
| `'warning'` | `C.warningBorder` | `C.warningBg` | `C.warning` |
| `'danger'` | `C.dangerBorder` | `C.dangerBg` | `C.danger` |
| `'info'` | `C.infoBorder` | `C.infoBg` | `C.primary` |

### `searchBar(prefix, x, y, w)`

Height: **40px**. Renders input + `⌕` icon + `'Search…'` placeholder.

---

## Component dimensions — canonical reference

When writing or editing any wireframe generator, verify against these values (from S03, S04, S09, S10, S18).

| Component | Dimension | Value | Source |
|---|---|---|---|
| Sidebar width | `SB` | **230px** | S18 |
| Header height | `HDR` | **60px** | S18 |
| Sidebar bg | — | `C.white` | S18 |
| Logo area height | — | **60px**, `C.gray50` bg | S18 |
| Logo text | — | `'⬡  Axis'` 18px `C.primary` | S18 |
| Nav item | w×h | **214×36px** | S18 |
| Nav item (active) | bg/stroke | `C.infoBg` / `C.infoBorder` + 3px left bar | S18 |
| Nav item (active) text | color | `C.primary` (not white) | S18 |
| Nav item text | y offset | `y + 9`, 13px | S18 |
| Button (primary) | h, sw | **h=36, sw=2**, `C.accentDark`/`C.accent` | S03 |
| Button (ghost) | h, sw | **h=36, sw=1**, `C.gray300`/`C.white` | S03 |
| Button text | y offset | `y + 10`, 13px, centered | S03 |
| Input / Select | h | **40px**, `C.gray300` border, rounded | S04 |
| Input placeholder | y offset | `y + 11`, 13px, `C.gray500` | S04 |
| Select arrow | x | `x + w - 22` | S04 |
| Badge | h | **28px**, rounded | S09 |
| Badge width | — | `label.length × 8 + 24` | S09 |
| Badge text | y offset | `y + 6`, 12px, centered | S09 |
| Table header | h | **44px**, `C.gray100` bg | S10 |
| Table header text | y offset | `y + 12`, 13px | S10 |
| Table row | h | **50px** | S10 |
| Table row text | y offset | `y + 15`, 13px | S10 |

---

## Adding a new screen

1. Add a `genXxx()` function in `generate-screens.mjs`:
   - Start with `appShell(prefix, W, H, NAV, activeIdx, pageTitle)`
   - Use `component(buildXxx, cx, cy)` for any element that matches a template section
   - Use `btn`, `inputField`, `badge`, etc. from `components.mjs` for individual controls
   - Use raw `rect`, `text`, etc. only for screen-specific layout that has no template equivalent
2. Call `genXxx()` in the main section at the bottom of the file
3. Add the output path to the screen inventory table in this playbook
4. Run `node docs/wireframes/generate-screens.mjs` and `docs/scripts/generate-wireframes.ps1`
5. Add a `> **Wireframe**` callout to the relevant feature file

---

## Adding a section to the template (`generate-template.mjs`)

```js
export function buildXxx(y0) {
  const els = [...sectionHeader(N, 'Section Label', y0)];
  const yC = y0 + 48;  // use +68 if section has sub-labels at y0+46

  // sub-labels (only when section has distinct columns):
  els.push(text('xxx_col1_lbl', 50,  y0 + 46, 120, 14, 'Col 1', 11, C.gray500));

  // elements...
  return els;
}
```

**Rules:**
- `yC` offset: `y0 + 48` (no sub-labels) or `y0 + 68` (with sub-labels). Never mix.
- Element ID prefix: unique 3–6 char snake_case per section. Never reuse a prefix — Excalidraw silently deduplicates IDs.
- Section number `N` in `sectionHeader(N, ...)` must match visual order. Renumber all subsequent sections when inserting in the middle.
- Add `export` keyword — all builders must be exported so `generate-screens.mjs` can import them.
- Add the builder to the compose array inside the `isMain` guard at the bottom, in grouped order with `// S{NN}` comments.
- Update the TOC comment at the top of the file and the section count.
- Update the "Current section inventory" table in this playbook.

---

## `generate-template.mjs` isMain guard

The compose + write block is wrapped so the file can be imported as a module without side effects:

```js
if (process.argv[1] === fileURLToPath(import.meta.url)) {
  // compose all sections and write _template.excalidraw
}
```

This is what makes `import { buildWorkflowCanvas } from './generate-template.mjs'` safe in `generate-screens.mjs`.

---

## Screen inventory

| Module | Files |
|---|---|
| `_shared/` | `app-shell` |
| `E02-identity-access/` | `settings-users`, `settings-roles`, `settings-security`, `accept-invitation` |
| `E03-data-modeling/` | `data-models`, `data-classes`, `records` |
| `E04-workflow-builder/` | `workflows`, `workflow-editor` |
| `E05-form-builder/` | `forms`, `form-editor`, `form-submission` |
| `E06-workflow-engine/` | `executions`, `execution-detail` |

---

## Current section inventory (34 sections)

| Group | Sections |
|---|---|
| Foundations | S01–S03 |
| Input & Forms | S04–S08 |
| Data Display | S09–S14 |
| Navigation & Layout | S15–S18 |
| Feedback & Overlays | S19–S24 |
| Interaction Patterns | S25–S29 |
| Axis App Patterns | S30–S34 |
