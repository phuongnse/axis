# Wireframe Playbook

> **Navigation**: [← docs/README.md](../README.md) · [← design-source.md](./design-source.md) · [← AGENTS.md](../../AGENTS.md)

This playbook covers low-fidelity UI wireframe intent and use-case visual inventories. Penpot setup, source-link ownership, and MCP agent rules live in [design-source.md](./design-source.md). Wireframes prove layout, flow, hierarchy, content intent, and states; they do not reproduce final theme, decorative backdrop, dark/light styling, semantic color styling, shadows, or visual polish.

## Agent Checklist

When you touch `docs/wireframes/**` or use-case design-source/preview links:

- [ ] Read [wireframes/README.md](../wireframes/README.md#agent-contract).
- [ ] New output stays skeletal: grayscale structure only; state meaning comes from labels, copy, icons, and placement.
- [ ] Brand areas use neutral placeholders, not real logo art.
- [ ] Repeated screens in the same journey stay aligned in width, spacing rhythm, and panel order.
- [ ] Refreshed committed previews from Penpot when previews exist.
- [ ] If touching legacy `.excalidraw`, regenerated its `.svg` preview with `python scripts/axis.py generate wireframes`.
- [ ] Reviewed previews with [visual-artifact-checklist.md](./visual-artifact-checklist.md).
- [ ] Updated the owning use-case `README.md` if screens were added, renamed, split, or removed.

## File Map

| File or link | Purpose |
|---|---|
| Penpot source link | Source of truth for a screen design or low-fidelity frame |
| `docs/use-cases/{domain}/{use-case}/README.md` | Use-case `## Design Sources` inventory |
| Committed preview link | Optional exported preview for docs review |
| `docs/wireframes/README.md` | Shared wireframe contract |
| `docs/wireframes/*.excalidraw` / `.svg` | Legacy shared wireframe assets until migrated |
| `docs/use-cases/{domain}/{use-case}/*.excalidraw` / `.svg` | Legacy use-case wireframes until migrated |
| `frontend/scripts/export-wireframes.mjs` | Legacy Excalidraw SVG exporter |

## Multi-Screen Journey Pattern

Reusable pattern for use cases with several UI steps and error variants. Product copy and requirements stay in the use-case README.

**Worked example:** [register-workspace](../use-cases/platform-foundation/register-workspace/README.md).

### Documentation

| Do | Avoid |
|----|-------|
| One folder per actor journey | A separate folder per micro-step when the user sees one continuous flow |
| `## Screen flow` when there are branches, many screens, or easy-to-confuse error screens | Listing error screens as if they were sequential steps |
| `## Design Sources` with every UI source/preview row in order | Diagram-only assets in the design sources table |
| `## Diagrams` as Mermaid in the README | Sequence/entity image/source files beside use cases |
| Note runtime behavior with no dedicated screen | A wireframe file the app never renders |

### Screen Assets

| Type | Purpose |
|------|---------|
| Happy-path frame | One frame per distinct screen the user sees on the main path |
| `*-states` frame | Reference board for validation, API feedback, alternate entry, or status outcomes |
| Panel label | Short state name above each card/panel |

Split `*-states` by trigger: interactions on the same screen go on one board; a different route or entry point gets a separate board or outcome layout.

### Before Merge

- [ ] README screen flow and design sources table updated.
- [ ] Committed previews refreshed for touched design sources when previews exist.
- [ ] Legacy `.svg` regenerated for touched `.excalidraw`.
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

1. Create or edit the design source in Penpot.
2. Keep the screen traceable to a user goal, screen-flow step, or acceptance criterion.
3. Add or update the row in the use-case `## Design Sources` table.
4. Add or update `## Screen flow` when the journey has more than three screens or branch states.
5. Export and commit a preview when review needs a stable visual snapshot.
6. Review the preview at 100% zoom when a preview exists.

## Legacy Excalidraw

Existing `.excalidraw` files remain valid historical sources until migrated. When a PR touches a legacy file, keep source and preview together:

```bash
python scripts/axis.py generate wireframes -f <use-case-or-screen-fragment>
```

Use `python scripts/axis.py generate wireframes --changed` when several changed Markdown files reference legacy previews. Do not create new Excalidraw files for design-system or high-fidelity work; create Penpot frames and link them from the owning use-case `## Design Sources` table instead.

## Script Policy

Repo-level docs and wireframe utility scripts are Python-first. Native renderer tooling may live beside the owning package for legacy assets, such as `frontend/scripts/export-wireframes.mjs` for Excalidraw. Do not add ad hoc utility scripts under `docs/`, `docs/scripts/`, `docs/wireframes/`, or `docs/diagrams/`.
