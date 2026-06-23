# Design Source Playbook

> **Navigation**: [← docs/README.md](../README.md) · [← wireframes.md](./wireframes.md) · [← AGENTS.md](../../AGENTS.md)

This playbook owns the Axis design-source workflow. Penpot is the approved source of truth for product design, design-system files, and low-fidelity screen sources. Axis implementation still follows the use-case specs first: design clarifies layout and handoff, but it does not invent behavior, acceptance criteria, endpoints, roles, or data contracts.

## Decisions

| Topic | Rule |
|---|---|
| Design tool | Use Penpot Cloud as the product design source under the `Axis` workspace/team and `Axis Product` project. Contributors use individual accounts against the shared Axis project. |
| Runtime boundary | Do not add Penpot to the Axis app stack or repo CLI; Penpot is external design infrastructure, not an app dependency. |
| Source links | Use-case `## Design Sources` rows link to Penpot file/page/frame URLs when the source exists. |
| Previews | Committed previews are optional; use `N/A` until an export is needed for review or stable documentation. |
| AI agent | Use the configured Penpot Cloud MCP workflow. Do not add Penpot MCP as an Axis frontend dependency or repo command. |
| Secrets | Never commit MCP keys, URLs containing `userToken`, personal access tokens, or personal Penpot exports. |
| Link hygiene | Docs link to Penpot design file/page/frame URLs only. MCP connection and stream URLs stay in the local MCP client configuration. |

## Penpot Cloud

Create the baseline in Penpot Cloud before adding source links to use-case docs.
Use `Axis` as the stable workspace/team namespace and `Axis Product` as the
project that owns product design sources. Each contributor uses their own
Penpot account against the shared Axis project.

Recommended Cloud structure:

| Level | Naming |
|---|---|
| Workspace / Team | `Axis` |
| Project | `Axis Product` |
| Design system file | `Axis Design System` |
| Shared shell file | `Axis App Shell` |
| Use-case file | `{domain} / {use-case-slug}` |
| Page | `Flow`, `Design Sources`, `States`, `Handoff` as needed |
| Frame | Match the use-case screen slug, e.g. `register-workspace` or `register-workspace-states` |

Keep design-system components and shared app-shell references in shared files.
Keep use-case-specific screens in the owning use-case file. When a real Cloud
file/page/frame URL exists, update the owning docs in the same PR.

Official references:

- [Penpot MCP guide](https://help.penpot.app/mcp/)

## Baseline Inventory

Create these shared Penpot sources first. Do not add a source link to an owning
use-case document until the referenced file/page/frame exists in Penpot.

| Source | Penpot location | Purpose | Owning docs |
|---|---|---|---|
| Design system | `Axis` → `Axis Product` → `Axis Design System` | Tokens, primitive variants, component anatomy, and reusable UI states | [design-system.md](./design-system.md) |
| App shell | `Axis` → `Axis Product` → `Axis App Shell` | Shared authenticated layout, navigation, and responsive shell decisions | [frontend.md](./frontend.md) |
| Register workspace | `Axis` → `Axis Product` → `platform-foundation / register-workspace` → `Design Sources` → `register-workspace` | First product-flow source frame for the current public registration surface | [platform-foundation/register-workspace](../use-cases/platform-foundation/register-workspace/README.md) |

For each source, keep the Penpot frame name stable and update the owning docs in
the same PR when a real Penpot URL becomes available. Use `N/A` for committed
previews until review needs a stable exported image.

## Design-First Workflow

1. Read the owning use-case ACs and screen flow.
2. Create or update Penpot frames for the screens the user will actually see.
3. Update the owning use-case `## Design Sources` table with Penpot source links.
4. Export and commit previews only when review needs a stable visual snapshot.
5. Implement code from the approved spec and design source.
6. Compare the implemented screen against Penpot during frontend verification.

When a use case has more than three screens, branches, or confusing error states, keep `## Screen flow` and `## Design Sources` in the same order.

## AI Agent Workflow

Penpot MCP can read and modify the currently focused Penpot page. Treat it like a write-capable collaborator.

### Cloud MCP

Use this with the configured Penpot Cloud MCP integration:

1. In Penpot, open **Your account → Integrations → MCP Server**.
2. Enable MCP and generate an MCP key.
3. Configure the local MCP client with the generated MCP connection details.
4. Open the intended Penpot file and choose **File → MCP Server → Connect**.

### Prompt Discipline

- Start every session with read-only prompts: list pages, inspect frames, summarize layers, or export metadata.
- Before write prompts, ask the agent to describe the intended edits.
- Keep write prompts small: one screen, one component set, or one naming cleanup at a time.
- Keep the active Penpot tab focused on the exact file/page being changed.
- Use native Penpot boards, shapes, text, and components as the editable source. Do not replace a source frame with an imported/exported SVG or bitmap shortcut.
- After each write, inspect the layer tree and export the touched board/frame to verify it is visible, editable, and not clipped.
- Review the design manually in Penpot before coding from it.
- Never let agent output change product behavior that is not already in the use-case spec.

## Verification

When design-source docs or links change, run the relevant checks:

```bash
python scripts/axis.py check use-case-docs
python scripts/axis.py check doc-navigation
python scripts/axis.py check markdown-links
python scripts/axis.py check doc-drift
```

Also run [visual-artifact-checklist.md](./visual-artifact-checklist.md) for any changed design source, preview, or Mermaid diagram.
