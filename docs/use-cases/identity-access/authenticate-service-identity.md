# Authenticate A Service Identity

> **Navigation**: [docs/use-cases/identity-access/README.md](./README.md) · [docs/use-cases/identity-governance/manage-workspace-service-identities.md](../identity-governance/manage-workspace-service-identities.md) · [docs/architecture/identity-governance.md](../../architecture/identity-governance.md) · [AGENTS.md](../../../AGENTS.md)

## Purpose

Let an active Workspace service identity authenticate without a browser and obtain a short-lived opaque/reference access token only after proving possession of an active ES256 private key.

## Primary actor

- Service operator acting through a registered Workspace service identity

## Supporting actors

- Audit receives durable redacted authentication security outcomes.

## Preconditions

- The service identity, its exactly one Workspace grant, and the selected ES256 public JWK are active.
- The service operator holds the corresponding private key and can produce a short-lived signed assertion.

## Trigger

- The service needs a service access token for an authorized operation in its Workspace.

## Success guarantee

- The authenticated service receives one short-lived opaque/reference access token scoped only to its active service identity and Workspace.

## Minimal guarantee

- A malformed, stale, replayed, unknown, revoked, cross-Workspace, or otherwise invalid assertion yields no token and no additional authority.

## Main flow

1. Service operator creates a short-lived `private_key_jwt` client assertion with required `iss`, `sub`, `iat`, `exp`, and unique `jti`; `iss` and `sub` equal its client identifier, the audience is the exact token endpoint, and the selected `kid` identifies an active key.
2. Service submits the OAuth client-credentials grant with the assertion.
3. System resolves the service identity and key, validates assertion shape, ES256 signature, claim binding, lifetime, replay resistance, and current identity/Workspace/key/grant state.
4. System records the required redacted authentication outcome and issues one short-lived opaque/reference access token only after those checks succeed.
5. Service presents the token to an authenticated Workspace operation; validation rechecks active authority before that operation can proceed.

