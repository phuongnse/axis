# Identity & Access Management

> **Navigation**: [← Use Cases](../README.md) · [← docs/README.md](../../README.md)

---

## Overview

Provide secure authentication and a flexible role-based access control (RBAC) system. Users belong to a team account, hold one or more roles, and each role grants a set of permissions. All identity data is tenant-scoped.

> **API contract → typed SPA:** the identity REST endpoints are the source of truth for the SPA's TypeScript types. `Axis.Api` emits `openapi.json` (verified in sync by `OpenApiDocumentTests`), which is compiled to `frontend/src/lib/api-types.ts`, so request/response shapes (registration, legal versions, slug preview, …) never drift from the wire. Regenerate with `npm run gen:api-types`.

## Business Value

Security and access control are non-negotiable for a SaaS product. Team accounts need confidence that their users see only what they should see.

## Use Cases

### Authentication

| Use case | Summary |
|---|---|
| [Change password while signed in](change-password/) | Change my password while signed in so that I can keep my account secure. |
| [Reset forgotten password](reset-password/) | Reset my password via email so that I can regain access to my account if I forget it. |
| [View and revoke active sessions](sessions/) | See where I'm currently signed in so that I can revoke access from devices I no longer use. |
| [Sign in to the workspace](sign-in/) | Sign in with my email and password — or with Microsoft, Google, or GitHub — so that I can access my team account's… |
| [Sign out](sign-out/) | Sign out so that my session is terminated and no one else can use my account from this device. |
| [Silent token refresh](token-refresh/) | My session to stay active while I'm working so that I'm not interrupted by unexpected sign-out prompts. |

### Users & invitations

| Use case | Summary |
|---|---|
| [Accept an invitation](accept-invite/) | Accept my invitation and set up my account so that I can access the team account. |
| [Deactivate a user](deactivate-user/) | Deactivate a user so that they can no longer access the workspace without deleting their history. |
| [Invite a user to the team account](invite-user/) | Invite a team member by email so that they can join the workspace and start collaborating. |
| [Manage user profile](user-profile/) | Update my profile information so that my name and contact details are current. |

### Roles & permissions

| Use case | Summary |
|---|---|
| [Permission enforcement on the API](api-permissions/) | Every API endpoint to enforce the required permission so that unauthorized actions are rejected at the server… |
| [Assign a role to a user](assign-role/) | Assign a role to a user so that they get the appropriate permissions. |
| [Create a custom role](create-role/) | Create a custom role with specific permissions so that I can grant exactly the right level of access to a group of… |
| [Edit a custom role](edit-role/) | Edit an existing custom role so that I can adjust permissions as our needs change. |
| [View and manage roles](list-roles/) | See all roles in my team account so that I can understand who has what level of access. |
| [Permission enforcement in the frontend](ui-permissions/) | The UI to hide or disable features I don't have access to so that I'm not confused by actions that will fail. |

### Localization & theming

| Use case | Summary |
|---|---|
| [Switch application language (English / Vietnamese)](language/) | Switch the application language between English and Vietnamese so that I can read and operate the app in my preferred… |
| [Switch visual theme (light / dark / system)](theme/) | Switch the visual theme between light, dark, and system so that I can choose a comfortable theme and preserve readable… |

### Other

| Use case | Summary |
|---|---|
| [Register a standalone user account](register-user/) | Register my standalone user identity with email/password so that I can use Axis as an individual before creating or… |



---

## Diagrams

