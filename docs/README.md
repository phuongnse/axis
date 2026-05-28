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
| [Use cases](./use-cases/README.md) | Product specs: domains, use cases, wireframes, diagrams, implementation progress |

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

## Domains overview

| Domain | Phase | Status |
|---|---|---|
| [platform-foundation](./use-cases/platform-foundation/README.md) | MVP | 🚧 In Progress |
| [identity-access](./use-cases/identity-access/README.md) | MVP | 🚧 In Progress |
| [data-modeling](./use-cases/data-modeling/README.md) | MVP | 🚧 In Progress |
| [workflow-builder](./use-cases/workflow-builder/README.md) | MVP | 🚧 In Progress |
| [form-builder](./use-cases/form-builder/README.md) | MVP | 🚧 In Progress |
| [workflow-engine](./use-cases/workflow-engine/README.md) | MVP | 🚧 In Progress |
| [page-builder](./use-cases/page-builder/README.md) | Phase 2 | ⏳ Planned |

---

## Key Diagrams

All diagrams are Excalidraw (`.excalidraw` source + `.svg` preview). Regenerate with `node docs/use-cases/_architecture/generate-diagrams.mjs` then `docs/scripts/generate-diagrams.ps1`.

**System-level** (`docs/use-cases/_architecture/diagrams/`):

| Diagram | Source | Preview |
|---|---|---|
| System Context | [system-context.excalidraw](./use-cases/_architecture/diagrams/system-context.excalidraw) | [system-context.svg](./use-cases/_architecture/diagrams/system-context.svg) |
| Container Diagram | [container.excalidraw](./use-cases/_architecture/diagrams/container.excalidraw) | [container.svg](./use-cases/_architecture/diagrams/container.svg) |
| Module Overview | [module-overview.excalidraw](./use-cases/_architecture/diagrams/module-overview.excalidraw) | [module-overview.svg](./use-cases/_architecture/diagrams/module-overview.svg) |

**Domain-level** (in each `docs/use-cases/{domain}/diagrams/`):

| Diagram | Source | Preview |
|---|---|---|
| Tenant Provisioning | [tenant-provisioning.excalidraw](./use-cases/platform-foundation/provision-tenant/tenant-provisioning.excalidraw) | [tenant-provisioning.svg](./use-cases/platform-foundation/provision-tenant/tenant-provisioning.svg) |
| Auth Flow | [auth-flow.excalidraw](./use-cases/identity-access/sign-in/auth-flow.excalidraw) | [auth-flow.svg](./use-cases/identity-access/sign-in/auth-flow.svg) |
| Data Model | [data-model.excalidraw](./use-cases/data-modeling/create-model/data-model.excalidraw) | [data-model.svg](./use-cases/data-modeling/create-model/data-model.svg) |
| Workflow Model | [workflow-model.excalidraw](./use-cases/workflow-builder/create-workflow/workflow-model.excalidraw) | [workflow-model.svg](./use-cases/workflow-builder/create-workflow/workflow-model.svg) |
| Form Model | [form-model.excalidraw](./use-cases/form-builder/create-form/form-model.excalidraw) | [form-model.svg](./use-cases/form-builder/create-form/form-model.svg) |
| Execution Flow | [execution-flow.excalidraw](./use-cases/workflow-engine/start-execution/execution-flow.excalidraw) | [execution-flow.svg](./use-cases/workflow-engine/start-execution/execution-flow.svg) |

## Wireframes

Excalidraw wireframes live in `docs/use-cases/{domain}/wireframes/`, co-located with each domain's use cases and diagrams. Shared screens (template, app-shell) remain in `docs/use-cases/_shared/wireframes/`. Each use case lives in `docs/use-cases/{domain}/{short-slug}/README.md` with a `## Wireframes` table (see [use-case template](./use-cases/USE_CASE_TEMPLATE.md)).

| Screen | Source | Preview |
|---|---|---|
| Login | [login.excalidraw](./use-cases/identity-access/sign-in/login.excalidraw) | [login.svg](./use-cases/identity-access/sign-in/login.svg) |

---

## Single source of truth per topic

When two docs disagree, the **owner** wins. Update the owner first; everything else is a pointer.

| Topic | Owner |
|---|---|
| Use-case layout (flow + AC + artifacts + status) | [use-cases/USE_CASE_TEMPLATE.md](./use-cases/USE_CASE_TEMPLATE.md) + [playbooks/docs-style.md](./playbooks/docs-style.md#use-case-files-wireframes--implementation-status) |
| Product scope, target users, MVP cut | [PRODUCT_VISION.md](./PRODUCT_VISION.md) |
| Library versions and ADRs | [TECH_STACK.md](./TECH_STACK.md) |
| Source tree and module boundaries | [../CLAUDE.md](../CLAUDE.md) |
| Per-use-case ACs and current gaps | `docs/use-cases/{domain}/{short-slug}/README.md` |
| Module-wide layer status | [PROGRESS.md](./PROGRESS.md) |
| Daily agent workflow + gates | [playbooks/agent-checklist.md](./playbooks/agent-checklist.md) |
| Local dev (compose, ports, URLs) | [playbooks/local-dev.md](./playbooks/local-dev.md) + [`docker-compose.yml`](../docker-compose.yml) |
| Implementation patterns and pitfalls | [playbooks/patterns.md](./playbooks/patterns.md) (start at [patterns-index.md](./playbooks/patterns-index.md)) |
