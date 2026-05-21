# Implementation Progress

> Updated each time a layer is completed. Source of truth for current implementation state.
> When completing a layer, update this file — **not** CLAUDE.md.

## Shared Kernel ⚠️

Domain, Application, and Infrastructure layers complete.

> ⚠️ **Remaining gap (deferred):**
> - **Wolverine durable outbox not configured**: Wolverine is wired and `IMessageBus` resolves correctly. Domain events are dispatched in-memory after `SaveChangesAsync`. The durable PostgreSQL outbox (survives process restart) is deferred until a decision is made on the Wolverine persistence schema strategy — tracked as E01 Platform Foundation gap.

## Identity — E02-identity-access

**Domain ✅ | Application ✅ | Infrastructure ✅ | API ✅ | Frontend ⚠️**

Full auth, user, role, invitation, and session management. OpenIddict 5.x OIDC server (Authorization Code + PKCE for SPA; Client Credentials for M2M). RBAC via custom permission policies. All Identity API endpoints covered by integration tests.

**Frontend:** PKCE sign-in (`/login`, `/callback`), route guard (`/_authenticated`), app shell, global 401 redirect — Phase 1 complete. Module screens ⏳.

## DataModeling — E03-data-modeling

**Domain ✅ | Application ✅ | Infrastructure ✅ | API ✅ | Frontend ⏳**

Custom model, field, data class, and record CRUD. Full-text search, per-field JSONB filters, sort-by-column, bulk delete, CSV export. All endpoints covered by integration tests.

## WorkflowBuilder — E04-workflow-builder

**Domain ✅ | Application ✅ | Infrastructure ✅ | API ✅ | Frontend ⏳**

Workflow definitions with steps, transitions, triggers, cycle detection, publish/archive lifecycle. Import/export (JSON + ZIP). All endpoints covered by integration tests.

## FormBuilder — E05-form-builder

**Domain ✅ | Application ✅ | Infrastructure ✅ | API ✅ | Frontend ⏳**

Form definitions with typed fields (9 field types, polymorphic config). Form submission tasks (`FormSubmission` aggregate, token-based public submit, My Tasks queries). Cross-module isolation via Wolverine event-driven local denormalization — no direct cross-module SQL. `FormStepReached` handler creates tasks when workflow hits Form steps.

## WorkflowEngine — E06-workflow-engine

**Domain ✅ | Application ✅ | Infrastructure ⚠️ | API ✅ | Frontend ⏳**

Execution lifecycle (start, cancel, retry, retry-with-context). Step state machine with per-step execution handlers (Form, HTTP, Condition, Script, Notification). `WorkflowSnapshot` local read model populated from `WorkflowPublished` events. Paged execution history and retry history queries. REST API: `/api/executions`, `/api/workflows/{id}/executions`. Infrastructure ⚠️: `IScriptExecutor` and `INotificationSender` are stubs (real dispatch deferred).

## PageBuilder — E07-page-builder

**⏳ Phase 2 — not started**

---

## Frontend Foundation

**Status: ⚠️ Phase 1 complete — feature screens ⏳**

React 18 + TypeScript + Vite. TanStack Router, TanStack Query, Zustand, shadcn/ui, Tailwind. Biome (lint + format). `npm run ci` gate enforced. Auth flow (PKCE + `/login` + `/callback`), authenticated layout with sidebar, Vite dev proxy to API.

## Platform Foundation — E01

**Tenant provisioning (US-003):** `ITenantSchemaProvisioner` runs on email verification — creates `tenant_{org_id}` schema and migrates DataModeling (EnsureCreated), WorkflowBuilder, FormBuilder, and WorkflowEngine databases. Retry/alert UI deferred.
