# Implementation Progress

> Updated each time a layer is completed. Source of truth for current implementation state.
> When completing a layer, update this file — **not** CLAUDE.md.

## Shared Kernel ⚠️

- `Axis.Shared.Domain`: Entity, AggregateRoot, ValueObject, IDomainEvent, Result/Result<T>
- `Axis.Shared.Application`: ICommand/IQuery/ICommandHandler/IQueryHandler, ValidationBehavior, TenantContext/ITenantContext
- `Axis.Shared.Infrastructure`: AxisDbContext, TenantSchemaInterceptor, UnitOfWork, MessageBus

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

- **Infrastructure**: DataModelingDbContext (public), EF Core configurations (DataModel/DataClass/DataRecord), JSONB FieldDefinition converter (polymorphic FieldConfig), JSONB DataRecord._data, 3 repositories (incl. GetPagedAsync with search via `data::text ILIKE`), DataModelingUnitOfWork, integration tests (Testcontainers)
- **API**: Minimal API — `/api/models` (9 endpoints: CRUD + field management + reorder), `/api/data-classes` (7 endpoints), `/api/models/{id}/records` (5 endpoints with pagination+search). FieldConfigHelper for discriminated deserialization. Integration tests (WebApplicationFactory).

## WorkflowBuilder — E04-workflow-builder

**Domain ✅ | Application ✅ | Infrastructure ✅ | API ⏳ | Frontend ⏳**

- **Infrastructure**: WorkflowBuilderDbContext, WorkflowDefinition config (steps/transitions/triggers as JSONB with custom WorkflowStepConverter), WorkflowRepository, 7 integration tests (Testcontainers)

## FormBuilder — E05-form-builder

**Domain ✅ | Application ✅ | Infrastructure ✅ | API ⏳ | Frontend ⏳**

- **Infrastructure**: FormBuilderDbContext, EF Core config (FormDefinition with fields as JSONB via FormFieldConverter — 9 field types, polymorphic FormFieldConfig), FormRepository (IsReferencedByWorkflowAsync cross-module JSONB query), 8 integration tests (Testcontainers)

## WorkflowEngine — E06-workflow-engine

**Domain ✅ | Application ✅ | Infrastructure ✅ | API ⏳ | Frontend ⏳**

- **Infrastructure**: WorkflowEngineDbContext, EF Core config (WorkflowExecution with `_context` as JSONB), ExecutionRepository (4 methods: AddAsync, GetByIdAsync, GetAllAsync, GetByWorkflowAsync), WorkflowDefinitionReader (cross-module raw SQL query on `workflow_definitions.status`), WorkflowEngineUnitOfWork, 8 integration tests (Testcontainers)

## PageBuilder — E07-page-builder

**⏳ Phase 2 — not started**
