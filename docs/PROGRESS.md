# Implementation Progress

> Updated each time a layer is completed. Source of truth for current implementation state.
> When completing a layer, update this file — **not** CLAUDE.md.

## Distributed-ready foundation rollout

The project has pivoted from "modular monolith that can extract later" to **modulith with strict service boundaries** ([ADR-010](TECH_STACK.md#adr-010-modulith-with-strict-service-boundaries-so-extraction-is-a-redeploy)). The existing module implementations are functional but were built against the original assumption (shared DB, in-process events, shared kernel implementation). They must be migrated to the new contract before user-facing feature work continues.

Foundation phases (each a sequence of small PRs):

| Phase | Status | Deliverable |
|---|---|---|
| **Phase 0 — Foundation decisions** | ✅ done (PR #59) | Rewrote ADR-001/002/009; added ADR-010..023; updated `ARCHITECTURE.md` + `CLAUDE.md` + `patterns.md`. |
| **Phase 1 — Infrastructure foundation** | ✅ done | PR #83–#90: Kafka/RabbitMQ (ADR-017), per-module DBs (ADR-011), Wolverine enroll (ADR-012), migrations (ADR-023), OpenTelemetry (ADR-018), **Avro + Schema Registry + CloudEvents** for WorkflowBuilder lifecycle events (ADR-019). |
| **Phase 2 — Per-module HTTP/gRPC boundary** | ⚠️ in progress | All MVP modules have `Axis.{Module}.Contracts` + Kafka Avro events. Identity + FormBuilder gRPC services wired. WorkflowBuilder `WorkflowFormReferenceService` gRPC + `FormDeletedEvent` Kafka. **Deferred:** DataModeling gRPC. |
| **Phase 3 — Per-module EF migrations** | ⚠️ in progress | Identity, DataModeling, FormBuilder, WorkflowBuilder, WorkflowEngine have migrations; tests use `MigrateAsync`. PageBuilder pending (module not started). |
| **Phase 4 — Deployment readiness** | ⏳ pending | Per-module Dockerfile; `docker-compose.dev.yml` runs each module as a separate container; CI builds per-module artifacts; K8s manifests; per-module Vault policies. |

Feature work (Frontend feature UIs, E07 PageBuilder) tracks per-epic **Open work** in [docs/epics/README.md](./epics/README.md#how-agents-find-open-work). Foundation phases 1–2 remain ⚠️ in places; E02–E06 backend APIs are largely shipped — agents should read US **Implementation status** callouts, not epic `- [ ]` checkboxes.

### Deferred decisions — surface when triggered

| Decision | Trigger to write | Why deferred |
|---|---|---|
| **ADR-026 — Selective event sourcing** (likely `WorkflowExecution` first) | Phase 1 complete **or** team identifies a concrete aggregate that needs audit/time-travel | Premature without a concrete aggregate to design against; ADR quality benefits from implementation experience on the first conversion. Depends on Phase 1's Avro + Schema Registry (ADR-019) for event versioning. Kafka transport (PR #84) and routing rule (ADR-025) are already in place — no infra blockers, only design + implementation. The dedicated ADR + first-aggregate conversion ship together in one PR. |

## Shared Kernel ✅ (narrowed per ADR-017)

`Axis.Shared.Domain` and `Axis.Shared.Application` hold abstractions only (primitives, interfaces, Result/Error types, exception types). `Axis.Shared.Infrastructure` now contains only genuinely cross-cutting infrastructure that every module needs identically: `TenantSchemaInterceptor`, `HttpTenantContext`, `HandlerLoggingMiddleware`, and the DI registration extension. The `UnitOfWork` base class and `AxisDbContext` base class were inlined into each module's Infrastructure project per [ADR-017](TECH_STACK.md#adr-017-axisshared-is-abstractions-only-no-shared-implementation) — explicit per-module ownership over magical inheritance.

## Identity — E02-identity-access

**Domain ✅ | Application ✅ | Infrastructure ✅ | API ✅ | Frontend ⏳ · Service-boundary retrofit ✅**

Full auth, user, role, invitation, and session management. OpenIddict 5.x OIDC server (Authorization Code + PKCE for SPA; Client Credentials for M2M). RBAC via custom permission policies. All Identity API endpoints covered by integration tests.

> ✅ **Phase 2 complete (PR #93):** `Axis.Identity.Contracts` with `IdentityService.GetUserPermissions` gRPC + 5 Avro lifecycle event schemas (`OrganizationVerifiedEvent`, `UserDeactivatedEvent`, `UserReactivatedEvent`, `RoleAssignedEvent`, `RoleRemovedEvent`) published via Wolverine outbox → Kafka with CloudEvents envelope (ADR-019). Each of DataModeling/FormBuilder/WorkflowBuilder/WorkflowEngine subscribes to `OrganizationVerifiedEvent` and provisions its own tenant schema (central `TenantSchemaProvisioner` and `ProvisionTenantMessage` removed — extraction is now a redeploy per ADR-010). Gateway uses `AddGrpcClient<IdentityService.IdentityServiceClient>` with `Modules:Identity:GrpcUrl` config; JWKS-only validation rule documented in [patterns.md § Pattern 3](playbooks/patterns.md#-pattern-3-jwks-only-jwt-validation-in-consuming-modules).

## DataModeling — E03-data-modeling

**Domain ✅ | Application ✅ | Infrastructure ✅ | API ✅ | Frontend ⏳ · Service-boundary retrofit ✅**

Custom model, field, data class, and record CRUD. Full-text search, per-field JSONB filters, sort-by-column, bulk delete, CSV export. All endpoints covered by integration tests.

> ✅ **Contracts + Kafka:** `Axis.DataModeling.Contracts` publishes lifecycle events; FormBuilder + WorkflowBuilder consume `ModelDeletedEvent` for broken refs. **Deferred:** relation fields on other models flagged broken when target model deleted.

## WorkflowBuilder — E04-workflow-builder

**Domain ✅ | Application ✅ | Infrastructure ✅ | API ✅ | Frontend ⏳ · Service-boundary retrofit ⚠️**

Workflow definitions with steps, transitions, triggers, cycle detection, publish/archive lifecycle. Import/export (JSON + ZIP). All endpoints covered by integration tests.

> ✅ **Broken refs:** `workflow_form_references` + `workflow_model_references` read models; sync on step/trigger changes; `ModelDeletedHandler` + `FormDeletedHandler` (Kafka); publish blocked when broken; `GetWorkflow` exposes `isBroken`; `WorkflowFormReferenceService` gRPC for form delete guard (draft + active workflows).

## FormBuilder — E05-form-builder

**Domain ✅ | Application ✅ | Infrastructure ✅ | API ✅ | Frontend ⏳ · Service-boundary retrofit ⚠️**

Form definitions + F04 form tasks (`FormSubmission`, token submit, my tasks, expiry job). Submission user resolved via `ICurrentUser` in Application.

> ✅ **Phase 2 Contracts:** Avro form-task + `FormDeletedEvent`; `form_model_references` + `ModelDeletedHandler`; delete-model guard via FormBuilder gRPC; delete-form guard via WorkflowBuilder gRPC (`WorkflowFormReferenceService`).

## WorkflowEngine — E06-workflow-engine

**Domain ✅ | Application ✅ | Infrastructure ⚠️ | API ✅ | Frontend ⏳ · Service-boundary retrofit ⚠️**

Execution lifecycle (start, cancel, retry, retry-with-context). `ExecutionEndpoints` registered; default-input shaping handled in `StartExecutionHandler`. Infrastructure ⚠️: `IScriptExecutor` and `INotificationSender` stubs.

> ⚠️ **Retrofit (PR for E05+E06 closure):** `Axis.WorkflowEngine.Contracts` shipped with Avro schema `FormStepReachedEvent` (CloudEvents envelope, ADR-019). `WorkflowEngineEventMapper` translates domain events at `SaveChangesAsync` time; FormBuilder consumes via Kafka topic `axis.workflowengine.form-step-reached`. The 2 cross-module Domain references that were tracked in `WORKAROUNDS.md` are now resolved (ratchet shrunk). **Deferred:** gRPC service for sync RPC needs (none today); saga orchestrator ([ADR-020](TECH_STACK.md#adr-020-saga-orchestration-for-cross-module-workflows)); dedicated `axis_workflowengine` database; switch tests to `MigrateAsync`; real `IScriptExecutor` + `INotificationSender`.

## E01 — Platform Foundation

**F01 tenant registration (backend):** ✅ register, verify (opaque tokens), resend limit, idempotency, Kafka provisioning coordinator, optional `subscriptionPlanId` on register.

**F04 subscription plans (backend):** ⚠️ `GET /api/plans`, platform plan change, 402 limits (workflows / users / executions), Redis counters. Frontend pricing UI ⏳. Bulk multi-workflow import limit AC deferred until API exists.

**F02 organization management:** ⏳ not started (no profile/settings/delete APIs).

**F03 tenant isolation:** ⚠️ `TenantSchemaInterceptor` + `ITenantContext` shipped; cross-tenant integration tests and org-status 403 gaps — see [E01 F03](./epics/E01-platform-foundation/features/F03-tenant-isolation.md).

**Agents:** per-US truth in feature `Implementation status` callouts; epic [Open work](./epics/E01-platform-foundation/README.md#open-work-agents) lists next backend/frontend items.

## Identity / E01 — tenant provisioning (cross-cutting)

**Verify email → provision:** `User.VerifyEmail()` sets org `Provisioning`, seeds `tenant_module_provisions`, publishes `OrganizationVerifiedEvent` → each module provisions and reports via `TenantModuleProvisionReportEvent`; Identity coordinator retries (3×, exponential backoff) and logs critical alert on exhaustion; `GET /api/auth/provisioning-status?token=` for polling. **Deferred:** provisioning wait UI (Frontend).

## PageBuilder — E07-page-builder

**⏳ Phase 2 — not started**

---

## Frontend Foundation

**Status: ✅ Tooling complete — feature implementation ⏳**

React 18 + TypeScript + Vite. TanStack Router, TanStack Query, Zustand, shadcn/ui, Tailwind. Biome (lint + format). `npm run ci` gate enforced. All module feature UIs remain ⏳.
