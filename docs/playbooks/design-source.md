# Design Source Playbook

> **Navigation**: [← docs/README.md](../README.md) · [← wireframes.md](./wireframes.md) · [← AGENTS.md](../../AGENTS.md)

This playbook owns the Axis design-source workflow. Penpot is the approved source of truth for product design, design-system files, and low-fidelity screen sources. Axis implementation still follows the use-case specs first: design clarifies layout and handoff, but it does not invent behavior, acceptance criteria, endpoints, roles, or data contracts.

## Decisions

| Topic | Rule |
|---|---|
| Design tool | Use self-hosted Penpot for shared product design sources. Personal drafts are not source of truth until moved into the shared instance. |
| Runtime boundary | Do not add Penpot to the default Axis `docker-compose.yml`; it is design infrastructure, not an app dependency. |
| Source links | Use-case `## Wireframes` rows link to Penpot file/page/frame URLs when the source exists. |
| Previews | Committed previews are optional; use `N/A` until an export is needed for review or stable documentation. |
| AI agent | Use the official Penpot MCP workflow. Do not add Penpot MCP as an Axis frontend dependency. |
| Secrets | Never commit MCP keys, URLs containing `userToken`, personal Penpot exports, or local Penpot `.env` files. |

## Self-Hosted Penpot

Keep Penpot separate from the Axis app stack so local app development stays deterministic.
Run the optional design-infra stack through the repo CLI:

```bash
python scripts/axis.py design-source penpot up
```

The first run downloads the official Penpot compose file for the pinned
`PENPOT_VERSION` used by `scripts/axis.py` into `.local/penpot/docker-compose.yaml`.
The `.local/` directory is ignored because it contains runtime state, local environment files, and design data;
the repeatable workflow lives in `scripts/axis.py` and this playbook.

Default local URLs:

| Service | URL |
|---|---|
| Penpot | `http://localhost:9001` |
| Penpot mail catcher | `http://localhost:1080` |

Check status:

```bash
python scripts/axis.py design-source penpot status
```

Smoke-test the local HTTP surfaces:

```bash
python scripts/axis.py design-source penpot smoke
```

Stop it with:

```bash
python scripts/axis.py design-source penpot down
```

Update it with:

```bash
python scripts/axis.py design-source penpot update
```

For troubleshooting:

```bash
python scripts/axis.py design-source penpot logs
```

For a shared team instance, put Penpot behind HTTPS and a reverse proxy. Back up
Penpot Docker volumes before upgrades because they hold design data and uploaded
assets. Override `PENPOT_VERSION` in the shell only when intentionally moving the
local design-source stack to a reviewed Penpot release.

Official references:

- [Penpot self-host Docker guide](https://help.penpot.app/technical-guide/getting-started/docker/)
- [Penpot MCP guide](https://help.penpot.app/mcp/)

## Penpot File Structure

Use stable names so source links survive design handoff and AI-agent edits.

| Level | Naming |
|---|---|
| Team | `Axis` |
| Project | `Axis Product` |
| Design system file | `Axis Design System` |
| Shared shell file | `Axis App Shell` |
| Use-case file | `{domain} / {use-case-slug}` |
| Page | `Flow`, `Wireframes`, `States`, `Handoff` as needed |
| Frame | Match the use-case screen slug, e.g. `register-workspace` or `register-workspace-states` |

Keep design-system components and shared app-shell references in shared files. Keep use-case-specific screens in the owning use-case file.

## Baseline Inventory

Create these shared Penpot sources first. Do not add a source link to an owning
use-case document until the referenced file/page/frame exists in Penpot.

| Source | Penpot location | Purpose | Owning docs |
|---|---|---|---|
| Design system | `Axis` → `Axis Product` → `Axis Design System` | Tokens, primitive variants, component anatomy, and reusable UI states | [design-system.md](./design-system.md) |
| App shell | `Axis` → `Axis Product` → `Axis App Shell` | Shared authenticated layout, navigation, and responsive shell decisions | [frontend.md](./frontend.md) |
| Register workspace | `Axis` → `Axis Product` → `platform-foundation / register-workspace` → `Wireframes` → `register-workspace` | First product-flow source frame for the current public registration surface | [platform-foundation/register-workspace](../use-cases/platform-foundation/register-workspace/README.md) |

For each source, keep the Penpot frame name stable and update the owning docs in
the same PR when a real Penpot URL becomes available. Use `N/A` for committed
previews until review needs a stable exported image.

## Design-First Workflow

1. Read the owning use-case ACs and screen flow.
2. Create or update Penpot frames for the screens the user will actually see.
3. Update the owning use-case `## Wireframes` table with Penpot source links.
4. Export and commit previews only when review needs a stable visual snapshot.
5. Implement code from the approved spec and design source.
6. Compare the implemented screen against Penpot during frontend verification.

When a use case has more than three screens, branches, or confusing error states, keep `## Screen flow` and `## Wireframes` in the same order.

## AI Agent Workflow

Penpot MCP can read and modify the currently focused Penpot page. Treat it like a write-capable collaborator.

### Remote MCP

Use this when the Penpot account or shared instance exposes the MCP integration:

1. In Penpot, open **Your account → Integrations → MCP Server**.
2. Enable MCP and generate an MCP key.
3. Configure the local MCP client with the server URL from Penpot.
4. Open the intended Penpot file and choose **File → MCP Server → Connect**.

URL shape:

```text
https://{your-penpot-domain}/mcp/stream?userToken={secret-token}
```

### Local MCP

Use this when you need local resources or the remote MCP endpoint is unavailable:

```bash
python scripts/axis.py design-source penpot mcp
```

Then in Penpot:

1. Load the plugin manifest from `http://localhost:4400/manifest.json`.
2. Run the plugin and connect it to the MCP server.
3. Point the MCP client at `http://localhost:4401/mcp` with HTTP transport.

### Prompt Discipline

- Start every session with read-only prompts: list pages, inspect frames, summarize layers, or export metadata.
- Before write prompts, ask the agent to describe the intended edits.
- Keep write prompts small: one screen, one component set, or one naming cleanup at a time.
- Keep the active Penpot tab focused on the exact file/page being changed.
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

Also run [visual-artifact-checklist.md](./visual-artifact-checklist.md) for any changed wireframe source, preview, or Mermaid diagram.
