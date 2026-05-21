# Axis Platform — Documentation

> **Navigation**: [← CLAUDE.md](../CLAUDE.md)

> A low-code SaaS platform for building data-driven workflow applications.

---

## Navigation

| Section | Description |
|---|---|
| [Contributing](../CONTRIBUTING.md) | Branch naming, PR checklist, doc drift, gates |
| [Product Vision](./PRODUCT_VISION.md) | Goals, target users, problem & solution |
| [Progress](./PROGRESS.md) | Module layer status (summary; epics hold detail) |
| [Tech Stack](./TECH_STACK.md) | Technology decisions and rationale |
| [Architecture](./ARCHITECTURE.md) | System design, modules, data strategy |
| [Epics](./epics/README.md) | All epics, features, and user stories |
| [Wireframes](./wireframes/) | Screen wireframes — Excalidraw source + SVG preview |

### Playbooks (how-to guides)

| Playbook | Description |
|---|---|
| [**Agent checklist**](./playbooks/agent-checklist.md) | **One page** — gates, epic map; `./scripts/check-doc-drift.sh` before every PR |
| [Process](./playbooks/process.md) | Step-by-step implementation workflow — backend and frontend |
| [Patterns index](./playbooks/patterns-index.md) | Task → section map into `patterns.md` |
| [Patterns](./playbooks/patterns.md) | Technical patterns, pitfalls, and code examples |
| [Testing](./playbooks/testing.md) | Test isolation, naming, file layout, mocking rules — .NET and frontend |
| [Frontend](./playbooks/frontend.md) | TanStack Query patterns, TypeScript discipline, routing, component design |
| [Wireframe kit](./playbooks/wireframes.md) | Component kit template rules — section builder anatomy, ID prefixes, offsets |

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

Excalidraw wireframes live in `docs/epics/{E0N}/wireframes/`, co-located with each epic's features and diagrams. Shared screens (template, app-shell) remain in `docs/wireframes/`. Each feature file links to its wireframe via a `> **Wireframe**` callout directly after the feature title.

| Screen | Source | Preview |
|---|---|---|
| Login | [login.excalidraw](./epics/E02-identity-access/wireframes/login.excalidraw) | [login.svg](./epics/E02-identity-access/wireframes/login.svg) |

---

## Roadmap

### Phase 1 — MVP
Core platform: multi-tenancy, auth, data modeling, workflow building, form builder, execution engine.

### Phase 2 — UI Builder
Drag & drop page builder, widget library, data binding.

### Phase 3 — Ecosystem
Marketplace connectors, AI-assisted workflow suggestions, analytics dashboard.
