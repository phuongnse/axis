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
| [Epics](./epics/README.md) | All epics, features, and user stories |
| [Wireframes](./wireframes/) | Screen wireframes — Excalidraw source + SVG preview |

### Playbooks (how-to guides)

| Playbook | Description |
|---|---|
| [Process](./playbooks/process.md) | Step-by-step implementation workflow — backend and frontend |
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

| Diagram | Description |
|---|---|
| [System Context](./diagrams/system-context.puml) | C4 Level 1 — actors and external systems |
| [Container Diagram](./diagrams/container-diagram.puml) | C4 Level 2 — main containers |
| [Module Overview](./diagrams/module-overview.puml) | Modular monolith modules and dependencies |

## Wireframes

Excalidraw wireframes live in `docs/wireframes/`. Each feature file links to its wireframe via a `> **Wireframe**` callout directly after the feature title.

| Screen | Source | Preview |
|---|---|---|
| Login | [login.excalidraw](./wireframes/E02-identity-access/login.excalidraw) | [login.svg](./wireframes/E02-identity-access/login.svg) |

---

## Roadmap

### Phase 1 — MVP
Core platform: multi-tenancy, auth, data modeling, workflow building, form builder, execution engine.

### Phase 2 — UI Builder
Drag & drop page builder, widget library, data binding.

### Phase 3 — Ecosystem
Marketplace connectors, AI-assisted workflow suggestions, analytics dashboard.
