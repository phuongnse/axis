# Implementation Progress

> **Navigation**: [← docs/README.md](./README.md) · [← AGENTS.md](../AGENTS.md)

> Updated each time a layer is completed. Source of truth for current implementation state.
> When completing a layer, update this file — **not** AGENTS.md.

## Distributed-ready foundation rollout

The project has pivoted from "modular monolith that can extract later" to **modulith with strict service boundaries** ([ADR-010](TECH_STACK.md#adr-010-modulith-with-strict-service-boundaries-so-extraction-is-a-redeploy)). The existing module implementations are functional but were built against the original assumption (shared DB, in-process events, shared kernel implementation). They must be migrated to the new contract before user-facing feature work continues.

Foundation phases (engineering rollout labels — not a product scope cut; each a sequence of small PRs):

| Phase | Status | Deliverable |
|---|---|---|
| **Phase 0 — Foundation decisions** | ✅ done | Rewrote ADR-001/002/009; added ADR-010..023; updated `ARCHITECTURE.md` + `AGENTS.md` + `patterns.md`. |
| **Phase 1 — Infrastructure foundation** | ✅ done | Kafka/RabbitMQ (ADR-017), per-module DBs (ADR-011), Wolverine enroll (ADR-012), migrations (ADR-023), OpenTelemetry (ADR-018), **Avro + Schema Registry + CloudEvents** for WorkflowBuilder lifecycle events (ADR-019). |
| **Phase 2 — Per-module HTTP/gRPC boundary** | ✅ done | All core modules have `Axis.{Module}.Contracts` + Kafka Avro events. Identity (`IdentityService`), FormBuilder (`FormModelReferenceService`), WorkflowBuilder (`WorkflowFormReferenceService`), and DataModeling (`DataModelCatalogService`) gRPC services are wired behind module boundaries. |
| **Phase 3 — Per-module EF migrations** | ⚠️ in progress | Identity, DataModeling, FormBuilder, WorkflowBuilder, WorkflowEngine have migrations; tests use `MigrateAsync`. PageBuilder pending (module not started). |
| **Phase 4 — Deployment readiness** | ⏳ pending | Per-module Dockerfile; `docker-compose.dev.yml` runs each module as a separate container; CI builds per-module artifacts; K8s manifests; per-module Vault policies. |

Feature work (frontend feature UIs, page-builder) tracks per-domain **Open work** in [docs/use-cases/README.md](./use-cases/README.md#how-agents-find-open-work). Foundation phases 1–2 remain ⚠️ in places; identity-access through workflow-engine backend APIs are largely shipped — agents should read use-case **Implementation status** callouts, not domain `- [ ]` checkboxes.

### Deferred decisions — surface when triggered

| Decision | Trigger to write | Why deferred |
|---|---|---|
| **ADR-026 — Selective event sourcing** (likely `WorkflowExecution` first) | Phase 1 complete **or** team identifies a concrete aggregate that needs audit/time-travel | Premature without a concrete aggregate to design against; ADR quality benefits from implementation experience on the first conversion. Depends on Phase 1's Avro + Schema Registry (ADR-019) for event versioning. Kafka transport and the routing rule (ADR-025) are already in place — no infra blockers, only design + implementation. The dedicated ADR + first-aggregate conversion should ship together. |

## Shared Kernel ✅ (narrowed per ADR-017)

`Axis.Shared.Domain` and `Axis.Shared.Application` hold abstractions only (primitives, interfaces, Result/Error types, exception types). `Axis.Shared.Infrastructure` now contains only genuinely cross-cutting infrastructure that every module needs identically: `TenantSchemaInterceptor`, `HttpTenantContext`, `HandlerLoggingMiddleware`, and the DI registration extension. The `UnitOfWork` base class and `AxisDbContext` base class were inlined into each module's Infrastructure project per [ADR-017](TECH_STACK.md#adr-017-axisshared-is-abstractions-only-no-shared-implementation) — explicit per-module ownership over magical inheritance.

## Identity (`identity-access`)

**Domain ✅ | Application ✅ | Infrastructure ✅ | API ✅ | Frontend ⏳ · Service-boundary retrofit ✅**

Full auth, user, role, invitation, and session management. OpenIddict 5.x OIDC server (Authorization Code + PKCE for SPA; Client Credentials for M2M). RBAC via custom permission policies. All Identity API endpoints covered by integration tests.

> ✅ **Frontend preference foundation:** [language](./use-cases/identity-access/language/) and [theme](./use-cases/identity-access/theme/) now cover EN/VI locale switching plus light/dark/system mode for the current SPA shell.

> ✅ **Phase 2 complete:** `Axis.Identity.Contracts` with `IdentityService.GetUserPermissions` gRPC + 5 Avro lifecycle event schemas published via Wolverine outbox → Kafka with CloudEvents envelope (ADR-019). Each of DataModeling/FormBuilder/WorkflowBuilder/WorkflowEngine subscribes to `OrganizationVerifiedEvent` and provisions its own tenant schema (central `TenantSchemaProvisioner` and `ProvisionTenantMessage` removed — extraction is now a redeploy per ADR-010). Gateway uses `AddGrpcClient<IdentityService.IdentityServiceClient>` with `Modules:Identity:GrpcUrl` config; JWKS-only validation rule documented in [patterns.md § Pattern 3](playbooks/patterns.md#-pattern-3-jwks-only-jwt-validation-in-consuming-modules).

## Data Modeling (`data-modeling`)

**Domain ✅ | Application ✅ | Infrastructure ✅ | API ✅ | Frontend ⏳ · Service-boundary retrofit ✅**

Custom model, field, data class, and record CRUD. Full-text search, per-field JSONB filters, sort-by-column, bulk delete, CSV export. All endpoints covered by integration tests.

> ✅ **Contracts + Kafka:** `Axis.DataModeling.Contracts` publishes lifecycle events; FormBuilder + WorkflowBuilder consume `ModelDeletedEvent` for broken refs. **Deferred:** relation fields on other models flagged broken when target model deleted.

## Workflow Builder (`workflow-builder`)

**Domain ✅ | Application ✅ | Infrastructure ✅ | API ✅ | Frontend ⏳ · Service-boundary retrofit ✅**

Workflow definitions with steps, transitions, triggers, cycle detection, publish/archive lifecycle. Import/export (JSON + ZIP). All endpoints covered by integration tests.

> ✅ **Broken refs:** `workflow_form_references` + `workflow_model_references` read models; sync on step/trigger changes; `ModelDeletedHandler` + `FormDeletedHandler` (Kafka); publish blocked when broken; `GetWorkflow` exposes `isBroken`; `WorkflowFormReferenceService` gRPC for form delete guard (draft + active workflows).

## Form Builder (`form-builder`)

**Domain ✅ | Application ✅ | Infrastructure ✅ | API ✅ | Frontend ⏳ · Service-boundary retrofit ✅**

Form definitions + form tasks (`FormSubmission`, token submit, my tasks, expiry job). Submission user resolved via `ICurrentUser` in Application.

> ✅ **Phase 2 Contracts:** Avro form-task + `FormDeletedEvent`; `form_model_references` + `ModelDeletedHandler`; delete-model guard via FormBuilder gRPC; delete-form guard via WorkflowBuilder gRPC (`WorkflowFormReferenceService`).

## Workflow Engine (`workflow-engine`)

**Domain ✅ | Application ✅ | Infrastructure ⚠️ | API ✅ | Frontend ⏳ · Service-boundary retrofit ✅**

Execution lifecycle (start, cancel, retry, retry-with-context). `ExecutionEndpoints` registered; default-input shaping handled in `StartExecutionHandler`. Database `axis_workflowengine` with EF migrations; tests use `MigrateAsync`. Infrastructure ⚠️: real `IScriptExecutor` and `INotificationSender` still stubs.

> ✅ **Retrofit:** `Axis.WorkflowEngine.Contracts` with Avro `FormStepReachedEvent` (CloudEvents, ADR-019). `WorkflowEngineEventMapper` + Kafka outbox; FormBuilder consumes `axis.workflowengine.form-step-reached`. Cross-module Domain references in `WORKAROUNDS.md` resolved. **Deferred:** gRPC (no sync RPC needs today); saga orchestrator ([ADR-020](TECH_STACK.md#adr-020-saga-orchestration-for-cross-module-workflows)); trigger handlers (schedule/webhook/event); SignalR live updates; error-notification dispatch.

## Platform Foundation (`platform-foundation`)

**Tenant registration:** ✅ `register-org` owns organization contact email verification + tenant provisioning. Dedicated `/register/organization` frontend, slug preview, legal versions, confirmation/resend, verify-email states, provisioning status/retry UI, and first-owner `/register?setupToken=...` handoff are shipped. Standalone email/password user registration is complete in `identity-access/register-user`; third-party provider registration/linking remains a separate Identity follow-up.

**Subscription plans (backend):** ✅ `GET /api/plans`, platform plan change, 402 limits (workflows / users / executions), Redis counters. Frontend pricing UI ⏳. **Deferred:** atomic execution counter under concurrency; fail-closed when Redis unavailable; bulk multi-workflow import limit AC until bulk endpoint exists.

**Organization management (backend):** ✅ profile API, settings + usage, scheduled deletion with 30-day hard-delete job. Frontend ⏳.

**Tenant isolation:** ✅ `TenantSchemaInterceptor`, `TenantOrganizationAccessMiddleware` (403 for missing/archived/not-ready orgs), cross-tenant API integration tests — see [tenant isolation](./use-cases/platform-foundation/tenant-scope/).

**Agents:** per-use-case truth in **Implementation status** callouts; domain [Open work](./use-cases/platform-foundation/README.md#open-work-agents) lists next backend/frontend items.

## Registration journey (cross-cutting)

**Register org (sign-up → verify → provision):** spec in [register-org](./use-cases/platform-foundation/register-org/README.md). **Verify → async provision:** `User.VerifyEmail()` sets org `Provisioning`, seeds `tenant_module_provisions`, publishes `OrganizationVerifiedEvent` → each module provisions and reports via `TenantModuleProvisionReportEvent`; Identity coordinator retries (3×, exponential backoff) and logs critical alert on exhaustion; `GET /api/auth/provisioning-status?token=` for polling. **Frontend ✅:** `/register/organization`, `/register/confirmation`, `/auth/verify`, first-owner `/register?setupToken=...`, and `/provisioning` screens are shipped with retry support.

## Page Builder (`page-builder`)

**⏳ Not started** — see [page-builder use cases](./use-cases/page-builder/README.md).

---

## Frontend Foundation

**Status: ✅ Tooling complete — feature implementation ⏳**

React 19 + TypeScript + Vite. TanStack Router, TanStack Query, Zustand, i18next/react-i18next, shadcn/ui, Tailwind. Biome (lint + format). `npm run ci` gate enforced. All module feature UIs remain ⏳.
