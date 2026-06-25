# Visual artifact checklist (agents)

> **Navigation**: [← docs/README.md](../README.md) · [← design-source.md](./design-source.md) · [← AGENTS.md](../../AGENTS.md)

Use this checklist **before every commit** that changes visual artifacts:

- `docs/use-cases/**` `## Design Sources` source/preview rows
- committed design-source preview assets
- `docs/README.md` and use-case `README.md` **Mermaid** blocks under `## Diagrams`, `## Screen flow`, or [Current Diagram](../README.md#current-diagram)

## 1) Semantic checks

- [ ] Source/target meaning is correct (connector starts from intended boundary/service/actor).
- [ ] One visual meaning = one style (consistent connector color/line style/arrowhead usage).
- [ ] Legend (if present) matches actual styles used in the file.
- [ ] Detail level matches artifact scope (system-context vs container vs module vs screen/design source).
- [ ] Screen source is traceable to a use-case AC, screen-flow step, or documented state.

## 2) Geometry checks

- [ ] No connector crosses through block bodies.
- [ ] No connector crosses text labels.
- [ ] No floating segments (all connectors anchor to source/target boundaries).
- [ ] Arrow direction is obvious at 100% zoom.
- [ ] Routed connectors use safe channels (between blocks, not through blocks).

## 3) Layout checks

- [ ] Spacing is balanced (rows, columns, badges, legend, callouts).
- [ ] No clipped elements near canvas edges.
- [ ] Boundaries do not leave excessive dead space.
- [ ] Legend/callouts are readable and do not collide with content.

## 4) Update flow (required)

**Design sources:**

1. Edit the editable source of truth.
2. Update the owning use-case `## Design Sources` source link.
3. Export and commit preview assets only when a stable preview is needed or an existing preview row changes.
4. Review committed previews at 100% zoom with this checklist.

**Diagrams (Mermaid in Markdown):**

1. Edit diagram content in `docs/README.md` or the owning use-case `README.md`.
2. Theme colors: only in [`docs/diagrams/mermaid_theme.py`](../diagrams/mermaid_theme.py) — run `python scripts/axis.py docs sync-mermaid-theme` after changing `MERMAID_INIT`.
3. Preview on GitHub or in the IDE; see [mermaid.md](./mermaid.md).
4. Commit `.md` only for sequence or entity diagrams.

## 5) Use-case `README.md` sync (when `docs/use-cases/**` changes)

- [ ] Every documented **screen** has a row in `## Design Sources` (error `*-states` included).
- [ ] Rows use `Source` + `Preview`; the `Source` cell links to an editable design source.
- [ ] No sequence/entity diagram files (`*-flow`, `*-model`, `*-cases`) in the folder — those belong in `## Diagrams` as **Mermaid** in the README.
- [ ] `## Diagrams` uses `### <slug>` + fenced `mermaid` blocks; other use cases linked in `**Related:**` prose only.
- [ ] When >3 screens or a branched flow exists: `## Screen flow` is present and row order matches the design sources table.
