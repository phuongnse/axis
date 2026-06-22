# Wireframes

> **Navigation**: [← Wireframe playbook](../playbooks/wireframes.md) · [← Design source](../playbooks/design-source.md) · [← docs/README.md](../README.md)

Wireframes are low-fidelity skeletons. They document layout, user flow, content intent, form/help/error states, and action hierarchy. They do **not** document product theme, dark/light styling, decorative backdrops, semantic color styling, final colors, shadows, or exact visual polish. Those belong to the design system, frontend components, screenshots, and visual QA.

## What Lives Here

This folder is the shared wireframe contract and legacy shared-asset location. New design sources live in Penpot and are linked from the owning use-case `## Wireframes` table; setup lives in [design-source.md](../playbooks/design-source.md).

| File | Role |
|------|------|
| `app-shell.excalidraw` | Legacy shared authenticated app chrome reference until migrated to Penpot |
| `app-shell.svg` | Legacy committed preview rendered from `app-shell.excalidraw` |

Use-case screen sources live beside their owning use case as source/preview rows:

```text
docs/use-cases/{domain}/{use-case}/README.md -> ## Wireframes
```

Legacy use cases may still contain:

```text
docs/use-cases/{domain}/{use-case}/{screen}.excalidraw
docs/use-cases/{domain}/{use-case}/{screen}.svg
```

When touching legacy `.excalidraw`, regenerate the matching `.svg`, then review the preview at 100% zoom with [visual-artifact-checklist.md](../playbooks/visual-artifact-checklist.md).

## Agent Contract

### Fidelity

- Use grayscale structure by default.
- Keep buttons and action controls lightly filled with the neutral skeleton surface; never use primary/destructive/success semantic styling.
- State meaning must come from labels, copy, icons, and placement, not color styling.
- Show decorative/background systems only as neutral placeholders when they affect layout.
- Do not encode real product palette, dark/light mode, final radius, shadow, texture, or decorative motif in wireframes.
- If a visual decision needs enforcement, put it in design-system/frontend component rules and tests, not wireframe screenshots.

### Layout

- Keep repeated screens in the same use-case journey visually aligned: same card widths, panel grids, section order, and spacing rhythm.
- `*-states` frames/files are reference boards for validation, API feedback, alternate entry, or status outcomes. They are not a second happy path.
- State labels belong above the relevant panel/card.
- For multi-screen journeys, keep the order of `## Wireframes` and `## Screen flow` in the use-case README aligned.
- Use the shared app-shell design source for authenticated screens; legacy reference dimensions are in `docs/wireframes/app-shell.excalidraw` until migration.

### Brand Placeholders

- Reserve brand/logo space with a neutral box or word placeholder only.
- Do not embed the real product logo, palette, gradients, or decorative brand treatment in wireframes.

### Must

- Update the owning use-case `README.md` when screens are added, renamed, split, or removed.
- Keep every screen traceable to a product requirement, screen flow step, or acceptance criterion.
- Keep source and preview links in the same row; use `N/A` only when the source or preview truly does not exist yet.
- Commit legacy `.excalidraw` and regenerated `.svg` together when touching legacy assets.

### Must Not

- Add ad hoc utility scripts or external-service dependencies for docs/wireframe generation.
- Add sequence/entity diagram files beside use cases; those belong as Mermaid blocks in the use-case README.
- Add visual polish that implies final frontend styling.
