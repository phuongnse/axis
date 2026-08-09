# Manage Workspace Service Identities

> **Navigation**: [docs/use-cases/identity-governance/README.md](./README.md) · [docs/use-cases/identity-access/authenticate-service-identity.md](../identity-access/authenticate-service-identity.md) · [docs/architecture/identity-governance.md](../../architecture/identity-governance.md) · [AGENTS.md](../../../AGENTS.md)

## Purpose

Let an active Workspace administrator create and irrevocably revoke a non-human service identity and its public signing keys for exactly one current Workspace.

## Primary actor

- Active Workspace administrator

## Supporting actors

- A service operator supplies public signing-key material for a service identity.
- Existing human-administrator OAuth authorizes the typed MCP administration tools.
- Audit receives durable redacted lifecycle outcomes.

## Preconditions

- The administrator has active `Administrator` Workspace membership in the current Workspace.
- The target service identity has no authority before this journey creates its active Workspace grant.

## Trigger

- The administrator needs a non-human integration to act only in the current Workspace.

## Success guarantee

- A uniquely identified service identity belongs to the current Workspace, has an active Workspace grant, and has one or more usable ES256 public JWKs with non-secret lifecycle state visible to the administrator.

## Minimal guarantee

- No shared secret, private key, cross-Workspace authority, duplicate active key identifier, or unaudited security lifecycle outcome is created or exposed.

## Main flow

1. Administrator opens service-identity management for the current Workspace.
2. Administrator creates a service identity and records its client identifier for the service operator.
3. Administrator adds an ES256 public JWK with a unique `kid`; the service operator retains the corresponding private key.
4. System validates current Workspace authority, exact Workspace scope, JWK public-key suitability, and that neither the `kid` nor RFC 7638 public-JWK thumbprint was ever revoked for this identity.
5. System commits the identity, active Workspace grant, key lifecycle state, and required redacted audit outcome, then reads back the non-secret result.
6. Administrator can add another public JWK for overlap, revoke an individual key, or irrevocably revoke the service identity and its intrinsic Workspace grant.

