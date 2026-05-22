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

**Domain ✅ | Application ⚠️ | Infrastructure ✅ | API ⚠️ | Frontend ⏳**

Form definitions + F04 form tasks (`FormSubmission`, token submit, my tasks, expiry job). **PR #50 deferred:** move `submittedBy` parsing from `FormTaskEndpoints` to Application.

## WorkflowEngine — E06-workflow-engine

**Domain ✅ | Application ✅ | Infrastructure ⚠️ | API ⚠️ | Frontend ⏳**

Execution lifecycle (start, cancel, retry, retry-with-context). `ExecutionEndpoints` registered. **PR #50 deferred:** thin-endpoint refactor for `StartExecution` request shaping. Infrastructure ⚠️: `IScriptExecutor` and `INotificationSender` stubs.

## Identity / E01 — tenant provisioning (cross-cutting)

**Verify email → provision:** `VerifyEmailHandler` saves verified state, then enqueues `ProvisionTenantMessage` (Wolverine → `ProvisionTenantHandler` + `ITenantSchemaProvisioner`). **Deferred:** retry/backoff/alert on provision failures, provisioning wait UI, Admin role on verify per E01 US-003.

## PageBuilder — E07-page-builder

**⏳ Phase 2 — not started**

---

## Frontend Foundation

**Status: ✅ Tooling complete — feature implementation ⏳**

React 18 + TypeScript + Vite. TanStack Router, TanStack Query, Zustand, shadcn/ui, Tailwind. Biome (lint + format). `npm run ci` gate enforced. All module feature UIs remain ⏳.
