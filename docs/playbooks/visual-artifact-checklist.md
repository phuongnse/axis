# Visual artifact checklist (agents)

> **Navigation**: [← docs/README.md](../README.md) · [← design-source.md](./design-source.md) · [← AGENTS.md](../../AGENTS.md)

Use this checklist **before every commit** that changes visual artifacts:

- `docs/wireframes/**`
- `docs/use-cases/**` `## Design Sources` source/preview rows
- committed design-source preview assets
- legacy `docs/**/*.excalidraw` and `docs/**/*.svg` wireframe assets
- `docs/README.md` and use-case `README.md` **Mermaid** blocks under `## Diagrams`, `## Screen flow`, or [Key Diagrams](../README.md#key-diagrams)

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

1. Edit the source of truth in Penpot.
2. Update the owning use-case `## Design Sources` source link.
3. Export and commit preview assets only when a stable preview is needed or an existing preview row changes.
4. Review committed previews at 100% zoom with this checklist.

**Legacy Excalidraw wireframes:**

1. Edit source `.excalidraw` files.
2. Regenerate related `.svg` previews (`python scripts/axis.py generate wireframes`).
3. Review at 100% zoom with this checklist.

**Diagrams (Mermaid in Markdown):**

1. Edit diagram content in `docs/README.md` or the owning use-case `README.md`.
2. Theme colors: only in [`docs/diagrams/mermaid_theme.py`](../diagrams/mermaid_theme.py) — run `python scripts/axis.py docs sync-mermaid-theme` after changing `MERMAID_INIT`.
3. Preview on GitHub or in the IDE; see [mermaid.md](./mermaid.md).
4. Commit `.md` only — no `.excalidraw` / `.svg` for sequence or entity diagrams.

## 5) Use-case `README.md` sync (when `docs/use-cases/**` changes)

- [ ] Every documented **screen** has a row in `## Design Sources` (error `*-states` included).
- [ ] New/updated rows use `Source` + `Preview`; legacy rows with `Excalidraw` are accepted only until that use case is refreshed.
- [ ] No sequence/entity diagram files (`*-flow`, `*-model`, `*-cases`) in the folder — those belong in `## Diagrams` as **Mermaid** in the README.
- [ ] `## Diagrams` uses `### <slug>` + fenced `mermaid` blocks; other use cases linked in `**Related:**` prose only.
- [ ] When >3 screens or branched flow: `## Screen flow` present and **row order** matches design sources table ([docs-style § Use-case visual artifacts](./docs-style.md#use-case-files--design-sources--implementation-status), example [register-workspace](../use-cases/platform-foundation/register-workspace/README.md)); pattern checklist [wireframes.md § Multi-screen journey](./wireframes.md#multi-screen-journey-pattern).
