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

Platform **architecture** diagrams live here as **Mermaid** (renders on GitHub and in editors with Mermaid support). **UI wireframes** stay Excalidraw — see [Wireframes](#wireframes) below.

Use-case **sequence / entity** diagrams live in each use-case `README.md` under `## Diagrams` (also Mermaid). Index:

| Diagram | Owner |
|---|---|
| Tenant provisioning | [provision-tenant § Diagrams](./use-cases/platform-foundation/provision-tenant/README.md#diagrams) |
| Register organization (flow + cases) | [register-org § Diagrams](./use-cases/platform-foundation/register-org/README.md#diagrams) |
| Auth flow | [sign-in § Diagrams](./use-cases/identity-access/sign-in/README.md#diagrams) |
| Data model | [create-model § Diagrams](./use-cases/data-modeling/create-model/README.md#diagrams) |
| Workflow model | [create-workflow § Diagrams](./use-cases/workflow-builder/create-workflow/README.md#diagrams) |
| Form model | [create-form § Diagrams](./use-cases/form-builder/create-form/README.md#diagrams) |
| Execution flow | [start-execution § Diagrams](./use-cases/workflow-engine/start-execution/README.md#diagrams) |

### System context

External actors and the Axis platform boundary. Detail: [ARCHITECTURE.md § System Context](./ARCHITECTURE.md#system-context).

```mermaid
flowchart LR
  PA[Platform Admin]
  OA[Org Admin]
  OM[Org Member]
  EU[End User]

  subgraph Axis["Axis Platform"]
    SPA[Web Application]
    API[Axis.Api Gateway + Modules]
  end

  Email[(Email Service)]
  ExtAPI[(External APIs)]
  Webhook[(Webhook Targets)]

  PA --> API
  OA --> SPA
  OM --> SPA
  EU --> SPA
  SPA --> API
  API --> Email
  API --> ExtAPI
  API --> Webhook
```

### Container diagram

Runtime containers, brokers, and per-module databases. Detail: [ARCHITECTURE.md § Containers](./ARCHITECTURE.md#containers).

```mermaid
flowchart TB
  subgraph Platform["Axis.Api Gateway + Module Services (modulith)"]
    SPA[Web Application<br/>React SPA]
    ID[Identity]
    DM[DataModeling]
    WB[WorkflowBuilder]
    FB[FormBuilder]
    WE[WorkflowEngine]
    PB[PageBuilder]
    Kafka[Kafka + Schema Registry<br/>Events / Snapshots]
    RMQ[RabbitMQ<br/>Commands / Jobs / Saga]
    GRPC[gRPC Contracts<br/>Sync RPC]
    OIDC[OpenIddict<br/>OAuth2 / OIDCE]
    SR[SignalR]
  end

  subgraph Data["Per-module PostgreSQL"]
    DB1[(Identity DB)]
    DB2[(DataModeling DB)]
    DB3[(WorkflowBuilder DB)]
    DB4[(WorkflowEngine DB)]
    DB5[(FormBuilder DB)]
    DB6[(PageBuilder DB)]
  end

  Redis[(Redis)]
  Vault[HashiCorp Vault]
  Obs[Grafana Tempo / Loki / Mimir]
  S3[AWS S3]
  Mail[Email Service]

  ID & DM & WB & FB & WE & PB --> Kafka
  ID & DM & WB & FB & WE & PB --> RMQ
  ID & DM & WB & FB & WE & PB --> GRPC
  ID & DM & WB & FB & WE & PB --> DB1 & DB2 & DB3 & DB4 & DB5 & DB6
  Platform --> Redis
  Platform --> Vault
  Platform --> Obs
  Platform --> S3
  Platform --> Mail
  SPA --> Platform
```

### Module overview

Cross-module communication (Kafka events, RabbitMQ commands, gRPC escape hatch). Detail: [ARCHITECTURE.md](./ARCHITECTURE.md).

```mermaid
flowchart TB
  SK[Shared Kernel — primitives & abstractions]

  ID[Identity]
  DM[DataModeling]
  WB[WorkflowBuilder]
  FB[FormBuilder]
  WE[WorkflowEngine]
  PB[PageBuilder]

  SK --- ID & DM & WB & FB
  Kafka[Kafka + Schema Registry — Events / Snapshots]
  RMQ[RabbitMQ — Commands / Jobs / Saga steps]
  GRPC[gRPC — sync RPC escape hatch]

  WB -->|publish| Kafka
  Kafka -->|consume| WE
  RMQ -->|consume command| FB
  WE -->|local read model| WE
```

## Wireframes

Excalidraw wireframes live alongside each use case in `docs/use-cases/{domain}/{short-slug}/`. Shared app shell only: `docs/wireframes/app-shell`. Kit source is `.mjs` (`blocks.mjs`, `generate-template.mjs`). Each use case uses `## Wireframes` (Excalidraw) and `## Diagrams` (Mermaid in README) — see [use-case template](./use-cases/USE_CASE_TEMPLATE.md).

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
