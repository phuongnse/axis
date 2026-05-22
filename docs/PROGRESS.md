# Implementation Progress

> Updated each time a layer is completed. Source of truth for current implementation state.
> When completing a layer, update this file — **not** CLAUDE.md.

## Distributed-ready foundation rollout

The project has pivoted from "modular monolith that can extract later" to **modulith with strict service boundaries** ([ADR-010](TECH_STACK.md#adr-010-modulith-with-strict-service-boundaries-so-extraction-is-a-redeploy)). The existing module implementations are functional but were built against the original assumption (shared DB, in-process events, shared kernel implementation). They must be migrated to the new contract before user-facing feature work continues.

Foundation phases (each a sequence of small PRs):

| Phase | Status | Deliverable |
|---|---|---|
| **Phase 0 — Foundation decisions** | ⚠️ in progress (this PR) | Rewrite ADR-001/002/009; add ADR-010..023; update `ARCHITECTURE.md` + `CLAUDE.md` + `patterns.md`. No code change. |
| **Phase 1 — Infrastructure foundation** | ⏳ pending | Add Kafka + Schema Registry + Vault to `docker-compose.dev.yml`; wire WolverineFx.Kafka transport; refactor `Axis.Shared` to abstractions only; per-module DB connection separation + per-module Wolverine schema; OpenTelemetry instrumentation. |
| **Phase 2 — Per-module HTTP/gRPC boundary** | ⏳ pending | One PR per module: introduce `Axis.{Module}.Contracts` (proto + Avro); expose gRPC server; rewrite `Axis.Api` to call modules through gRPC clients; replace in-process service interfaces. Identity goes first. |
| **Phase 3 — Per-module EF migrations** | ⏳ pending | Generate initial migrations for Identity / DataModeling / PageBuilder (already done for FormBuilder / WorkflowBuilder / WorkflowEngine); switch test fixtures from `EnsureCreatedAsync` to `MigrateAsync` per [ADR-023](TECH_STACK.md#adr-023-per-module-ef-core-migrations-only). |
| **Phase 4 — Deployment readiness** | ⏳ pending | Per-module Dockerfile; `docker-compose.dev.yml` runs each module as a separate container; CI builds per-module artifacts; K8s manifests; per-module Vault policies. |

Feature work (Frontend feature UIs, E07 PageBuilder, remaining E01/E06 gaps) is paused until Phase 1–2 complete. Estimated timeline ~2–3 months.

## Shared Kernel ⚠️ (scope shrinking under ADR-017)

Current state: Domain, Application, and Infrastructure layers exist. Under [ADR-017](TECH_STACK.md#adr-017-axisshared-is-abstractions-only-no-shared-implementation), `Axis.Shared.Infrastructure` is being narrowed to genuinely cross-cutting abstractions only; UnitOfWork base class, tenant-schema interceptor, and Wolverine middleware will move into the modules that own them. The migration ships incrementally with Phase 1 PRs.

## Identity — E02-identity-access

**Domain ✅ | Application ✅ | Infrastructure ✅ | API ✅ | Frontend ⏳ · Service-boundary retrofit ⏳**

Full auth, user, role, invitation, and session management. OpenIddict 5.x OIDC server (Authorization Code + PKCE for SPA; Client Credentials for M2M). RBAC via custom permission policies. All Identity API endpoints covered by integration tests.

> ⏳ **Retrofit under [ADR-010](TECH_STACK.md#adr-010-modulith-with-strict-service-boundaries-so-extraction-is-a-redeploy):** introduce `Axis.Identity.Contracts` (gRPC `IdentityService` + Avro events for user/role lifecycle); expose JWKS for cross-module token validation; remove cross-module DbContext access from other modules' code; move Identity DbContext to its own `axis_identity` database; switch tests from `EnsureCreated` to migrations. Runs in Phase 2 — Identity goes first because every other module validates JWTs against it.

## DataModeling — E03-data-modeling

**Domain ✅ | Application ✅ | Infrastructure ✅ | API ✅ | Frontend ⏳ · Service-boundary retrofit ⏳**

Custom model, field, data class, and record CRUD. Full-text search, per-field JSONB filters, sort-by-column, bulk delete, CSV export. All endpoints covered by integration tests.

> ⏳ **Retrofit:** add `Axis.DataModeling.Contracts` (gRPC + Avro events for `ModelCreated`/`FieldAdded`/...); move to `axis_datamodeling` database; generate initial EF migration; switch tests to migrations.

## WorkflowBuilder — E04-workflow-builder

**Domain ✅ | Application ✅ | Infrastructure ✅ | API ✅ | Frontend ⏳ · Service-boundary retrofit ⏳**

Workflow definitions with steps, transitions, triggers, cycle detection, publish/archive lifecycle. Import/export (JSON + ZIP). All endpoints covered by integration tests.

> ⏳ **Retrofit:** add `Axis.WorkflowBuilder.Contracts`; existing cross-module events (`FormStepAdded`, `WorkflowPublished`, ...) become Kafka-published with Avro schemas; move to `axis_workflowbuilder` database; existing EF migrations stay but tests switch from `EnsureCreated` to `MigrateAsync`.

## FormBuilder — E05-form-builder

**Domain ✅ | Application ✅ | Infrastructure ✅ | API ✅ | Frontend ⏳ · Service-boundary retrofit ⏳**

Form definitions + F04 form tasks (`FormSubmission`, token submit, my tasks, expiry job). Submission user resolved via `ICurrentUser` in Application.

> ⏳ **Retrofit:** add `Axis.FormBuilder.Contracts`; existing `FormStepReachedHandler` / `FormTaskSubmittedHandler` cross-module flow moves to Kafka transport (current Wolverine in-process pub becomes Kafka producer/consumer); move to `axis_formbuilder` database; tests switch to migrations.

## WorkflowEngine — E06-workflow-engine

**Domain ✅ | Application ✅ | Infrastructure ⚠️ | API ✅ | Frontend ⏳ · Service-boundary retrofit ⏳**

Execution lifecycle (start, cancel, retry, retry-with-context). `ExecutionEndpoints` registered; default-input shaping handled in `StartExecutionHandler`. Infrastructure ⚠️: `IScriptExecutor` and `INotificationSender` stubs.

> ⏳ **Retrofit:** add `Axis.WorkflowEngine.Contracts`; saga orchestrator for execution-step coordination ([ADR-020](TECH_STACK.md#adr-020-saga-orchestration-for-cross-module-workflows)); cross-module FormTask flow goes via Kafka; move to `axis_workflowengine` database; tests switch to migrations.

## Identity / E01 — tenant provisioning (cross-cutting)

**Verify email → provision:** `VerifyEmailHandler` saves verified state, then enqueues `ProvisionTenantMessage` (Wolverine → `ProvisionTenantHandler` + `ITenantSchemaProvisioner`). Under the new architecture this flow becomes: Identity publishes `OrganizationVerified` to Kafka; each module subscribes and provisions its own tenant schema in its own DB. **Deferred:** retry/backoff/alert on provision failures, provisioning wait UI, Admin role on verify per E01 US-003 — these land in Phase 2 alongside the per-module Kafka subscribers.

## PageBuilder — E07-page-builder

**⏳ Phase 2 — not started**

---

## Frontend Foundation

**Status: ✅ Tooling complete — feature implementation ⏳**

React 18 + TypeScript + Vite. TanStack Router, TanStack Query, Zustand, shadcn/ui, Tailwind. Biome (lint + format). `npm run ci` gate enforced. All module feature UIs remain ⏳.