Shared lifecycle, assertion, immediate-revocation, concurrency, and audit realization is owned by [Identity Governance architecture](../../architecture/identity-governance.md#service-identity-realization).

## Alternate / error flows

- Inactive or non-administrator Workspace membership denies every lifecycle action without disclosing a foreign identity or key.
- A missing, private, malformed, unsupported, or non-ES256 JWK is rejected before lifecycle mutation; private key material and shared secrets are never accepted.
- A duplicate or revoked `kid`, or key material matching a revoked RFC 7638 public-JWK thumbprint under any `kid`, conflicts without replacing the existing key. A key may be added for overlap only under the same identity and Workspace.
- A stale lifecycle revision or concurrent add/revoke returns a recoverable conflict or canonical terminal result; no revoked key, identity, or grant becomes active again.
- Revoking a key or identity is irreversible. Identity revocation also revokes its intrinsic Workspace grant; the administrator must create a new current-authority identity or key to recover service access.
- Audit persistence, audit-delivery setup, or read-back failure fails closed and does not report the lifecycle change as successful.

## Acceptance Criteria

*Happy path*

- **AC-001** An active administrator creates a service identity under exactly the current Workspace with one active Workspace grant and no human membership or Organization-role implication.
- **AC-002** An authorized administrator can add multiple active ES256 public JWKs to the same service identity for key overlap, and every active key has a unique `kid` within that identity.
- **AC-003** The administrator can read client identifier, current Workspace, non-secret key identity/status, and lifecycle outcome without private key material, shared secrets, or authentication artifacts.
- **AC-004** An authorized administrator can irrevocably revoke a key or service identity; identity revocation also revokes its intrinsic Workspace grant, and all existing and later service access relying on the revoked authority is denied immediately.

*Validation and recovery*

- **AC-005** Only an active current-Workspace administrator can create, add, read, or revoke service-identity authority; identity lifecycle roles, product roles, Organization membership, and client claims do not substitute for that authority.
- **AC-006** Missing, malformed, private, unsupported, or non-ES256 JWK input and a duplicate active `kid` fail before mutation without exposing existing key material.
- **AC-007** A service identity cannot be created under more than one current Workspace, cannot receive a cross-Workspace grant, and does not create human membership, product role, Team, or Group authority.
- **AC-008** Idempotent retry and concurrent lifecycle actions preserve one canonical outcome, unique active-key identity, and irreversible revoke behavior without silent overwrite or resurrection.
- **AC-009** Required lifecycle creation, revocation, denial, and read-back failure outcomes are correlated, append-only, redacted, and fail closed when required audit work cannot persist.

*Boundaries*

- **AC-010** The journey never accepts, stores, renders, or returns a shared secret, private key, client assertion, service access token, refresh token, or browser authorization artifact.
- **AC-011** Service identity creation and management use server-derived subject and current Workspace scope; cross-Workspace lookup is non-disclosing.
- **AC-012** Management UI exposes lifecycle state and recoverable errors accessibly, prevents duplicate submission while pending, supports keyboard key-management actions, and remains usable on compact layouts without secret-bearing fields.
- **AC-013** Key revocation retains immutable `kid` and RFC 7638 public-JWK-thumbprint tombstones; neither a revoked `kid` nor its public key material under a renamed `kid` can be added or authenticate again.
- **AC-014** Service-identity lifecycle administration is exposed through typed MCP tools authorized by the existing human administrator OAuth boundary; no service credential or token is accepted by or exposed through those tools.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Application boundary | A service identity has exactly one Workspace grant; add/revoke key and identity invariants preserve unique active `kid` and irreversible lifecycle state | AC-001, AC-002, AC-004, AC-007 | Domain test + Application test | Yes |
| AT-002 | Application/Infrastructure boundaries | Valid JWK lifecycle, canonical retry, concurrent add/revoke, audit persistence, and read-back complete atomically or fail closed | AC-002, AC-003, AC-008, AC-009 | Application test + Infrastructure integration test | Yes |
| AT-003 | API boundary | Missing authority, malformed/private/non-ES256 JWK, duplicate `kid`, and cross-Workspace access deny without mutation or disclosure | AC-005, AC-006, AC-011 | API integration test | Yes |
| AT-004 | API/Application boundaries | Revoked key or identity, including its intrinsic grant, immediately removes service authority and cannot be reactivated by retry or concurrency | AC-004, AC-008 | API integration test + Application test | Yes |
| AT-005 | Infrastructure boundary | Lifecycle successes, denials, and failures are redacted, correlated, append-only, and readable without credentials or key material | AC-009, AC-010 | Infrastructure integration test | Yes |
| AT-006 | Browser journey | Administrator manages keys and revocation with accessible pending, success, conflict, and recovery states | AC-003, AC-012 | Browser automation | Yes |
| AT-007 | Application/Infrastructure boundaries | Attempts to reuse a revoked `kid` or revoked public-key thumbprint under another `kid` fail, while overlap with a distinct active key remains valid | AC-002, AC-006, AC-013 | Application test + Infrastructure integration test | Yes |
| AT-008 | API/MCP boundaries | Typed service-identity lifecycle tools use existing human-administrator OAuth and never accept or reveal service credentials or tokens | AC-005, AC-010, AC-014 | API integration test + MCP contract test | Yes |

## Out Of Scope

- Service authentication and token issuance, owned by [Authenticate A Service Identity](../identity-access/authenticate-service-identity.md).
- Product-role assignment and product policy enforcement.
- Shared secrets, dynamic client registration, browser flows, refresh tokens, delegated human authority, Group/Team membership, IdP/SCIM, and cross-Workspace service access.

## Screen flow

| Surface | Required contract |
|---|---|
| Service identity collection | Identify the current Workspace, service identities, non-secret key/lifecycle state, and actions allowed by server-reported authority. |
| Create identity | Explain the one-Workspace boundary, provide the client identifier only after success, accept public JWK material only, and keep private material out of the interface. |
| Key rotation and revoke | Show existing non-secret key identity and status, support overlap before revocation, require explicit irreversible confirmation, and show immediate recovery guidance. |

> **Implementation status**
>
> | Layer | Status |
> |---|---|
> | Identity Domain | Done |
> | Identity Application | Done |
> | Identity Infrastructure | Done |
> | Audit | Done |
> | API | Done |
> | MCP | Done |
> | Frontend | Done |
>
> **Gaps vs spec:** None.
>
> **Deferred follow-ups:** Only the separately owned capabilities under Out Of Scope are deferred.
>
> **Verification:** Every required AT is mapped to current Domain, Application, PostgreSQL infrastructure, API, MCP contract, durable audit, and focused browser evidence in [manage-workspace-service-identities.evidence.md](./manage-workspace-service-identities.evidence.md).
>
> **Decisions:** Service identities are Identity-owned non-human subjects with exactly one Workspace grant. They authenticate only through the linked service-authentication contract; revocation is irreversible and immediately authoritative.
