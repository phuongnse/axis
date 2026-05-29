# Visual artifact checklist (agents)

> **Navigation**: [← docs/README.md](../README.md) · [← CLAUDE.md](../../CLAUDE.md)

Use this checklist **before every commit** that changes visual artifacts:

- `docs/diagrams/**/*.excalidraw`
- `docs/diagrams/**/*.svg`
- `docs/wireframes/**/*.excalidraw`
- `docs/wireframes/**/*.svg`
- `docs/use-cases/**/*.excalidraw`
- `docs/use-cases/**/*.svg`

## 1) Semantic checks

- [ ] Source/target meaning is correct (connector starts from intended boundary/service/actor).
- [ ] One visual meaning = one style (consistent connector color/line style/arrowhead usage).
- [ ] Legend (if present) matches actual styles used in the file.
- [ ] Detail level matches artifact scope (system-context vs container vs module vs screen/wireframe).

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

1. Edit source generators or source `.excalidraw` files.
2. Regenerate related `.svg` previews.
3. Review generated results at 100% zoom with this checklist.
4. Commit only after all checks pass.

## 5) Use-case `README.md` sync (when `docs/use-cases/**` changes)

- [ ] Every **screen** `.excalidraw` in the use-case folder has a row in `## Wireframes` (error `*-states` included).
- [ ] No **diagram** `.excalidraw` (e.g. `*-flow`, `*-model`) listed under `## Wireframes` — only under `## Diagrams`.
- [ ] `## Diagrams` rows are **only** files in this folder; other use cases linked in `**Related:**` prose, not duplicated in the table.
- [ ] When >3 screens or branched flow: `## Screen flow` present and **row order** matches wireframes table ([docs-style § Use-case visual artifacts](./docs-style.md#use-case-files--wireframes--implementation-status), example [register-org](../use-cases/platform-foundation/register-org/README.md)).

