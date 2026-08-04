# Axis Documentation

> **Navigation**: [AGENTS.md](../AGENTS.md)

Axis is an open-source platform being built for adaptable, workflow-driven business applications.

## Primary Docs

| Doc | Use |
|---|---|
| [docs/ARCHITECTURE.md](./ARCHITECTURE.md) | Current runtime shape and boundaries. |
| [docs/TECH_STACK.md](./TECH_STACK.md) | Approved stack baseline and version owners. |
| [docs/use-cases/README.md](./use-cases/README.md) | Implemented or actively specified use cases only. |
| [docs/foundations/README.md](./foundations/README.md) | Non-use-case foundation contracts with AC/AT. |
| [docs/ENFORCEMENT.md](./ENFORCEMENT.md) | Enforcement status for recurring rule classes. |

## Playbooks

| Playbook | Use |
|---|---|
| [docs/playbooks/agent-checklist.md](./playbooks/agent-checklist.md) | Review checkpoints and verification boundary. |
| [docs/playbooks/design-gate.md](./playbooks/design-gate.md) | Required reasoning artifact before non-trivial changes. |
| [docs/playbooks/design-gate-structural-agent-workflows.md](./playbooks/design-gate-structural-agent-workflows.md) | Corrective full dossier for structural agent workflows and policy guards. |
| [docs/playbooks/design-gate-executable-business-object-workflow.md](./playbooks/design-gate-executable-business-object-workflow.md) | Current full dossier for the executable Business Object workflow. |
| [docs/playbooks/design-gate-mcp-browser-authorization.md](./playbooks/design-gate-mcp-browser-authorization.md) | Current full dossier for the local MCP browser-authorization handoff. |
| [docs/playbooks/api-patterns.md](./playbooks/api-patterns.md) | REST/OpenAPI and API-type change guidance. |
| [docs/playbooks/frontend.md](./playbooks/frontend.md) | SPA implementation guidance. |
| [docs/playbooks/client-experience.md](./playbooks/client-experience.md) | Self-directed client, UI, and UX decisions. |
| [docs/playbooks/search.md](./playbooks/search.md) | Server-owned search, CQRS provider, and storage boundaries. |
| [docs/playbooks/testing.md](./playbooks/testing.md) | Backend and frontend test conventions. |
| [docs/playbooks/docs-style.md](./playbooks/docs-style.md) | Documentation ownership and size rules. |
| [docs/playbooks/scripts.md](./playbooks/scripts.md) | Axis CLI and repo script standards. |
| [docs/playbooks/local-dev.md](./playbooks/local-dev.md) | Local stack commands and ports. |
| [docs/playbooks/mcp.md](./playbooks/mcp.md) | Local MCP bridge boundary, auth, tools, and verification. |

## Current Diagram

```mermaid
flowchart LR
  User["User"]
  SPA["React SPA"]
  API["Axis.Api"]
  Identity["Identity module"]
  DB[("PostgreSQL: axis_identity")]
  Redis[("Redis")]
  Mail["SMTP / Maildev"]

  User --> SPA
  SPA --> API
  API --> Identity
  Identity --> DB
  Identity --> Redis
  Identity --> Mail
```
