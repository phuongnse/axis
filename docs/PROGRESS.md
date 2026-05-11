# Implementation Progress

> Updated each time a layer is completed. Source of truth for current implementation state.
> When completing a layer, update this file — **not** CLAUDE.md.

## Shared Kernel ✅

- `Axis.Shared.Domain`: Entity, AggregateRoot, ValueObject, IDomainEvent, Result/Result<T>
- `Axis.Shared.Application`: ICommand/IQuery/ICommandHandler/IQueryHandler, ValidationBehavior, TenantContext/ITenantContext
- `Axis.Shared.Infrastructure`: AxisDbContext, TenantSchemaInterceptor, UnitOfWork, MessageBus

## Identity — E02-identity-access

**Domain ✅ | Application ✅ | Infrastructure ✅ | API ⚠️ | Frontend ⏳**

> ⚠️ **API layer has a known architectural gap**: implemented with a hand-rolled `JwtTokenService` instead of OpenIddict. The entire Identity API layer must be refactored to OpenIddict (Authorization Code + PKCE for SPA; Client Credentials for external integrations) before the module can be marked ✅. See ADR-004 in `docs/TECH_STACK.md`.

- **Domain**: Organization, User, Role, Invitation aggregates; Email, OrganizationSlug value objects; all domain events
- **Application**: RegisterOrganization, InviteUser, AcceptInvitation, DeactivateUser, AssignRoleToUser, CreateRole, UpdateRole, UpdateUserProfile; AuthenticateUser, VerifyEmail, ResendVerificationEmail, RequestPasswordReset, ResetPassword, ChangePassword, RevokeSession; GetRoles, GetUserSessions queries
- **Infrastructure**: IdentityDbContext (public schema), all EF Core configurations, all repositories, BCryptPasswordHasher (work factor 12), MailKitEmailSender, IdentityUnitOfWork, PasswordResetTokenStore, RefreshTokenStore (refresh_tokens table), SessionStoreService
- **API (current — to be replaced)**: Custom JWT via `JwtTokenService`; endpoints for signin/refresh/signout/verify-email/forgot-password/reset-password, org registration, invitations, user management, roles. PermissionPolicyProvider for RBAC; RedisJtiBlacklist; ValidationExceptionMiddleware (422). 27 integration tests passing — but these tests will need to be rewritten against the OpenIddict flow.

## DataModeling — E03-data-modeling

**Domain ✅ | Application ✅ | Infrastructure ⚠️ | API ✅ | Frontend ⏳**

> ⚠️ **Infrastructure gap — JSONB change tracking workaround**: `DataModelingDbContext.SaveChangesAsync` overrides to force-mark `_fields` as modified. This is incomplete — it only fires when the entity is already `Modified` (another scalar field changed). If only `_fields` is mutated, changes are silently lost. Fix: add `ValueComparer` to `DataModelConfiguration` and `DataClassConfiguration`, then remove `MarkJsonbFieldsModified`. See PATTERNS.md → "EF Core JSONB collection change tracking".

- **Infrastructure**: DataModelingDbContext (public), EF Core configurations (DataModel/DataClass/DataRecord), JSONB FieldDefinition converter (polymorphic FieldConfig), JSONB DataRecord._data, 3 repositories (incl. GetPagedAsync with search via `data::text ILIKE`), DataModelingUnitOfWork, integration tests (Testcontainers)
- **API**: Minimal API — `/api/models` (9 endpoints: CRUD + field management + reorder), `/api/data-classes` (7 endpoints), `/api/models/{id}/records` (5 endpoints with pagination+search). FieldConfigHelper for discriminated deserialization. Integration tests (WebApplicationFactory).

## WorkflowBuilder — E04-workflow-builder

**Domain ✅ | Application ✅ | Infrastructure ✅ | API ⏳ | Frontend ⏳**

- **Infrastructure**: WorkflowBuilderDbContext, WorkflowDefinition config (steps/transitions/triggers as JSONB with custom WorkflowStepConverter), WorkflowRepository, 7 integration tests (Testcontainers)

## FormBuilder — E05-form-builder

**Domain ✅ | Application ✅ | Infrastructure ⚠️ | API ⏳ | Frontend ⏳**

> ⚠️ **Infrastructure gap — JSONB change tracking missing**: `FormDefinitionConfiguration` maps `_fields` with `HasConversion` but no `ValueComparer`. Unlike DataModeling, FormBuilderDbContext has no workaround at all — any mutation to form fields that doesn't also change a scalar property will be silently lost. Fix: add `ValueComparer` to `FormDefinitionConfiguration`. See PATTERNS.md → "EF Core JSONB collection change tracking".

- **Infrastructure**: FormBuilderDbContext, EF Core config (FormDefinition with fields as JSONB via FormFieldConverter — 9 field types, polymorphic FormFieldConfig), FormRepository (IsReferencedByWorkflowAsync cross-module JSONB query), 8 integration tests (Testcontainers)

## WorkflowEngine — E06-workflow-engine

**Domain ✅ | Application ✅ | Infrastructure ✅ | API ⏳ | Frontend ⏳**

- **Infrastructure**: WorkflowEngineDbContext, EF Core config (WorkflowExecution with `_context` as JSONB), ExecutionRepository (4 methods: AddAsync, GetByIdAsync, GetAllAsync, GetByWorkflowAsync), WorkflowDefinitionReader (cross-module raw SQL query on `workflow_definitions.status`), WorkflowEngineUnitOfWork, 8 integration tests (Testcontainers)

## PageBuilder — E07-page-builder

**⏳ Phase 2 — not started**
