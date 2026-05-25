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
| **Phase 2 — Per-module HTTP/gRPC boundary** | ⚠️ in progress | Identity complete: `Axis.Identity.Contracts` + `IdentityService` gRPC (`GetUserPermissions`) + Avro lifecycle events (`OrganizationVerifiedEvent`, `UserDeactivatedEvent`, `UserReactivatedEvent`, `RoleAssignedEvent`, `RoleRemovedEvent`) + per-module `OrganizationVerifiedHandler` (provisions each module's tenant schema from Kafka) + gateway `IdentityServiceClient` gRPC wiring. Remaining: DataModeling/FormBuilder/WorkflowBuilder/WorkflowEngine `Axis.{Module}.Contracts` projects with their own events. |
| **Phase 3 — Per-module EF migrations** | ⚠️ in progress | Identity, DataModeling, FormBuilder, WorkflowBuilder, WorkflowEngine have migrations; tests use `MigrateAsync`. PageBuilder pending (module not started). |
| **Phase 4 — Deployment readiness** | ⏳ pending | Per-module Dockerfile; `docker-compose.dev.yml` runs each module as a separate container; CI builds per-module artifacts; K8s manifests; per-module Vault policies. |

Feature work (Frontend feature UIs, E07 PageBuilder, remaining E01/E06 gaps) is paused until Phase 1–2 complete. Estimated timeline ~2–3 months.

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

**Domain ✅ | Application ✅ | Infrastructure ✅ | API ✅ | Frontend ⏳ · Service-boundary retrofit ⏳**

Custom model, field, data class, and record CRUD. Full-text search, per-field JSONB filters, sort-by-column, bulk delete, CSV export. All endpoints covered by integration tests.

> ⏳ **Retrofit:** add `Axis.DataModeling.Contracts` (gRPC + Avro events for `ModelCreated`/`FieldAdded`/...); move to `axis_datamodeling` database; generate initial EF migration; switch tests to migrations.

## WorkflowBuilder — E04-workflow-builder

**Domain ✅ | Application ✅ | Infrastructure ✅ | API ✅ | Frontend ⏳ · Service-boundary retrofit ⏳**

Workflow definitions with steps, transitions, triggers, cycle detection, publish/archive lifecycle. Import/export (JSON + ZIP). All endpoints covered by integration tests.

> ⏳ **Retrofit:** add `Axis.WorkflowBuilder.Contracts`; existing cross-module events (`FormStepAdded`, `WorkflowPublished`, ...) become Kafka-published with Avro schemas; move to `axis_workflowbuilder` database; existing EF migrations stay but tests switch from `EnsureCreated` to `MigrateAsync`.

## FormBuilder — E05-form-builder

**Domain ✅ | Application ✅ | Infrastructure ✅ | API ✅ | Frontend ⏳ · Service-boundary retrofit ⚠️**

Form definitions + F04 form tasks (`FormSubmission`, token submit, my tasks, expiry job). Submission user resolved via `ICurrentUser` in Application.

> ✅ **Phase 2 Contracts:** Avro form-task events; `form_model_references` + `ModelDeletedHandler` (DataModeling Kafka) for Relation Picker broken refs; delete-model blocked when active form references exist. **Deferred:** gRPC service.

## WorkflowEngine — E06-workflow-engine

**Domain ✅ | Application ✅ | Infrastructure ⚠️ | API ✅ | Frontend ⏳ · Service-boundary retrofit ⚠️**

Execution lifecycle (start, cancel, retry, retry-with-context). `ExecutionEndpoints` registered; default-input shaping handled in `StartExecutionHandler`. Infrastructure ⚠️: `IScriptExecutor` and `INotificationSender` stubs.

> ⚠️ **Retrofit (PR for E05+E06 closure):** `Axis.WorkflowEngine.Contracts` shipped with Avro schema `FormStepReachedEvent` (CloudEvents envelope, ADR-019). `WorkflowEngineEventMapper` translates domain events at `SaveChangesAsync` time; FormBuilder consumes via Kafka topic `axis.workflowengine.form-step-reached`. The 2 cross-module Domain references that were tracked in `WORKAROUNDS.md` are now resolved (ratchet shrunk). **Deferred:** gRPC service for sync RPC needs (none today); saga orchestrator ([ADR-020](TECH_STACK.md#adr-020-saga-orchestration-for-cross-module-workflows)); dedicated `axis_workflowengine` database; switch tests to `MigrateAsync`; real `IScriptExecutor` + `INotificationSender`.

## Identity / E01 — tenant provisioning (cross-cutting)

**Verify email → provision (PR #93):** `User.VerifyEmail()` raises an `OrganizationVerified` domain event; `IdentityUnitOfWork` maps it to `OrganizationVerifiedEvent` (Avro) and publishes via Wolverine outbox → Kafka topic `axis.identity.organization-verified`. Each of DataModeling/FormBuilder/WorkflowBuilder/WorkflowEngine has an `OrganizationVerifiedHandler` in its own Infrastructure project that `CREATE SCHEMA IF NOT EXISTS` + `MigrateAsync` for that module's DB, serialized per-org via a Postgres advisory lock to make Kafka redelivery safe against `__EFMigrationsHistory` races. **Deferred (F01 US-003 follow-up):** retry/backoff/alert on provision failures, provisioning wait UI, Admin role on verify per E01 US-003.

## PageBuilder — E07-page-builder

**⏳ Phase 2 — not started**

---

## Frontend Foundation

**Status: ✅ Tooling complete — feature implementation ⏳**

React 18 + TypeScript + Vite. TanStack Router, TanStack Query, Zustand, shadcn/ui, Tailwind. Biome (lint + format). `npm run ci` gate enforced. All module feature UIs remain ⏳.
