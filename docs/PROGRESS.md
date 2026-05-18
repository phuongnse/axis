# Implementation Progress

> Updated each time a layer is completed. Source of truth for current implementation state.
> When completing a layer, update this file — **not** CLAUDE.md.

## Shared Kernel ⚠️

- `Axis.Shared.Domain`: Entity, AggregateRoot, ValueObject, IDomainEvent, Result/Result<T>
- `Axis.Shared.Application`: ICommand/IQuery/ICommandHandler/IQueryHandler, ValidationBehavior, TenantContext/ITenantContext
- `Axis.Shared.Infrastructure`: AxisDbContext, TenantSchemaInterceptor, UnitOfWork (uses Wolverine's `IMessageBus` framework interface — no custom `MessageBus` class)

> ⚠️ **Remaining gap (deferred):**
> - **Wolverine durable outbox not configured**: Wolverine is wired (`UseWolverine` + `UseEntityFrameworkCoreTransactions`) and `IMessageBus` resolves correctly. Domain events are dispatched in-memory after `SaveChangesAsync`. The durable PostgreSQL outbox (survives process restart) is deferred until a decision is made on the Wolverine persistence schema strategy — tracked as E01 Platform Foundation gap.

## Identity — E02-identity-access

**Domain ✅ | Application ✅ | Infrastructure ✅ | API ✅ | Frontend ⏳**

- **Domain**: Organization, User, Role, Invitation aggregates; Email, OrganizationSlug value objects; all domain events
- **Application**: RegisterOrganization, InviteUser, AcceptInvitation, DeactivateUser, AssignRoleToUser, CreateRole, UpdateRole, UpdateUserProfile; AuthenticateUser, VerifyEmail, ResendVerificationEmail, RequestPasswordReset, ResetPassword, ChangePassword, RevokeSession; GetRoles, GetUserSessions queries
- **Infrastructure**: IdentityDbContext (public schema), all EF Core configurations, all repositories, BCryptPasswordHasher (work factor 12), MailKitEmailSender, IdentityUnitOfWork, PasswordResetTokenStore, SessionStoreService (wraps `IOpenIddictTokenManager`), OpenIddictSeeder (`axis_spa` PKCE + `axis_m2m` client credentials clients)
- **API**: OpenIddict 5.x — `GET /connect/authorize` (Authorization Code + PKCE), `POST /connect/login` (credential validation + 5-min session cookie), `POST /connect/token` (code exchange / refresh / client credentials). Refresh token in httpOnly `Secure SameSite=Strict` cookie via `ApplyRefreshTokenCookieHandler`/`ExtractRefreshTokenFromCookieHandler`. `POST /api/auth/signout` (revoke via `IOpenIddictTokenManager` + JTI Redis blacklist). PermissionPolicyProvider (OpenIddict validation scheme). Full PKCE integration test helper (`AuthHelper.CompletePkceFlowAsync`). 11 auth integration tests.

## DataModeling — E03-data-modeling

**Domain ✅ | Application ✅ | Infrastructure ✅ | API ✅ | Frontend ⏳**

- **Infrastructure**: DataModelingDbContext (public schema), EF Core configurations (DataModel/DataClass/DataRecord), JSONB FieldDefinition converter (polymorphic FieldConfig), JSONB DataRecord._data, 3 repositories. `GetPagedAsync` supports full-text search (`data::text ILIKE`), per-field JSONB filters (eq/contains/gt/lt/isEmpty/isNotEmpty combined with AND), and custom sort-by-column. `BulkDeleteAsync` (single UPDATE statement). `GetAllForExportAsync` (streamed in 500-record chunks). DataModelingUnitOfWork. Integration tests (Testcontainers).
- **Application**: All command/query handlers including `BulkDeleteRecordsHandler` and `ExportRecordsCsvHandler` (US-046). `RecordFieldValidator` service (US-035). `Result.FieldValidation` factory + `ErrorCodes.FieldValidation` (US-035).
- **API**: Minimal API — `/api/models` (9 endpoints), `/api/data-classes` (7 endpoints), `/api/models/{id}/records` (7 endpoints: CRUD + bulk-delete `POST /bulk-delete` + CSV export `GET /export`, with pagination/search/`?filter=field:op:value`/sort). HTTP 422 `ValidationProblemDetails` on record create/update (US-035). Integration tests (WebApplicationFactory).

## WorkflowBuilder — E04-workflow-builder

**Domain ✅ | Application ✅ | Infrastructure ✅ | API ✅ | Frontend ⏳**

- **Domain**: WorkflowDefinition aggregate with Start/End nodes, step/transition/trigger management, cycle detection (DFS), ConfigureStep method, AddTrigger duplicate-type guard, Duplicate() deep-copy. All domain events.
- **Application**: All 15 command/query handlers — CreateWorkflow, PublishWorkflow, ArchiveWorkflow, UnarchiveWorkflow, UpdateWorkflow, DuplicateWorkflow, AddStep, RemoveStep, ConfigureStep, AddTransition, RemoveTransition, AddTrigger, RemoveTrigger, ImportWorkflow, BulkExportWorkflows; GetWorkflows (paged), GetWorkflow (by ID), ExportWorkflow (with credential scrubbing) queries.
- **Infrastructure**: WorkflowBuilderDbContext, WorkflowDefinition config (steps/transitions/triggers as JSONB with custom WorkflowStepConverter), WorkflowRepository, 7 integration tests (Testcontainers)
- **API**: 18 Minimal API endpoints — `GET/POST /api/workflows`, `GET/PUT /api/workflows/{id}`, `POST /{id}/publish`, `POST /{id}/archive`, `POST /{id}/unarchive`, `POST /{id}/duplicate`, `GET /{id}/export` (JSON download), `POST /import`, `GET /export-all` (ZIP), step CRUD (`POST/PUT/DELETE /{id}/steps/{stepId}`), transition management (`POST/DELETE /{id}/transitions`), trigger management (`POST /{id}/triggers`, `DELETE /{id}/triggers/{type}`). NullWorkflowBuilderUnitOfWork + WorkflowBuilderDbContext EnsureCreatedAsync added to ApiTestFixture. 17 API integration tests.

## FormBuilder — E05-form-builder

**Domain ✅ | Application ✅ | Infrastructure ✅ | API ⏳ | Frontend ⏳**

- **Domain**: FormDefinition, FormField aggregates; all field types and domain events. FormSubmission aggregate (Pending/Submitted/Expired/Cancelled state machine; AccessToken Guid; FormTaskCreated/Submitted/Expired/Cancelled events).
- **Application**: CreateForm, DeleteForm, UpdateForm commands; GetForms, GetFormById queries; AddFieldToForm, RemoveFieldFromForm, ReorderFormFields commands. Decision: GetFormsHandler uses in-memory pagination (accepted MVP trade-off; Page/PageSize inputs clamped to valid range).
- **Infrastructure**: FormBuilderDbContext, EF Core config (FormDefinition with fields as JSONB via FormFieldConverter — 9 field types, polymorphic FormFieldConfig), FormRepository (IsReferencedByWorkflowAsync cross-module JSONB query), 8 integration tests (Testcontainers)

## WorkflowEngine — E06-workflow-engine

**Domain ✅ | Application ✅ | Infrastructure ✅ | API ⏳ | Frontend ⏳**

- **Domain**: WorkflowExecution aggregate with execution state machine, domain events, and step management methods (AddStep/StartStep/CompleteStep/FailStep/WaitStep/SkipStep/CancelStep). ExecutionStep as owned Entity<Guid> (Pending/Running/Waiting/Completed/Failed/Skipped/Cancelled state machine; InputSnapshot/OutputSnapshot; IsTerminal for idempotency; StepType enum — events raised by WorkflowExecution, not ExecutionStep). `CreateRetryWithModifiedContext` added for US-102.
- **Application**: StartExecution, CancelExecution, RetryExecution, RetryExecutionWithContext commands. GetExecution (with step timeline), GetExecutionsByWorkflow (paged + status filter), GetAllExecutions (paged + status filter), GetRetryHistory queries. DTOs: ExecutionSummaryResponse, ExecutionStepResponse, ExecutionResponse.
- **Infrastructure**: WorkflowEngineDbContext (WorkflowExecution only — ExecutionStep is an owned entity, no standalone DbSet), EF Core config (WorkflowExecution `_context` as JSONB; ExecutionStep InputSnapshot/OutputSnapshot as JSONB, mapped via `OwnsMany` with `WithOwner().HasForeignKey(s => s.ExecutionId)`), ExecutionRepository (8 methods including paginated projection queries + GetWithStepsAsync via `Include(e => e.Steps)` + GetRetriesAsync), WorkflowDefinitionReader, WorkflowEngineUnitOfWork. EF migration `AddExecutionSteps` (creates `workflow_executions` + `execution_steps` tables with FK `ExecutionId → workflow_executions.Id`). 8 existing + 10 new integration tests (Testcontainers — require Docker).

## PageBuilder — E07-page-builder

**⏳ Phase 2 — not started**

---

## Frontend Foundation

**Status: ✅ Tooling complete — feature implementation ⏳**

- **Project**: `frontend/` at repo root (React 18 + TypeScript 6 + Vite 5)
- **Routing**: TanStack Router with file-based routes in `frontend/src/routes/`
- **HTTP client**: `fetchApi` wrapper (`frontend/src/lib/api.ts`) — timeout, `credentials: include`, `ApiError` typed with `unknown` data
- **Build gate**: `npm run ci` = `tsc -b --noEmit && biome ci .` — zero TypeScript errors + zero Biome errors/warnings required before every push
- **Linting + formatting**: Biome 2.x (replaces ESLint + Prettier) — `frontend/biome.json`; Tailwind directives handled via `css.parser.tailwindDirectives: true` + `overrides` suppressing `noUnknownAtRules` for CSS only
- **TypeScript**: `strict: true` enforced in both `tsconfig.app.json` and `tsconfig.node.json`
- **Tests**: Vitest 3.x + `@testing-library/react` — `npm run test`; 11 tests passing

All module frontend layers remain **⏳** — no feature UI implemented yet.
