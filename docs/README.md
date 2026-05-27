# Axis Platform — Documentation

> **Navigation**: [← CLAUDE.md](../CLAUDE.md)

> A low-code SaaS platform for building data-driven workflow applications.

---

## Navigation

| Section | Description |
|---|---|
| [Product Vision](./PRODUCT_VISION.md) | Goals, target users, problem & solution |
| [Tech Stack](./TECH_STACK.md) | Technology decisions and rationale |
| [Architecture](./ARCHITECTURE.md) | System design, modules, data strategy |
| [Epics](./epics/README.md) | Epic/module index and implementation progress by area |
| [Use cases](./use-cases/README.md) | User-facing behavior specs (flow + AC + wireframes + diagrams + implementation status) |
| [Wireframes](./wireframes/) | Screen wireframes — Excalidraw source + SVG preview |

### Playbooks (how-to guides)

| Playbook | Description |
|---|---|
| [Local dev](./playbooks/local-dev.md) | Run the full stack with `docker compose up` — URLs, ports, hot reload, reset commands |
| [Process](./playbooks/process.md) | Step-by-step implementation workflow — backend and frontend; deferred follow-ups and PR wrap-up checklist |
| [Patterns](./playbooks/patterns.md) | Technical patterns, pitfalls, and code examples |
| [Testing](./playbooks/testing.md) | Test isolation, naming, file layout, mocking rules — .NET and frontend |
| [Frontend](./playbooks/frontend.md) | TanStack Query patterns, TypeScript discipline, routing, component design |
| [Wireframe kit](./playbooks/wireframes.md) | Component kit template rules — section builder anatomy, ID prefixes, offsets |
| [Docs style](./playbooks/docs-style.md) | Anti-patterns for `.md` files — single-owner rule, size budgets, when to create vs absorb |

---

## Epics Overview

| ID | Epic | Phase | Status |
|---|---|---|---|
| [E01](./epics/E01-platform-foundation/README.md) | Platform Foundation | MVP | 🚧 In Progress |
| [E02](./epics/E02-identity-access/README.md) | Identity & Access Management | MVP | 🚧 In Progress |
| [E03](./epics/E03-data-modeling/README.md) | Data Modeling | MVP | 🚧 In Progress |
| [E04](./epics/E04-workflow-builder/README.md) | Workflow Builder | MVP | 🚧 In Progress |
| [E05](./epics/E05-form-builder/README.md) | Form Builder | MVP | 🚧 In Progress |
| [E06](./epics/E06-workflow-engine/README.md) | Workflow Execution Engine | MVP | 🚧 In Progress |
| [E07](./epics/E07-page-builder/README.md) | Page & UI Builder | Phase 2 | ⏳ Planned |

---

## Key Diagrams

All diagrams are Excalidraw (`.excalidraw` source + `.svg` preview). Regenerate with `node docs/diagrams/generate-diagrams.mjs` then `docs/scripts/generate-diagrams.ps1`.

**System-level** (`docs/diagrams/`):

| Diagram | Source | Preview |
|---|---|---|
| System Context | [system-context.excalidraw](./diagrams/system-context.excalidraw) | [system-context.svg](./diagrams/system-context.svg) |
| Container Diagram | [container.excalidraw](./diagrams/container.excalidraw) | [container.svg](./diagrams/container.svg) |
| Module Overview | [module-overview.excalidraw](./diagrams/module-overview.excalidraw) | [module-overview.svg](./diagrams/module-overview.svg) |

**Epic-level** (in each `docs/epics/E0{N}-name/diagrams/`):

| Diagram | Source | Preview |
|---|---|---|
| Tenant Provisioning | [tenant-provisioning.excalidraw](./epics/E01-platform-foundation/diagrams/tenant-provisioning.excalidraw) | [tenant-provisioning.svg](./epics/E01-platform-foundation/diagrams/tenant-provisioning.svg) |
| Auth Flow | [auth-flow.excalidraw](./epics/E02-identity-access/diagrams/auth-flow.excalidraw) | [auth-flow.svg](./epics/E02-identity-access/diagrams/auth-flow.svg) |
| Data Model | [data-model.excalidraw](./epics/E03-data-modeling/diagrams/data-model.excalidraw) | [data-model.svg](./epics/E03-data-modeling/diagrams/data-model.svg) |
| Workflow Model | [workflow-model.excalidraw](./epics/E04-workflow-builder/diagrams/workflow-model.excalidraw) | [workflow-model.svg](./epics/E04-workflow-builder/diagrams/workflow-model.svg) |
| Form Model | [form-model.excalidraw](./epics/E05-form-builder/diagrams/form-model.excalidraw) | [form-model.svg](./epics/E05-form-builder/diagrams/form-model.svg) |
| Execution Flow | [execution-flow.excalidraw](./epics/E06-workflow-engine/diagrams/execution-flow.excalidraw) | [execution-flow.svg](./epics/E06-workflow-engine/diagrams/execution-flow.svg) |

## Wireframes

Excalidraw wireframes live in `docs/epics/{E0N}/wireframes/`, co-located with each epic's features and diagrams. Shared screens (template, app-shell) remain in `docs/wireframes/`. Each feature file lists wireframes in a `## Wireframes` table (see [US template](./epics/_template-feature-us.md)).

| Screen | Source | Preview |
|---|---|---|
| Login | [login.excalidraw](./epics/E02-identity-access/wireframes/login.excalidraw) | [login.svg](./epics/E02-identity-access/wireframes/login.svg) |

---

## Single source of truth per topic

When two docs disagree, the **owner** wins. Update the owner first; everything else is a pointer.

| Topic | Owner |
|---|---|
| Feature US layout (wireframes + status tables) | [epics/_template-feature-us.md](./epics/_template-feature-us.md) + [playbooks/docs-style.md](./playbooks/docs-style.md#feature-files--wireframes--implementation-status) |
| Product scope, target users, MVP cut | [PRODUCT_VISION.md](./PRODUCT_VISION.md) |
| Library versions and ADRs | [TECH_STACK.md](./TECH_STACK.md) |
| Source tree and module boundaries | [../CLAUDE.md](../CLAUDE.md) |
| Per-feature ACs and current gaps | `docs/epics/{module}/features/F0N-*.md` |
| Module-wide layer status | [PROGRESS.md](./PROGRESS.md) |
| Daily agent workflow + gates | [playbooks/agent-checklist.md](./playbooks/agent-checklist.md) |
| Local dev (compose, ports, URLs) | [playbooks/local-dev.md](./playbooks/local-dev.md) + [`docker-compose.yml`](../docker-compose.yml) |
| Implementation patterns and pitfalls | [playbooks/patterns.md](./playbooks/patterns.md) (start at [patterns-index.md](./playbooks/patterns-index.md)) |
