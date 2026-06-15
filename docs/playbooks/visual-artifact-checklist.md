# Visual artifact checklist (agents)

> **Navigation**: [← docs/README.md](../README.md) · [← AGENTS.md](../../AGENTS.md)

Use this checklist **before every commit** that changes visual artifacts:

- `docs/wireframes/**/*.excalidraw` and `docs/wireframes/**/*.svg`
- `docs/use-cases/**/*.excalidraw` and `docs/use-cases/**/*.svg` (**wireframe screens only** — not Mermaid in README)
- `docs/README.md` and use-case `README.md` **Mermaid** blocks under `## Diagrams` or [Key Diagrams](../README.md#key-diagrams)

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

**Wireframes (Excalidraw):**

1. Edit `docs/wireframes/*.mjs` generators or source `.excalidraw` files.
2. Regenerate related `.svg` previews (`generate-screens.mjs` + Kroki).
3. Review at 100% zoom with this checklist.

**Diagrams (Mermaid in Markdown):**

1. Edit diagram content in `docs/README.md` or the owning use-case `README.md`.
2. Theme colors: only in [`docs/diagrams/mermaid-theme.mjs`](../diagrams/mermaid-theme.mjs) — run `node docs/scripts/sync-mermaid-theme.mjs` after changing `MERMAID_INIT`.
3. Preview on GitHub or in the IDE; see [mermaid.md](./mermaid.md).
4. Commit `.md` only — no `.excalidraw` / `.svg` for sequence or entity diagrams.

## 5) Use-case `README.md` sync (when `docs/use-cases/**` changes)

- [ ] Every **screen** `.excalidraw` in the use-case folder has a row in `## Wireframes` (error `*-states` included).
- [ ] No sequence/entity diagram files (`*-flow`, `*-model`, `*-cases`) in the folder — those belong in `## Diagrams` as **Mermaid** in the README.
- [ ] `## Diagrams` uses `### <slug>` + fenced `mermaid` blocks; other use cases linked in `**Related:**` prose only.
- [ ] When >3 screens or branched flow: `## Screen flow` present and **row order** matches wireframes table ([docs-style § Use-case visual artifacts](./docs-style.md#use-case-files--wireframes--implementation-status), example [register-org](../use-cases/platform-foundation/register-org/README.md)); pattern checklist [wireframes.md § Multi-screen journey](./wireframes.md#multi-screen-journey-pattern).