See [Auth flow](./sign-in/README.md#auth-flow) (Mermaid).

---

## Default Roles

| Role | Description |
|---|---|
| **Admin** | Full access to all features within the team account |
| **Editor** | Can create and edit workflows, models, forms, pages |
| **Viewer** | Read-only access to data and execution history |
| **End User** | Access only to published pages and assigned forms |

*Team accounts can create custom roles with granular permissions.*

---

## Acceptance Criteria (domain)

- [ ] Users can register an account and join a team account via invitation.
- [ ] JWT tokens are validated on every request; expired tokens return 401.
- [ ] Users without required permissions receive 403, never 404 or 500.
- [ ] Password reset flow works end-to-end via email link.
- [ ] Roles and permissions are enforced on both API and frontend UI.

---

## Implementation Status

| Layer | Status | Notes |
|---|---|---|
| Domain | ✅ Done | `Team account`, `User`, `Role`, `Invitation` aggregates; `Email`, `Team accountSlug` value objects; all domain events |
| Application | ✅ Done | `RegisterTeamAccount`, `InviteUser`, `AcceptInvitation`, `DeactivateUser`, `AssignRoleToUser`, `CreateRole`, `UpdateRole`, `UpdateUserProfile`; `AuthenticateUser`, `VerifyEmail`, `ResendVerificationEmail`, `RequestPasswordReset`, `ResetPassword`, `ChangePassword`, `RevokeSession`; `GetRoles`, `GetUserSessions` queries |
| Infrastructure | ✅ Done | `IdentityDbContext` (public schema), EF Core mappings, all repositories, `BCryptPasswordHasher` (work factor 12), `MailKitEmailSender`, `IdentityUnitOfWork` (maps domain events → Avro integration events on save), `PasswordResetTokenStore`, `SessionStoreService` (wraps `IOpenIddictTokenManager`), `OpenIddictSeeder`. `IdentityGrpcService` exposes `GetUserPermissions` ([ADR-014](../../TECH_STACK.md#adr-014-grpc-for-internal-sync-rpc-and-rest-openapi-for-external-api)). `IdentityEventMapper` translates 5 domain events to Avro records published via Wolverine outbox → Kafka (ADR-019). |
| Contracts | ✅ Done | `Axis.Identity.Contracts` — `Protos/axis/identity/v1/identity_service.proto` (`GetUserPermissions`) + 5 Avro schemas (`TeamAccountVerifiedEvent`, `UserDeactivatedEvent`, `UserReactivatedEvent`, `RoleAssignedEvent`, `RoleRemovedEvent`) with hand-written `ISpecificRecord` generated code + `IdentityKafkaTopics` + `IdentityEventExtensions` (typed GUID accessors). |
| API | ✅ Done | OpenIddict 5.x OAuth2/OIDC server: `GET /connect/authorize` (PKCE), `POST /connect/login` (credential validation + session cookie), `POST /connect/token` (code exchange, refresh, client credentials). `POST /api/auth/signout` (revoke refresh token + JTI blacklist). Refresh token delivered as httpOnly `Secure SameSite=Strict` cookie via `ApplyRefreshTokenCookieHandler`; extracted from cookie on refresh via `ExtractRefreshTokenFromCookieHandler`. Permission-based authorization via `PermissionPolicyProvider` + `OpenIddictValidationAspNetCore`. JTI Redis blacklist. `POST /api/auth/verify-email` handles both team account-contact verification (no session; returns first-user setup token) and user-email verification (marks user email verified and establishes a sign-in session so the SPA can complete PKCE without re-entering credentials). `POST /api/auth/retry-provisioning` re-queues failed tenant module provisioning for a verification token (`RetryTenantProvisioningHandler`). Integration-tested with WebApplicationFactory + Testcontainers (PKCE full-flow helpers in `AuthHelper`). User/role/session/settings endpoints return typed DTOs (`CreatedResponse`, `MessageResponse`, `UserSessionResponse`) — no anonymous `object`. |
| Frontend | ⚠️ Partial | Login (PKCE), standalone user registration, app shell, dashboard scaffold, and localization/theming preference foundation are implemented. Settings/invitation/session flows, external-provider registration/linking, and multi-tab refresh remain pending. Team account registration screens and setup-token handoff polish are owned by [platform-foundation/register-team-account](../platform-foundation/register-team-account/). Standalone user-email verification runs PKCE from the callback and redirects to the dashboard (`completePostVerifyPkceFlow` → `/callback`). Workspace provisioning belongs to the team account verification/setup-token flow. |

**Key implementation decisions:**
- Identity uses the global `public` PostgreSQL schema (not a tenant schema). User email uniqueness is platform-wide; team account contact email belongs to [platform-foundation/register-team-account](../platform-foundation/register-team-account/).
- Passwords are hashed with BCrypt (work factor 12) via `IPasswordHasher`. The hash is stored as a first-class property on `User` (`PasswordHash`), not a shadow property.
- The 4 default system roles (Admin, Editor, Viewer, End User) and their full permission sets are seeded automatically by `RegisterTeamAccountHandler` — see [api-permissions](./api-permissions/) and [ui-permissions](./ui-permissions/) for the permission catalogue.
- **OpenIddict implementation**: OpenIddict 5.x serves as the in-process OAuth2/OIDC authorization server (ADR-004). Authorization Code + PKCE for the SPA; Client Credentials for M2M. Refresh tokens are stored as opaque reference tokens in the OpenIddict `OpenIddictTokens` table and delivered via httpOnly cookie. Access token JTIs are blacklisted in Redis on sign-out. Ephemeral signing/encryption keys are used in development; production should use Azure Key Vault certificates.
- **gRPC (dev):** manual `GetUserPermissions` checks — [patterns.md § gRPC dev verification](../../playbooks/patterns.md#dev--verify-getuserpermissions-with-grpcurl).
- **Known gap (user deactivation)**: Revoking all refresh tokens is immediate, but existing access tokens remain valid up to 15 minutes. Full compliance would require a Redis user-level blacklist (not implemented).

---

## Open work (agents)

| Area | Status | Detail |
|------|--------|--------|
| **Backend** | ⚠️ polish | [reset-password](./reset-password/), [change-password](./change-password/), [sessions](./sessions/): rate limits, session list API wiring. [api-permissions](./api-permissions/): `[RequirePermission]` / policy tests. [invite-user](./invite-user/): block admin self-invite at API. |
| **Frontend** | ⚠️ partial | Standalone user registration is shipped. Settings, invitation accept, external-provider registration/linking, session management UI, and multi-tab refresh remain open. Localization/theming foundation is shipped — see [language](./language/) and [theme](./theme/). |

Core auth/OIDC/RBAC backend is ✅; use feature **Gaps vs spec** for the next use case, not domain-level checkboxes.

---

## Dependencies

- [Platform Foundation](../platform-foundation/README.md)

## Dependents

- [Data Modeling](../data-modeling/README.md)
- All other domains
