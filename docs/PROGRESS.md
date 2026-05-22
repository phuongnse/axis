# Implementation Progress

> Updated each time a layer is completed. Source of truth for current implementation state.
> When completing a layer, update this file — **not** CLAUDE.md.

## Shared Kernel ⚠️

Domain, Application, and Infrastructure layers complete.

> ⚠️ **Remaining gap (deferred):**
> - **Wolverine durable outbox not configured**: Wolverine is wired and `IMessageBus` resolves correctly. Domain events are dispatched in-memory after `SaveChangesAsync`. The durable PostgreSQL outbox (survives process restart) is deferred until a decision is made on the Wolverine persistence schema strategy — tracked as E01 Platform Foundation gap.

## Identity — E02-identity-access

**Domain ✅ | Application ✅ | Infrastructure ✅ | API ✅ | Frontend ⏳**

Full auth, user, role, invitation, and session management. OpenIddict 5.x OIDC server (Authorization Code + PKCE for SPA; Client Credentials for M2M). RBAC via custom permission policies. All Identity API endpoints covered by integration tests.

## DataModeling — E03-data-modeling

**Domain ✅ | Application ✅ | Infrastructure ✅ | API ✅ | Frontend ⏳**

Custom model, field, data class, and record CRUD. Full-text search, per-field JSONB filters, sort-by-column, bulk delete, CSV export. All endpoints covered by integration tests.

## WorkflowBuilder — E04-workflow-builder

**Domain ✅ | Application ✅ | Infrastructure ✅ | API ✅ | Frontend ⏳**

Workflow definitions with steps, transitions, triggers, cycle detection, publish/archive lifecycle. Import/export (JSON + ZIP). All endpoints covered by integration tests.

## FormBuilder — E05-form-builder

**Domain ✅ | Application ✅ | Infrastructure ✅ | API ✅ | Frontend ⏳**

Form definitions with typed fields (9 field types, polymorphic config). Cross-module isolation via Wolverine event-driven local denormalization — no direct cross-module SQL. All endpoints covered by integration tests.

## WorkflowEngine — E06-workflow-engine

**Domain ✅ | Application ✅ | Infrastructure ⚠️ | API ⏳ | Frontend ⏳**

Execution lifecycle (start, cancel, retry, retry-with-context). Step state machine with per-step execution handlers (Form, HTTP, Condition, Script, Notification). `WorkflowSnapshot` local read model populated from `WorkflowPublished` events. Paged execution history and retry history queries. Cross-module isolation via local read models — WorkflowEngine never queries WorkflowBuilder or FormBuilder DBs. Infrastructure ⚠️: `IScriptExecutor` and `INotificationSender` are stubs (real dispatch deferred).

## PageBuilder — E07-page-builder

**⏳ Phase 2 — not started**

---

## Frontend Foundation

**Status: ✅ Tooling complete — E02 auth shell ⚠️**

React 18 + TypeScript + Vite. TanStack Router, TanStack Query, Zustand, shadcn/ui, Tailwind, Zod, react-hook-form. Biome (lint + format). `npm run ci` gate enforced. E02: login/app shell/dashboard match wireframe layout (Industrial Calm palette); OIDC PKCE form started; register/forgot are layout stubs. Other module UIs remain ⏳.
