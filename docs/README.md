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
| [Repo layout discovery](./playbooks/repo-layout-discovery.md) | **Agents:** auto vs manual CI maps (module → docs, Kafka, buf, indexes) + checklists before push |
| [Patterns](./playbooks/patterns.md) | Technical patterns, pitfalls, and code examples |
| [Testing](./playbooks/testing.md) | Test isolation, naming, file layout, mocking rules — .NET and frontend |
| [Frontend](./playbooks/frontend.md) | TanStack Query patterns, TypeScript discipline, routing, component design |
| [Wireframe kit](./playbooks/wireframes.md) | Screen generators, kit sections; **agents:** [spacing & blocks contract](./wireframes/README.md#agent-contract) |
| [Visual artifact checklist](./playbooks/visual-artifact-checklist.md) | Required review checklist for diagrams/wireframes/use-case visuals before commit |
| [Docs style](./playbooks/docs-style.md) | Anti-patterns for `.md` files — single-owner rule, size budgets, when to create vs absorb |

---

## Domains overview

| Domain | Status |
|---|---|
| [platform-foundation](./use-cases/platform-foundation/README.md) | 🚧 In Progress |
| [identity-access](./use-cases/identity-access/README.md) | 🚧 In Progress |
| [data-modeling](./use-cases/data-modeling/README.md) | 🚧 In Progress |
| [workflow-builder](./use-cases/workflow-builder/README.md) | 🚧 In Progress |
| [form-builder](./use-cases/form-builder/README.md) | 🚧 In Progress |
| [workflow-engine](./use-cases/workflow-engine/README.md) | 🚧 In Progress |
| [page-builder](./use-cases/page-builder/README.md) | ⏳ Not started |

---

## Key Diagrams

All diagrams are Excalidraw (`.excalidraw` source + `.svg` preview). Regenerate with `node docs/diagrams/generate-diagrams.mjs` then `docs/scripts/generate-diagrams.ps1`.

**System-level** (`docs/diagrams/`):

| Diagram | Source | Preview |
|---|---|---|
| System Context | [system-context.excalidraw](./diagrams/system-context.excalidraw) | [system-context.svg](./diagrams/system-context.svg) |
| Container Diagram | [container.excalidraw](./diagrams/container.excalidraw) | [container.svg](./diagrams/container.svg) |
| Module Overview | [module-overview.excalidraw](./diagrams/module-overview.excalidraw) | [module-overview.svg](./diagrams/module-overview.svg) |

**Use-case-level** (inside each `docs/use-cases/{domain}/{use-case}/` folder):

| Diagram | Source | Preview |
|---|---|---|
| Tenant Provisioning | [tenant-provisioning.excalidraw](./use-cases/platform-foundation/provision-tenant/tenant-provisioning.excalidraw) | [tenant-provisioning.svg](./use-cases/platform-foundation/provision-tenant/tenant-provisioning.svg) |
| Auth Flow | [auth-flow.excalidraw](./use-cases/identity-access/sign-in/auth-flow.excalidraw) | [auth-flow.svg](./use-cases/identity-access/sign-in/auth-flow.svg) |
| Data Model | [data-model.excalidraw](./use-cases/data-modeling/create-model/data-model.excalidraw) | [data-model.svg](./use-cases/data-modeling/create-model/data-model.svg) |
| Workflow Model | [workflow-model.excalidraw](./use-cases/workflow-builder/create-workflow/workflow-model.excalidraw) | [workflow-model.svg](./use-cases/workflow-builder/create-workflow/workflow-model.svg) |
| Form Model | [form-model.excalidraw](./use-cases/form-builder/create-form/form-model.excalidraw) | [form-model.svg](./use-cases/form-builder/create-form/form-model.svg) |
| Execution Flow | [execution-flow.excalidraw](./use-cases/workflow-engine/start-execution/execution-flow.excalidraw) | [execution-flow.svg](./use-cases/workflow-engine/start-execution/execution-flow.svg) |

## Wireframes

Excalidraw wireframes/diagrams live alongside each use case in `docs/use-cases/{domain}/{short-slug}/`. Shared app shell only: `docs/wireframes/app-shell`. Kit source is `.mjs` (`blocks.mjs`, `generate-template.mjs`). Each use case uses a `## Wireframes` + `## Diagrams` table (see [use-case template](./use-cases/USE_CASE_TEMPLATE.md)).

| Screen | Source | Preview |
|---|---|---|
| Register organization | [register-org.excalidraw](./use-cases/platform-foundation/register-org/register-org.excalidraw) | [register-org.svg](./use-cases/platform-foundation/register-org/register-org.svg) |
| Verify email | [verify-email.excalidraw](./use-cases/platform-foundation/verify-email/verify-email.excalidraw) | [verify-email.svg](./use-cases/platform-foundation/verify-email/verify-email.svg) |
| Pricing | [pricing.excalidraw](./use-cases/platform-foundation/view-plans/pricing.excalidraw) | [pricing.svg](./use-cases/platform-foundation/view-plans/pricing.svg) |
| Change password | [change-password.excalidraw](./use-cases/identity-access/change-password/change-password.excalidraw) | [change-password.svg](./use-cases/identity-access/change-password/change-password.svg) |
| Reset password | [forgot-password.excalidraw](./use-cases/identity-access/reset-password/forgot-password.excalidraw) | [forgot-password.svg](./use-cases/identity-access/reset-password/forgot-password.svg) |
| Accept invitation | [accept-invitation.excalidraw](./use-cases/identity-access/accept-invite/accept-invitation.excalidraw) | [accept-invitation.svg](./use-cases/identity-access/accept-invite/accept-invitation.svg) |

---

## Single source of truth per topic

When two docs disagree, the **owner** wins. Update the owner first; everything else is a pointer.

| Topic | Owner |
|---|---|
| Use-case layout (flow + AC + artifacts + status) | [use-cases/USE_CASE_TEMPLATE.md](./use-cases/USE_CASE_TEMPLATE.md) + [playbooks/docs-style.md](./playbooks/docs-style.md#use-case-files--wireframes--implementation-status) |
| Product scope, target users, production requirements | [PRODUCT_VISION.md](./PRODUCT_VISION.md) |
| Library versions and ADRs | [TECH_STACK.md](./TECH_STACK.md) |
| Source tree and module boundaries | [../CLAUDE.md](../CLAUDE.md) |
| Per-use-case ACs and current gaps | `docs/use-cases/{domain}/{short-slug}/README.md` |
| Module-wide layer status | [PROGRESS.md](./PROGRESS.md) |
| Daily agent workflow + gates | [playbooks/agent-checklist.md](./playbooks/agent-checklist.md) |
| Local dev (compose, ports, URLs) | [playbooks/local-dev.md](./playbooks/local-dev.md) + [`docker-compose.yml`](../docker-compose.yml) |
| Implementation patterns and pitfalls | [playbooks/patterns.md](./playbooks/patterns.md) (start at [patterns-index.md](./playbooks/patterns-index.md)) |