Assertion, token, immediate-revocation, replay, and audit realization is owned by [Identity Governance architecture](../../architecture/identity-governance.md#service-identity-realization).

## Alternate / error flows

- Any grant type other than OAuth client credentials, or any client authentication other than `private_key_jwt`, denies without issuing a token.
- Missing, malformed, expired, future-invalid, mismatched, unknown, revoked, or non-ES256 assertions/keys deny without divulging key or identity state.
- `iss`/`sub` mismatch, non-exact token audience, missing `iat`/`exp`/`jti`, assertion over five minutes long or older than five minutes, future/expired assertion outside 30-second skew, optional `nbf` after effective current time or before `iat`, replayed `jti`, invalid signature, or inactive Workspace grant denies and records the required redacted security outcome.
- Identity, grant, or key revocation after issuance causes the opaque/reference token to deny immediately; token validation never refreshes or extends authority.
- Authentication/audit dependency failure fails closed. A service retries only with a newly valid assertion; it never receives a browser continuation or fallback credential path.

## Acceptance Criteria

*Happy path*

- **AC-001** An active service identity with an active ES256 public JWK and exactly one active Workspace grant can obtain a five-minute opaque/reference token through OAuth client credentials using `private_key_jwt` only.
- **AC-002** The client assertion requires `iss = sub = client_id`, the exact token-endpoint audience, an active known `kid`, valid ES256 signature, and required `iat`, `exp`, and unique `jti`; `exp - iat` and assertion age are at most five minutes, validation allows at most 30 seconds clock skew, and optional `nbf` is neither after effective current time nor before `iat`.
- **AC-003** A successfully authenticated service token identifies only the authenticated service subject and its current Workspace; authenticated Workspace operations still pass the shared Workspace-access policy.

*Validation and recovery*

- **AC-004** Shared secrets, any other client-authentication method, refresh tokens, authorization-code/browser paths, and cross-Workspace grants are rejected and never become fallback behavior.
- **AC-005** Missing, malformed, expired, future-invalid, mismatched, unknown, revoked, replayed, or invalidly signed assertions deny before token issue without disclosing identity/key state.
- **AC-006** Each accepted `jti` is replay-protected as a non-reversible digest through `exp + 30 seconds`, then purged; duplicate delivery, concurrent exchange, or retry cannot issue another token from that assertion.
- **AC-007** Revoking the service identity (and therefore its intrinsic Workspace grant) or its selected signing key immediately denies both new exchanges and use of previously issued tokens.
- **AC-008** Authentication success, denial, replay, token rejection, and dependency failure are durably auditable with correlated redacted data; required audit failure fails closed.

*Boundaries*

- **AC-009** Client assertions, private keys, service tokens, and replay values are never rendered, logged, persisted in audit metadata, or returned beyond the token response required by the authenticated service.
- **AC-010** This service flow has no browser, interactive consent, UI, MCP, or human-session continuation; it returns a stable machine-readable success or denial outcome.
- **AC-011** Client-credentials activation and the exact Authorization enforcement for every service-reachable product action ship in the same implementation checkpoint; a service cannot use a baseline-WorkspaceAccess-only endpoint.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Application/Infrastructure boundaries | Valid private-key JWT client credentials issue a short-lived opaque/reference token bound to one active service identity and Workspace | AC-001, AC-002, AC-003 | Application test + Infrastructure integration test | Yes |
| AT-002 | Application boundary | Unsupported grant/auth methods and all malformed, claim-invalid, stale, unknown, revoked, or invalid-signature assertions deny without token issue | AC-004, AC-005 | Application test | Yes |
| AT-003 | Infrastructure boundary | Duplicate and concurrent `jti` exchange attempts are replay-rejected for the valid assertion lifetime | AC-006 | Infrastructure integration test | Yes |
| AT-004 | API/Application boundaries | Identity revocation, including its intrinsic Workspace grant, and key revocation deny new and previously issued opaque/reference token authority immediately | AC-007 | API integration test + Application test | Yes |
| AT-005 | Infrastructure boundary | Authentication success, denial, replay, and dependency failure have correlated redacted audit read-back and fail closed when required audit work is unavailable | AC-008, AC-009 | Infrastructure integration test | Yes |
| AT-006 | API boundary | The machine flow exposes no browser/MCP continuation, interactive consent, refresh token, or credential artifact beyond the required token response | AC-004, AC-009, AC-010 | API integration test | Yes |
| AT-007 | Application/Infrastructure boundaries | Missing temporal claims, overlong or stale lifetime, future/expired values at the 30-second skew boundary, and invalid optional `nbf` deny; a replay digest remains through `exp + 30 seconds` and is then purged | AC-002, AC-005, AC-006 | Application test + Infrastructure integration test | Yes |
| AT-008 | API/Application boundaries | Client-credentials activation and every service-reachable product action deploy together; service tokens deny at all baseline-WorkspaceAccess-only endpoints | AC-003, AC-010, AC-011 | API integration test + Application test | Yes |

## Out Of Scope

- Creating, rotating, or revoking service identities and keys.
- Human browser authentication, local MCP authorization, shared secrets, refresh tokens, dynamic client registration, token exchange, delegated authority, and cross-Workspace service access.
- Product-role assignment and product authorization decisions.

> **Implementation status**
>
> | Layer | Status |
> |---|---|
> | Identity Application | Not started |
> | Identity Infrastructure | Not started |
> | Audit | Not started |
> | API | Not started |
> | MCP | N/A |
>
> **Gaps vs spec:**
>
> | ID | Gap |
> |---|---|
> | GAP-001 | All implementation layers are not started; every acceptance criterion awaits implementation. |
>
> **Deferred follow-ups:** Only the separately owned capabilities under Out Of Scope are deferred.
>
> **Verification:** Not run; implementation evidence does not exist yet.
>
> **Decisions:** Service authentication is non-interactive OAuth client credentials with `private_key_jwt` and ES256 only. Opaque/reference tokens remain short-lived and immediately revocable through current Identity lifecycle state.
