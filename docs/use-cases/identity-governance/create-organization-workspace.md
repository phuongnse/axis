# Create An Organization Workspace

> **Navigation**: [docs/use-cases/identity-governance/README.md](./README.md) · [docs/use-cases/README.md](../README.md) · [docs/architecture/identity-governance.md](../../architecture/identity-governance.md) · [AGENTS.md](../../../AGENTS.md)

## Purpose

Let a verified Axis user create an Organization and its initial governed Workspace as one complete, retry-safe operation.

## Primary actor

- Verified Axis user who owns an active personal Workspace

## Supporting actors

- Audit receives the durable redacted creation outcome.

## Preconditions

- The user is authenticated, verified, active, and has an active personal Workspace.
- The request carries a retry identity scoped to the authenticated user and canonical creation request.

## Trigger

- The user needs a governed Workspace for multiple real users.

## Success guarantee

- One Organization, its initial active Workspace, the creator's Organization owner membership and Workspace administrator membership, and the required auditable creation outcome are committed and readable.

## Minimal guarantee

- No partial Organization graph or misleading success is left behind; a canonical retry can recover the committed outcome.

## Main flow

1. User opens the create-Organization surface, enters the Organization name, and confirms that its initial Workspace will use the same display name.
2. System validates and normalizes the name and derives subject and authority from the authenticated context.
3. System atomically creates the Organization, memberships, initial Workspace, retry-safe result, and required auditable outcome.
4. System reads the committed Organization, Workspace, memberships, and auditable outcome back.
5. Client shows the complete result and offers an explicit action to enter the new Workspace through [Switch Active Workspace](./switch-active-workspace.md).

The transaction, idempotency, membership, isolation, and audit realization is owned by [Identity Governance architecture](../../architecture/identity-governance.md#organization-creation-realization).

## Alternate / error flows

- Invalid name: show an actionable field error and perform no mutation.
- Identical retry: return the committed canonical result.
- Idempotency-key reuse with different content: fail with a conflict and preserve the original result.
- Concurrent creation: commit at most one complete graph; other requests recover that outcome or fail with a recoverable conflict.
- Required persistence or audit failure: roll back the graph and return a retryable failure.
- Read-back failure after a possible commit: do not claim a new success; retain the request state so a retry can recover canonically.

## Acceptance Criteria

*Happy path*

- **AC-001** A verified user with an active personal Workspace can create one Organization and its initial active organization Workspace through one confirmed journey.
- **AC-002** The committed creator has an active Organization `Owner` membership.
- **AC-003** The committed creator has an active `Administrator` membership in the initial Workspace.
- **AC-004** Success is returned only after the Organization, Workspace, memberships, canonical retry outcome, and required redacted audit outcome are read back.
- **AC-005** The result identifies the created Organization and Workspace and provides an explicit enter action without treating context switching as part of creation.

*Validation and recovery*

- **AC-006** Organization names are trimmed, Unicode-normalized, required, and bounded; invalid input performs no mutation and returns field-local diagnostics.
- **AC-007** Repeating the same canonical request with the same idempotency key returns the original committed outcome.
- **AC-008** Reusing an idempotency key with different canonical content returns a conflict without changing the original outcome.
- **AC-009** Concurrent requests commit at most one complete Organization graph for the canonical request.
- **AC-010** Required persistence or audit failure leaves no partial Organization, Workspace, membership, retry, or success outcome.

*Boundaries*

- **AC-011** The initial organization Workspace belongs to exactly one Organization; Organization membership alone grants no Workspace access.
- **AC-012** Organization lifecycle roles and Workspace lifecycle roles do not assign or imply product permissions.
- **AC-013** REST/OpenAPI and typed MCP creation operations derive subject and authority from authentication and accept no caller-supplied `userId` or authority-bearing Workspace argument.
- **AC-014** The creation experience is keyboard and screen-reader operable, announces pending and result states, and does not expose raw IDs, claims, credentials, or security tokens.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Browser journey | A verified user creates an Organization, receives a complete result, and chooses the separate enter action | AC-001, AC-005, AC-014 | Browser automation | Yes |
| AT-002 | Application/Infrastructure boundaries | Creation commits and reads back one Organization, initial Workspace, both required memberships, canonical retry outcome, and required auditable outcome atomically | AC-002, AC-003, AC-004, AC-010 | Application test + Infrastructure integration test | Yes |
| AT-003 | UI/API boundaries | Invalid Organization names return field-local diagnostics without mutation | AC-006, AC-014 | UI component test + API integration test | Yes |
| AT-004 | Application/Infrastructure boundaries | Identical retry, changed-payload reuse, and concurrent requests preserve one canonical graph and result | AC-007, AC-008, AC-009 | Application test + Infrastructure integration test | Yes |
| AT-005 | Application boundary | Organization and Workspace lifecycle roles remain separate from product permissions and Organization membership grants no Workspace access | AC-011, AC-012 | Application test | Yes |
| AT-006 | API/MCP boundaries | REST/OpenAPI and MCP expose typed creation without caller identity or authority arguments | AC-013 | API integration test + MCP contract test | Yes |

## Out Of Scope

- Entering or changing the active Workspace, owned by [Switch Active Workspace](./switch-active-workspace.md).
- Inviting or accepting members.
- Product-role assignment, Team or Group management, IdP/SCIM provisioning, and service identities.
- Organization or Workspace rename, suspension, deletion, restoration, transfer, or billing.

## Screen flow

| Surface | Required contract |
|---|---|
| Create Organization | Request one display name, explain the initial Workspace, keep validation field-local, and prevent duplicate submission while pending. |
| Creation result | Identify the complete result, distinguish creation from context entry, and provide one explicit enter or retry action. |

> **Implementation status**
>
> | Layer | Status |
> |---|---|
> | Identity Domain | Partial |
> | Identity Application | Partial |
> | Identity Infrastructure | Partial |
> | Audit | Partial |
> | API | Not started |
> | MCP | Not started |
> | Frontend | Not started |
>
> **Gaps vs spec:**
>
> | ID | Gap |
> |---|---|
> | GAP-001 | Complete transaction/read-back and audit delivery evidence, REST/OpenAPI, MCP, and the client journey remain incomplete. |
>
> **Deferred follow-ups:** Only the separately owned capabilities under Out Of Scope are deferred.
>
> **Verification:** Focused Domain, Application, migration, and PostgreSQL persistence evidence exists for the current partial implementation; full acceptance evidence has not run.
>
> **Decisions:** Creation ends with a readable committed graph and a separate explicit enter action. The durable realization and shared security invariants are owned by the linked Identity Governance architecture contract.
