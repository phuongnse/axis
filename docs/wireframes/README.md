# Wireframe kit — architecture

> **Navigation**: [Wireframe playbook](../playbooks/wireframes.md) · [← docs/README.md](../README.md)

## Goal

**Reuse blocks** so every screen shares the same geometry, colors, and spacing — not copy-paste in `generate-screens.mjs`.

## Three layers (single source per layer)

| Layer | File | Owns | Screens use |
|-------|------|------|-------------|
| **Primitives** | `components.mjs` | Colors, `rect`/`text`, `fieldLabel`, `btn`, `appShell`, `component()` / `componentContent()` | Import helpers only |
| **Blocks** | `blocks.mjs` | Reusable chunks: external sign-in, auth fields, `authCard`, terms row | **Import and compose** — primary reuse surface |
| **Kit sections** | `generate-template.mjs` | Large patterns (table, workflow canvas, permission matrix) | `component(buildXxx, x, y)` |

`_template.excalidraw` is a **generated catalog** of kit sections + blocks (S04, S38, S39). Regenerate when blocks or template builders change; it is not edited by hand.

## Workflow

```bash
# 1. Change primitives or blocks
#    components.mjs  — fieldLabel, REQUIRED_MARKER_GAP, colors
#    blocks.mjs      — auth SSO row, authFormField, authCard, …

# 2. Refresh catalog (optional but recommended when blocks change)
node docs/wireframes/generate-template.mjs

# 3. Refresh screens (always)
node docs/wireframes/generate-screens.mjs
node docs/wireframes/generate-screens.mjs   # second run — diff must be empty

# Optional: one use-case folder only (e.g. register-org + email-confirmation)
SCREEN_FILTER=register-org node docs/wireframes/generate-screens.mjs
SCREEN_FILTER=email-confirmation node docs/wireframes/generate-screens.mjs

# 4. SVG previews for changed .excalidraw
docs/scripts/generate-wireframes.ps1 -Filter register-org   # example
```

## Rules

1. **New reusable UI** → add to `blocks.mjs` (or a new `buildXxx` in `generate-template.mjs` if it is a full kit section).
2. **Screens** → layout + `placeAuthExternalSignIn()` / `authFormField()` / `component(buildTable, …)` only — no duplicate SSO icons or auth field geometry.
3. **`component()`** — only for builders that start with `sectionHeader` (2 elements stripped).
4. **`componentContent()`** — for headerless blocks from `blocks.mjs` (e.g. external sign-in). Never use `component()` on those.
5. **`_template`** — commit when S-section inventory or block catalog changes; skip on screen-only layout tweaks if blocks unchanged.

## Block inventory (`blocks.mjs`)

| Block | Use on |
|-------|--------|
| `placeAuthExternalSignIn` | register-org (and any auth screen with SSO) |
| `buildAuthCardHeader` / `buildAuthCardFooter` / `buildAuthSubmitButton` | Custom auth cards (register-org composite) |
| `authFormField`, `authReadOnlyValueField`, `authSlugPreviewField`, `authTermsRow` | register-org, states screens |
| `paintRegisterOrgEntryFields`, `paintRegisterOrgCompleteFields` | register-org happy path + `*-states` panels |
| `buildAuthCardBrandBar` | email-confirmation, provider error cards |
| `authCard` | login, register, forgot/change password, accept invite |

Constants: `AUTH_CARD_W`, `AUTH_EXTERNAL_SIGN_IN_BLOCK_H`, `AUTH_HEADER_H`, … — import from `blocks.mjs`, do not hardcode `440` on screens.
