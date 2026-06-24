# Axis Open Design Usage

Use this package to produce design-source artifacts for Axis. It is not a
runtime dependency for the SPA or backend.

## Workflow

1. Read the owning use-case acceptance criteria before asking Open Design to
   generate a screen.
2. Use `DESIGN.md` as the active design system.
3. Ask for one screen, one state board, or one component set at a time.
4. Review the artifact manually before implementation.
5. Link approved screen sources from the owning use-case `## Design Sources`
   table.
6. Implement production UI through Axis tokens, primitives, component tests, and
   consumer contracts.

## Boundaries

- Generated HTML, CSS, screenshots, and exports are design artifacts only.
- Do not paste generated code into `frontend/src`.
- Do not add Open Design CLI, MCP, model-router, or secret-bearing setup to this
  repo without a separate Design Gate and explicit approval.
- Do not let a design artifact invent product behavior that is not present in
  the owning use-case spec.
