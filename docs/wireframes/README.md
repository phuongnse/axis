# Wireframes

> **Navigation**: [Wireframe playbook](../playbooks/wireframes.md) | [docs/README.md](../README.md)

Wireframes are low-fidelity skeletons. They document layout, user flow, content intent, form/help/error states, and action hierarchy. They do **not** document product theme, dark/light styling, decorative backdrops, semantic color styling, final colors, shadows, or exact visual polish. Those belong to frontend components, design tokens, screenshots, and visual QA.

## What Lives Here

| File | Role |
|------|------|
| `app-shell.excalidraw` | Shared authenticated app chrome reference |
| `app-shell.svg` | Committed preview rendered from `app-shell.excalidraw` |

Use-case screen wireframes live beside their owning use case:

```text
docs/use-cases/{domain}/{use-case}/{screen}.excalidraw
docs/use-cases/{domain}/{use-case}/{screen}.svg
```

The `.excalidraw` file is the source. The `.svg` file is a committed preview generated through the official Excalidraw export API, so the preview matches Excalidraw output.

## Workflow

```bash
# Render all wireframe SVG previews.
python scripts/axis.py generate wireframes

# Render a subset by path substring.
python scripts/axis.py generate wireframes -f register-workspace

# Render changed wireframes and wireframes linked from changed Markdown.
python scripts/axis.py generate wireframes --changed
```

After changing any `.excalidraw`, regenerate the matching `.svg`, then review the preview at 100% zoom with [visual-artifact-checklist.md](../playbooks/visual-artifact-checklist.md).

## Agent Contract

### Fidelity

- Use grayscale structure by default.
- Keep buttons and action controls lightly filled with the neutral skeleton surface; never use primary/destructive/success semantic styling.
- State meaning must come from labels, copy, icons, and placement, not color styling.
- Show decorative/background systems only as neutral placeholders when they affect layout.
- Do not encode real product palette, dark/light mode, final radius, shadow, texture, or decorative motif in wireframes.
- If a visual decision needs enforcement, put it in frontend component rules/tests, not wireframe screenshots.

### Layout

- Keep repeated screens in the same use-case folder visually aligned: same card widths, panel grids, section order, and spacing rhythm.
- `*-states` files are reference boards for validation, API feedback, alternate entry, or status outcomes. They are not a second happy path.
- State labels belong above the relevant panel/card.
- For multi-screen journeys, keep the order of `## Wireframes` and `## Screen flow` in the use-case README aligned.
- Use `docs/wireframes/app-shell.excalidraw` as the shared app-shell reference for authenticated screens.

### Brand Placeholders

- Reserve brand/logo space with a neutral box or word placeholder only.
- Do not embed the real product logo, palette, gradients, or decorative brand treatment in wireframes.

### Must

- Commit `.excalidraw` and regenerated `.svg` together.
- Update the owning use-case `README.md` when screens are added, renamed, split, or removed.
- Keep every screen traceable to a product requirement, screen flow step, or acceptance criterion.
- Run `python scripts/axis.py generate wireframes` before review.

### Must Not

- Add ad hoc JavaScript, PowerShell, shell, or external-service scripts for docs/wireframe generation.
- Add sequence/entity diagram files beside use cases; those belong as Mermaid blocks in the use-case README.
- Add visual polish that implies final frontend styling.
