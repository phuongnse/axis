# Design Source Playbook

> **Navigation**: [docs/README.md](../README.md) | [wireframes.md](./wireframes.md) | [AGENTS.md](../../AGENTS.md)

This playbook owns the Axis design-source workflow. Axis uses a repo-owned Open
Design package for shared design-system direction, while use-case screen sources
remain traceable to their owning specs. Design sources must be editable,
reviewable, and traceable to the owning use-case spec. Axis implementation
follows the use-case specs first: design clarifies layout and handoff, but it
does not invent behavior, acceptance criteria, endpoints, roles, or data
contracts.

## Decisions

| Topic | Rule |
|---|---|
| Design tool | Open Design is the approved shared design-source workflow for Axis design-system direction. A use-case design source may still be an editable local artifact, a tool-native workspace link, or a legacy `.excalidraw` source while that legacy asset remains in use. |
| Runtime boundary | Design tools are external design infrastructure, not Axis app dependencies. Adding a required shared service, agent bridge, repo command, package dependency, or secret-bearing integration requires explicit approval and a Design Gate-backed `TECH_STACK.md` update. |
| Source links | Use-case `## Design Sources` rows link to editable source artifacts when they exist. Use `N/A` only when no source exists yet or the use case has no UI source. |
| Previews | Committed previews are optional derived artifacts; use `N/A` until an export is needed for review or stable documentation. |
| AI agent | Agent-assisted design is allowed only against editable sources. Keep tool-specific setup out of repo docs unless the tool is approved as shared project infrastructure. |
| Secrets | Never commit API keys, local agent connection strings, transport URLs, personal tokens, or personal export links. |
| Link hygiene | Source links point to editable design artifacts. Preview/export images belong in the `Preview` cell, never as the editable `Source`. |

## Source Organization

Keep shared design-system decisions, app-shell references, and use-case-specific
screens separate even when they live in the same local workspace or design tool.

Recommended source names:

| Source | Purpose | Owning docs |
|---|---|---|
| `Axis Design System` | Tokens, primitive variants, component anatomy, and reusable UI states | [design-system.md](./design-system.md) |
| `Axis App Shell` | Shared authenticated layout, navigation, and responsive shell decisions | [frontend.md](./frontend.md) |
| `{domain} / {use-case-slug}` | Use-case-specific screen sources and state boards | Owning use-case `README.md` |

## Open Design Package

The repo-owned Axis package lives outside production code so generated design
artifacts cannot become implicit app dependencies:

```text
design-sources/open-design/axis/
├── manifest.json
├── DESIGN.md
├── tokens.css
└── USAGE.md
```

Ownership:

| File | Owns |
|---|---|
| `DESIGN.md` | Axis visual direction for Open Design prompts: brand, density, layout rules, component anatomy, motion, copy, and anti-patterns |
| `manifest.json` | Machine-readable package identity and declared source files |
| `tokens.css` | Compiled semantic token seed for Open Design artifacts; executable SPA token contracts remain in `frontend/src/design-system/` |
| `USAGE.md` | Agent-facing workflow for producing reviewable artifacts without changing product behavior |

Do not copy generated Open Design HTML or CSS into the SPA. Treat generated
artifacts as editable design sources or previews. Production UI changes still
flow through [design-system.md](./design-system.md): tokens, primitive
contracts, component tests, and consumer contracts.

For use-case-specific screens, keep frame or artifact names stable and match the
documented screen slug, for example `register-workspace` or
`register-workspace-states`. When a real editable source URL or file path exists,
update the owning docs in the same PR. Use `N/A` for committed previews until
review needs a stable exported image.

## Design-First Workflow

1. Read the owning use-case ACs and screen flow.
2. Create or update editable design sources for the screens the user will
   actually see.
3. Update the owning use-case `## Design Sources` table with source links.
4. Export and commit previews only when review needs a stable visual snapshot.
5. Implement code from the approved spec and design source.
6. Compare the implemented screen against the design source during frontend
   verification.

When a use case has more than three screens, branches, or confusing error
states, keep `## Screen flow` and `## Design Sources` in the same order.

## Agent Workflow

Treat any design agent as a write-capable collaborator.

- Start every session with read-only prompts: list sources, inspect frames or
  files, summarize layers, or export metadata.
- Before write prompts, ask the agent to describe the intended edits.
- Keep write prompts small: one screen, one component set, or one naming cleanup
  at a time.
- Keep the active source focused on the exact file, page, board, or artifact
  being changed.
- Use editable native objects for the selected source format. Do not replace a
  source with an imported/exported SVG or bitmap shortcut.
- After each write, inspect the touched source and exported preview when one
  exists. Verify it is visible, editable, and not clipped.
- Review the design manually before coding from it.
- Never let agent output change product behavior that is not already in the
  use-case spec.

## Verification

When design-source docs or links change, run the relevant checks:

```bash
python scripts/axis.py check use-case-docs
python scripts/axis.py check doc-navigation
python scripts/axis.py check markdown-links
python scripts/axis.py check doc-drift
```

Also run [visual-artifact-checklist.md](./visual-artifact-checklist.md) for any
changed design source, preview, or Mermaid diagram.
