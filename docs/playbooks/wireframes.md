# Wireframe Playbook

> **Navigation**: [docs/README.md](../README.md) | [AGENTS.md](../../AGENTS.md)

This playbook covers UI wireframe assets. Wireframes are intentionally low-fidelity: they prove layout, flow, hierarchy, content intent, and states. They do not reproduce final theme, decorative backdrop, dark/light styling, semantic color styling, shadows, or visual polish.

## Agent Checklist

When you touch `docs/wireframes/**` or use-case `*.excalidraw`:

- [ ] Read [wireframes/README.md](../wireframes/README.md#agent-contract).
- [ ] New output stays skeletal: grayscale structure only; state meaning comes from labels, copy, icons, and placement.
- [ ] Brand areas use neutral placeholders, not real logo art.
- [ ] Repeated screens in the same journey stay aligned in width, spacing rhythm, and panel order.
- [ ] Regenerated `.svg` previews with `python scripts/axis.py generate wireframes`.
- [ ] Reviewed previews with [visual-artifact-checklist.md](./visual-artifact-checklist.md).
- [ ] Updated the owning use-case `README.md` if screens were added, renamed, split, or removed.

## File Map

| File | Purpose |
|---|---|
| `docs/wireframes/app-shell.excalidraw` | Shared authenticated app shell reference |
| `docs/wireframes/app-shell.svg` | Preview rendered from `app-shell.excalidraw` |
| `docs/use-cases/{domain}/{use-case}/*.excalidraw` | Source wireframe screens |
| `docs/use-cases/{domain}/{use-case}/*.svg` | Committed previews |
| `frontend/scripts/export-wireframes.mjs` | Native Excalidraw SVG exporter |

## Regeneration Commands

```bash
# All previews.
python scripts/axis.py generate wireframes

# One use-case or path fragment.
python scripts/axis.py generate wireframes -f register-workspace

# Changed docs only.
python scripts/axis.py generate wireframes --changed
```

The generator uses the official `@excalidraw/excalidraw` export API through the frontend toolchain so committed previews match Excalidraw rendering. The repo entrypoint stays under `scripts/axis.py`; the renderer implementation is native JavaScript because Excalidraw is browser-native.

## Multi-Screen Journey Pattern

Reusable pattern for use cases with several UI steps and error variants. Product copy and requirements stay in the use-case README.

**Worked example:** [register-workspace](../use-cases/platform-foundation/register-workspace/README.md).

### Documentation

| Do | Avoid |
|----|-------|
| One folder per actor journey | A separate folder per micro-step when the user sees one continuous flow |
| `## Screen flow` when there are branches, many screens, or easy-to-confuse error screens | Listing error screens as if they were sequential steps |
| `## Wireframes` with every UI `.excalidraw` in row order | Diagram-only assets in the wireframes table |
| `## Diagrams` as Mermaid in the README | Sequence/entity `.excalidraw` files beside use cases |
| Note runtime behavior with no dedicated screen | A wireframe file the app never renders |

### Screen Assets

| Type | Purpose |
|------|---------|
| Happy-path file | One file per distinct screen the user sees on the main path |
| `*-states` file | Reference board for validation, API feedback, alternate entry, or status outcomes |
| Panel label | Short state name above each card/panel |

Split `*-states` by trigger: interactions on the same screen go on one board; a different route or entry point gets a separate board or outcome layout.

### Before Merge

- [ ] README screen flow and wireframes table updated.
- [ ] `.svg` regenerated for touched `.excalidraw`.
- [ ] No visual polish that implies final frontend styling.
- [ ] No new ad hoc docs-level utility scripts outside the owning ecosystem tooling.

## Skeleton Rules

- Use neutral fills and borders for structure.
- Keep buttons and action controls lightly filled with the neutral skeleton surface; do not use primary, success, warning, or destructive semantic styling in wireframes.
- Prefer labels over color to express state: "Expired link", "Rate limited", "Validation error".
- Keep destructive/success/warning meaning in text and layout, not color tokens.
- Keep brand/logo as neutral placeholder boxes or simple text.
- Use app-shell reference dimensions when drawing authenticated screens: sidebar, header, nav, workspace selector, content area.

## Adding A New Screen

1. Create or edit the `.excalidraw` in the owning use-case folder.
2. Keep the screen traceable to a user goal, screen-flow step, or acceptance criterion.
3. Add or update the row in the use-case `## Wireframes` table.
4. Add or update `## Screen flow` when the journey has more than three screens or branch states.
5. Run `python scripts/axis.py generate wireframes -f <use-case-or-screen-fragment>`.
6. Review the `.svg` preview at 100% zoom.

## Script Policy

Repo-level docs and wireframe utility scripts are Python-first. Native renderer tooling may live beside the owning package, such as `frontend/scripts/export-wireframes.mjs` for Excalidraw. Do not add ad hoc `.mjs`, `.js`, `.ps1`, `.sh`, `.cmd`, or `.bat` scripts under `docs/`, `docs/scripts/`, `docs/wireframes/`, or `docs/diagrams/`.
