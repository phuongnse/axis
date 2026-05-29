# Wireframe Playbook — Component Kit & Screen Wireframes

> **Navigation**: [← docs/README.md](../README.md) · [← CLAUDE.md](../../CLAUDE.md)

This playbook covers the wireframe generation system.

- **Architecture & file map:** [`docs/wireframes/README.md`](../wireframes/README.md)
- **Agents — spacing, blocks, MUST/MUST NOT:** [README § Agent contract](../wireframes/README.md#agent-contract)

---

## Agent checklist

When you touch `docs/wireframes/**` or use-case `*.excalidraw`:

- [ ] Read [Agent contract](../wireframes/README.md#agent-contract) — auth fields use `authFormField` / `blocks.mjs`, not manual `y + 18`.
- [ ] Label/help/input spacing changed only in `fieldLabelBlock` (`components.mjs`); field stack gap only in `AUTH_FIELD_STACK_GAP` (`blocks.mjs`).
- [ ] No new hardcoded px for auth layout (`6`, `8`, `12`, `440`, …).
- [ ] SSO via `placeAuthExternalSignIn()` — not `component()` on headerless blocks.
- [ ] Auth brand: `buildAxisLogo` / `buildAuthCardHeader` — not `⬡  Axis` text; pass `files` to `write()`.
- [ ] `node docs/wireframes/generate-screens.mjs` twice → empty `git diff` on second run.
- [ ] Regenerated `.svg` for changed screens; [visual-artifact-checklist](./visual-artifact-checklist.md) satisfied.
- [ ] Use-case `README.md` wireframes table / screen flow updated if screens added or renamed.

**Reference screen:** [register-org](../use-cases/platform-foundation/register-org/README.md) (full block kit usage).

---

| Layer | File |
|-------|------|
| Primitives | `components.mjs` |
| **Reusable blocks** | **`blocks.mjs`** ← auth / register-org; compose here first |
| Kit sections | `generate-template.mjs` (S01–S37 builders, import-only) |
| Screen outputs | `generate-screens.mjs` → `docs/use-cases/.../*.excalidraw` |
| Shared shell | `docs/wireframes/app-shell.excalidraw` (+ `.svg`) |

> **Required with every wireframe/diagram edit:** run
> [`visual-artifact-checklist.md`](./visual-artifact-checklist.md)
> before commit.

---

## File map

| File | Purpose |
|---|---|
| `docs/wireframes/components.mjs` | Primitives, colors, `component()` / `componentContent()` |
| `docs/wireframes/blocks.mjs` | **Reusable blocks** (auth, SSO, fields) — screens import this first |
| `docs/wireframes/generate-template.mjs` | Kit section builders S01–S37 (tables, canvas, modals, …) |
| `docs/wireframes/generate-screens.mjs` | Screen layout — `blocks.mjs` + `component(buildXxx)` |
| `docs/wireframes/README.md` | Kit layers, workflow, block inventory |
| `docs/wireframes/app-shell.excalidraw` | Shared app chrome (generated; commit with screens when shell changes) |
| `docs/use-cases/{domain}/{use-case}/*.excalidraw` | Generated outputs of `generate-screens.mjs` — co-located with each use case |

**Regeneration commands:**

```powershell
# Regenerate screen wireframes (run twice; git diff must be empty on second run)
node docs/wireframes/generate-screens.mjs
node docs/wireframes/generate-screens.mjs
docs/scripts/generate-wireframes.ps1

# One use-case folder only (optional)
$env:SCREEN_FILTER = "register-org"
node docs/wireframes/generate-screens.mjs
docs/scripts/generate-wireframes.ps1 -Filter register-org
```

---

## Architecture: shared component library

`components.mjs` is the foundation. Nothing in the wireframe system defines primitives or colors anywhere else.

### Deterministic seed rule (non-negotiable)

All generated wireframes must use deterministic seeds per screen (`setSeed(deterministicSeedForScreen(screenKey))` in `generate-screens.mjs` before each screen generator runs).

- Why: Excalidraw roughness depends on seed; order-dependent global seeds cause unrelated files to churn when a new screen is inserted.
- Guarantee: adding/changing one screen does not rewrite untouched screens in other domains.
- Implementation: keep `runScreen(screenKey, generator)` wrapper in `generate-screens.mjs` and call every `genXxx()` through it.
- **Pre-commit check:** run `node docs/wireframes/generate-screens.mjs` twice; `git diff` must be empty after the second run (proves seeds are stable).

### Auth outcome cards — `stateHeadline` (platform-foundation email flows)

Use one layout for informational / error states (`email-confirmation`, `verify-email` grid cards, `verify-email-rate-limit`):

- **Row:** semantic-colored icon + title (`stateHeadline` in `components.mjs`)
- **Accent:** short underline (64px) in `semanticVariantBorder` — not a full-width bar or alert box
- **Body:** `C.gray700`; emphasize one line with `semanticVariantColor` (email address, countdown)
- **Grid screens:** colored state label above each card (`Expired link`, etc.) — do not add circular icon badges

Shared spacing constants in `generate-screens.mjs`: `AUTH_CARD_PAD`, `AUTH_SHELL_H`, `AUTH_HEADLINE_H`, `AUTH_BODY_GAP`.

### What lives in `components.mjs`

| Export | Type | Description |
|---|---|---|
| `rect`, `ellipse`, `text`, `hline`, `vline`, `arrow`, `sectionHeader` | functions | All primitive Excalidraw element builders |
| `C` | object | Industrial Calm color palette |
| `SB`, `HDR`, `CX`, `CY` | constants | Layout: sidebar 230px, header 60px |
| `translate(els, dx, dy)` | function | Shift all elements by (dx, dy) |
| `component(builderFn, x, y, contentDy?)` | function | Place a full template section (strips 2-line section header) |
| `componentContent(builderFn, x, y, contentDy?)` | function | Place a headerless block (e.g. `buildAuthExternalSignInBlock`) — do not use `component()` |
| `appShell(prefix, W, H, navItems, activeIdx, pageTitle)` | function | Parameterized app shell (matches S18 exactly) |
| `writeExcalidraw(filePath, elements)` | function | Write `.excalidraw` JSON to disk |
| `btn`, `inputField`, `selectField`, `badge`, `searchBar`, `pageHeader` | functions | Convenience UI builders with canonical dimensions |
| `setSeed`, `semanticVariantColor`, `semanticVariantBorder` | functions | Per-screen seed reset; semantic text/border colors |
| `stateHeadline` | function | Icon + title + short underline for auth outcome cards |

### Import pattern

**`generate-screens.mjs`:**
```js
import {
  C, SB, HDR, CX, CY,
  rect, text, appShell, component, writeExcalidraw,
} from './components.mjs';

import {
  placeAuthExternalSignIn,
  buildAuthCardHeader,
  authFormField,
  authCard,
} from './blocks.mjs';

import {
  buildWorkflowCanvas,
  buildTable,
  // ...kit sections as needed
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
- Use `component()` for full kit sections (with section header); use `componentContent()` for shared blocks like external sign-in — never recreate either from scratch
- If a builder uses `contentDy = 68` (sub-label sections like S04, S16, S19, S22, S23, S25, S26, S27), pass `contentDy = 68` explicitly
- ID collisions: each builder type must appear at most once per screen file (IDs are fixed in builders); if you need two copies, create a variant builder with a distinct prefix

---

## The `appShell()` helper

`appShell(prefix, W, H, navItems, activeIdx, pageTitle)` renders the full app frame matching S18 exactly — sidebar, logo, nav, header strip. Use it on every authenticated screen.

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `prefix` | string | ID prefix (e.g. `'dm'`) — must be unique per screen |
| `W` | number | Total screen width (use `1200`) |
| `H` | number | Total screen height (use `700`) |
| `navItems` | string[] | Nav label strings |
| `activeIdx` | number | 0-based index of the active nav item |
| `pageTitle` | string | Title shown in the header bar |

**Standard values:**
```js
const W   = 1200;
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

### `fieldLabel(prefix, x, y, label, { required?, color? })`

Form label row (11px, default `C.gray500`). When `required: true`, appends a red `*` in `C.danger` after the label with a fixed `REQUIRED_MARKER_GAP` (10px) from `components.mjs` — same offset on every field. **Change the gap in `components.mjs` only**, then regen affected screens.

### `fieldLabelBlock(prefix, x, y, innerW, label, { required?, helpText?, color? })`

Label row (and `*` when required), then optional **help row**: small `?` (12px circle, `C.primaryDark` on `C.infoBg`) **immediately before** the help copy (10px, `C.gray700`). Returns `{ els, labelBlockH, inputY }`. Prefer `authFormField(…, helpText)` on auth screens.

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

## Screen constants

All authenticated screens use these values. **Never change W or H without re-verifying every screen.**

```js
const W   = 1200;  // total screen width — must be ≥ 1150 (cx=250 + template content=900)
const H   = 700;   // total screen height
const PAD = 20;    // content area padding

const cx  = CX + PAD;          // 250 — first content element x (after sidebar + padding)
const cy  = CY + PAD;          // 80  — first content element y (after header + padding)
const cw  = W - CX - PAD * 2;  // 930 — usable content width
```

---

## Spacing formulas — pixel-exact rules

These formulas are the source of truth for all layout math in `generate-screens.mjs`. Always derive positions from them — never guess.

### Table layout

```text
tblY = cy + 56    // standard: toolbar(40px) + gap(16px)
tblY = cy + 82    // with breadcrumb: breadcrumb(18) + gap(8) + toolbar(40) + gap(16)
tblH = H - tblY - PAD
```

| Variant | When to use |
|---|---|
| `cy + 56` | Toolbar only (search bar, filters, action buttons in one row) |
| `cy + 82` | Toolbar preceded by a breadcrumb row |

Table internals (from S10, canonical):

| Element | Height | Content y-offset |
|---|---|---|
| Header row | **44px** `C.gray100` bg | `tblY + 12` |
| Data row | **50px** | `tblY + 44 + i × 50 + 15` |
| Badge in row | — | row `y + 11` (centers 28px badge in 50px row) |
| Button in row | — | row `y + 7` (centers 36px btn in 50px row) |

### Inline form row (label left, control right)

A "settings row" places a text label on the left and a control (input, checkbox, toggle) on the right. The label must be **vertically centered** with the control:

```text
labelY = controlY + (controlH - 16) / 2
```

| Control type | controlH | labelY formula | Example |
|---|---|---|---|
| `inputField` | 40px | `controlY + 12` | `text('lbl', cx, y+12, ...); inputField('inp', cx+320, y, ...)` |
| Toggle (`rect` h=24) | 24px | `controlY + 4` | `text('lbl', cx, y+4, ...);  rect('tog', cx+320, y, 44, 24, ...)` |
| Checkbox (`rect` h=20) | 20px | `controlY + 2` | `text('lbl', cx, y+2, ...);  rect('chk', cx+320, y, 20, 20, ...)` |

### Section header spacing (settings screens)

```text
title   at  ySection          (h=22, fontSize=16)
divider at  ySection + 26     (title h=22 + gap 4)
first row at ySection + 44    (divider + gap 18)
```

Subsequent sections start after the last control of the previous section, plus a **32px** inter-section gap.

---

## Auth screen pattern (`authCard`)

All standalone auth screens (no sidebar) use `authCard()` in `generate-screens.mjs`. **Never build auth cards by hand.**

```js
authCard(prefix, { title, subtitle?, items, extraLink? }, submitLabel, footerText)
```

| Parameter | Type | Description |
|---|---|---|
| `prefix` | string | ID prefix — must be unique per screen |
| `title` | string | Heading inside the card |
| `subtitle` | string \| null | Secondary line below the title (optional) |
| `items` | `{ label, placeholder, required? }[]` | Form fields — each is 72px tall; set `required: true` for a red `*` (`C.danger`) after the label |
| `extraLink` | string \| null | Right-aligned link above the submit button (e.g. `'Forgot password?'`) |
| `submitLabel` | string | Text on the full-width primary button |
| `footerText` | string | Centered link in the card footer |

**Card geometry (auto-calculated — do not override):**

```text
cardW   = 440
headerH = subtitle ? 136 : 112    // space from card top to first field
fieldH  = items.length × 72       // 72px per field: label(16) + gap(2) + input(40) + gap(14)
cardH   = headerH + fieldH + (extraLink ? 22 : 4) + 36 + 12 + 32
cardX   = Math.round((W - cardW) / 2)
cardY   = Math.round((H - cardH) / 2)
```

**Logo rule**: always `text(id, cardX, cardY+16, cardW, 28, '⬡  Axis', 18, C.primary, 'center')` — bounding box must span full `cardW` so `'center'` alignment works correctly. Never use a narrower bounding box.

**External sign-in:** import from `blocks.mjs` only:

```js
import { placeAuthExternalSignIn, AUTH_CARD_PAD_X, AUTH_EXTERNAL_SIGN_IN_BLOCK_H } from './blocks.mjs';

contentEls.push(...placeAuthExternalSignIn(cardX + AUTH_CARD_PAD_X, y));
y += AUTH_EXTERNAL_SIGN_IN_BLOCK_H;
```

`placeAuthExternalSignIn` wraps `componentContent` + `buildAuthExternalSignInBlock` — do not call `component()` on that block.

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
   - **Authenticated screen**: start with `appShell(prefix, W, H, NAV, activeIdx, pageTitle)`
   - **Auth screen** (login, register, etc.): use `authCard()` — never build the card by hand
   - **Auth screen with SSO / many fields / help text**: compose from `blocks.mjs` (`authFormField`, `paintRegisterOrg*`, …) — see [Agent contract](../wireframes/README.md#agent-contract)
   - Use `component(buildXxx, cx, cy)` for any element that matches a template section
   - Use `btn`, `inputField`, `badge`, etc. from `components.mjs` for individual controls
   - Use raw `rect`, `text`, etc. only for layout with no template equivalent
   - **All y-positions must use the spacing formulas** in the "Spacing formulas" section above — never guess offsets
2. Call `genXxx()` in the main section at the bottom of the file
3. Add the output path to the screen inventory table in this playbook
4. Run `node docs/wireframes/generate-screens.mjs` — **verify output has no `NaN` positions** (element count must be > 0)
5. Run `docs/scripts/generate-wireframes.ps1` to regenerate SVGs
6. Update the use-case `README.md`: add a `## Wireframes` row (and `## Screen flow` when >3 screens) per [docs-style § Use-case visual artifacts](./docs-style.md#use-case-files--wireframes--implementation-status) — see [register-org](../use-cases/platform-foundation/register-org/README.md) for the full layout

**Pre-commit checks for screen wireframes:**
- [ ] [Agent checklist](#agent-checklist) (spacing contract, blocks, regen twice)
- [ ] Element count > 0 for every generated file (NaN positions = 0 renderable elements)
- [ ] All table `tblY` values use the formula (`cy+56` or `cy+82`) — not ad-hoc numbers
- [ ] App-shell inline labels: `labelY = controlY + (controlH - 16) / 2` — auth card fields use `authFormField`, not this formula
- [ ] No custom duplicates of template builders — use `component()` instead
- [ ] Widest element: `cx + maxElementWidth ≤ W` (no overflow past right edge)

---

## Adding a kit section (`generate-template.mjs`)

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
- Update the TOC comment at the top of `generate-template.mjs` and the section count.
- Update the "Current section inventory" table in this playbook.

`generate-template.mjs` is **import-only** (no CLI, no `.excalidraw` output). Screens pull builders via `component(buildXxx, cx, cy)`.

---

## Screen inventory

| Domain path | Files |
|---|---|
| `docs/wireframes/` | `app-shell` only (shared kit artifact) |
| `docs/use-cases/<domain>/<use-case>/` | use-case-local assets (`*.excalidraw`, `*.svg`) |

---

## Current section inventory

> **Keep this table in sync with `generate-template.mjs`** — the TOC comment at the top of that file is the authoritative source. Whenever a section is added or removed, update both places together.

| Group | Sections |
|---|---|
| Foundations | S01–S03 |
| Input & Forms | S04–S08 |
| Auth blocks | `blocks.mjs` (not numbered kit sections) |
| Data Display | S09–S14 |
| Navigation & Layout | S15–S18 |
| Feedback & Overlays | S19–S24 |
| Interaction Patterns | S25–S29 |
| Axis App Patterns | S30–S37 |
