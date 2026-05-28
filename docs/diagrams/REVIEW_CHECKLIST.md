# Diagram review checklist (agents)

[← Back to diagrams README](./README.md)

Use this checklist **before every commit** that changes `docs/diagrams/*.excalidraw` or `docs/diagrams/*.svg`.

## 1) Semantic checks

- [ ] Source/target meaning is correct (e.g., connector starts from the intended boundary/service).
- [ ] One visual meaning = one style (connector color/line style is consistent).
- [ ] Legend matches actual styles used in the diagram.
- [ ] Detail level matches diagram scope (no unnecessary implementation details).

## 2) Geometry checks

- [ ] No connector crosses through any block body.
- [ ] No connector crosses label text.
- [ ] No floating segments (all connectors anchor to source/target boundaries).
- [ ] Arrowhead direction is visually obvious at 100% zoom.
- [ ] Routed connectors use safe channels (between blocks, not through blocks).

## 3) Layout checks

- [ ] Spacing is visually balanced (rows, columns, badges, legend).
- [ ] No clipped elements near canvas edges.
- [ ] Large boundaries do not leave excessive dead space.
- [ ] Legend is readable and does not collide with content.

## 4) Update flow (required)

1. Edit `docs/diagrams/generate-diagrams.mjs`.
2. Regenerate:
   - `node docs/diagrams/generate-diagrams.mjs`
   - Render updated `docs/diagrams/*.svg`
3. Run this checklist against the generated SVG at 100% zoom.
4. Commit only after all checks pass.

