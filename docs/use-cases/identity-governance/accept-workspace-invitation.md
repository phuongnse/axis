# Accept A Workspace Invitation

> **Navigation**: [docs/use-cases/identity-governance/README.md](./README.md) · [docs/use-cases/README.md](../README.md) · [Identity Governance architecture](../../architecture/identity-governance.md) · [AGENTS.md](../../../AGENTS.md)

## Purpose

Let the intended recipient authenticate, review, and accept a Workspace invitation exactly once without leaking invitation or membership data.

## Primary actor

- Invited existing or new Axis user

## Supporting actors

- Identity Access authenticates an existing user or completes standalone registration and verification.
- Audit receives durable redacted exchange and acceptance outcomes.

## Preconditions

- A non-expired pending invitation exists for the recipient's normalized email.
- The recipient has the product-owned invitation link.

## Trigger

- The recipient opens the invitation link and chooses to join the Workspace.

## Success guarantee

- The intended verified account has the allowed active memberships, the invitation is consumed exactly once, and the Workspace is eligible for explicit context selection.

## Minimal guarantee

- No unauthorized membership or metadata disclosure occurs; a still-valid invitation remains retryable after a transactional failure.

## Main flow

1. Recipient opens the product-owned link; the client removes token material before routing, telemetry, or third-party content and exchanges it for a browser-bound handoff.
2. An existing verified user signs in, or a new user completes standalone registration and verification, while the handoff preserves intent.
3. System verifies the authenticated normalized email and displays the Organization, Workspace, inviter, requested role, and expiry.
4. Recipient confirms acceptance.
5. System revalidates lifecycle authority and atomically consumes the invitation, establishes only the allowed Organization and Workspace memberships, and queues the audit outcome.
6. System reads the invitation and memberships back and offers explicit entry through [Switch Active Workspace](./switch-active-workspace.md).

Exchange, handoff, membership mutation, concurrency, retention, and audit realization is owned by [Identity Governance architecture](../../architecture/identity-governance.md#invitation-exchange-and-acceptance-realization).

## Alternate / error flows

- Unknown, expired, used, revoked, superseded, or rate-limited token or handoff: fail closed without Workspace metadata and show applicable recovery.
- Authenticated email differs from the target: preserve the invitation and require the intended account.
- Inviter authority, Organization membership, Workspace eligibility, or requested role changed: reject without membership mutation.
- Suspended Organization or Workspace membership: reject rather than reactivate implicitly.
- Concurrent acceptance or replay: exactly one request mutates memberships; later requests return the canonical terminal classification.
- Membership or audit-outbox persistence fails: roll back consumption and membership changes so a valid invitation remains retryable.
- Required denied, replayed, or stale-authority audit persistence fails: return a generic retryable failure rather than an unaudited lifecycle outcome.

## Acceptance Criteria

*Happy path*

- **AC-001** An existing verified target user can authenticate, review the invitation, and accept it once.
- **AC-002** A new target user can complete standalone registration and email verification, resume the invitation intent, and accept through the same membership transaction.
- **AC-003** Review discloses Organization, Workspace, inviter, requested Workspace role, and expiry only after handoff and authenticated-email validation.
- **AC-004** Acceptance preserves an active Organization role or establishes only baseline Organization `Member` when membership is absent or removed.
- **AC-005** Acceptance creates or reactivates only the invited Workspace membership and requested role.
- **AC-006** Success requires read-back of the consumed invitation and active memberships before the Workspace becomes an eligible switch target.

*Validation and recovery*

- **AC-007** Unknown, expired, used, revoked, superseded, and rate-limited tokens or handoffs fail closed without Organization, Workspace, inviter, role, or membership disclosure.
- **AC-008** Acceptance requires the authenticated account's normalized email to equal the invitation target and never transfers intent to another account.
- **AC-009** Acceptance revalidates inviter authority, target Organization and Workspace eligibility, invitation status, and requested Workspace role.
- **AC-010** Suspended Organization or Workspace membership blocks acceptance and is not reactivated implicitly.
- **AC-011** Concurrent acceptance and replay create at most one Organization membership and one Workspace membership and then return the canonical terminal classification.
- **AC-012** Invitation consumption, membership changes, and required audit-outbox persistence are atomic; failure leaves a still-valid invitation retryable.

*Boundaries*

- **AC-013** Token exchange removes the URL fragment before routing, telemetry, or third-party content and retains no token material in browser-managed storage or logs.
- **AC-014** The handoff is short-lived, bound to one browser, represented by a secure opaque cookie, and survives only the required sign-in or registration journey.
- **AC-015** Acceptance cannot assign or elevate Organization `Owner` or `Administrator` and cannot grant product permissions.
- **AC-016** Acceptance, replay, invalid-token, wrong-account, and stale-authority outcomes produce correlated append-only redacted audit records.
- **AC-017** Terminal cleanup removes reversible token, handoff, delivery-envelope, and target-email material after required work while retaining only approved non-secret replay and lifecycle state.
- **AC-018** Token exchange, browser handoff, account resumption, and recipient acceptance remain internal/bootstrap rather than MCP operations.
- **AC-019** Exchange, review, acceptance, result, and recovery states are keyboard and screen-reader operable, localized, compact-layout safe, and expose a concrete recovery action.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Browser journey | Existing verified recipient exchanges, authenticates, reviews, accepts, and receives an explicit Workspace-entry action | AC-001, AC-003, AC-006, AC-013, AC-019 | Browser automation | Yes |
| AT-002 | Browser journey | New recipient registers, verifies, resumes the browser-bound handoff, and accepts without browser token storage | AC-002, AC-013, AC-014, AC-019 | Browser automation | Yes |
| AT-003 | API/Application boundaries | Wrong-account, invalid-token, expired, revoked, superseded, used, throttled, stale-authority, and suspended-membership attempts fail before mutation with non-disclosing outcomes | AC-007, AC-008, AC-009, AC-010 | API integration test + Application test | Yes |
| AT-004 | Application/Infrastructure boundaries | Acceptance preserves or establishes only the allowed Organization role, creates the invited Workspace role, reads back, and remains atomic on failure | AC-004, AC-005, AC-006, AC-012, AC-015 | Application test + Infrastructure integration test | Yes |
| AT-005 | Application/Infrastructure boundaries | Concurrent acceptance and replay create at most one membership result and return the canonical terminal classification | AC-011 | Application test + Infrastructure integration test | Yes |
| AT-006 | Infrastructure boundary | Exchange and handoff state are browser-bound, expiring, hashed or opaque as required, and absent from browser storage and logs | AC-013, AC-014 | Infrastructure integration test | Yes |
| AT-007 | Infrastructure boundary | Success and rejected outcomes persist correlated redacted audit state and terminal cleanup retains only approved non-secret lifecycle material | AC-016, AC-017 | Infrastructure integration test | Yes |
| AT-008 | API/MCP boundaries | Exchange, handoff, resumption, and acceptance routes remain internal/bootstrap and absent from MCP product operations | AC-018 | API integration test + MCP contract test | Yes |

## Out Of Scope

- Invitation creation, delivery, resend, and pending revocation, owned by [Invite A Workspace Member](./invite-workspace-member.md).
- Automatically changing the active Workspace after acceptance.
- Removing or suspending an accepted membership.
- Organization-role promotion, product permissions, Team or Group assignment, IdP/SCIM, anonymous acceptance, and invitation transfer.

## Screen flow

| Surface | Required contract |
|---|---|
| Invitation handoff | Remove the fragment before application processing and preserve only the secure server-side handoff. |
| Authentication | Preserve intent through sign-in or standalone registration and verification. |
| Invitation review | Disclose invitation facts only to the matched authenticated account and provide one explicit Accept action. |
| Acceptance result | Show the new membership and explicit entry action, or a non-sensitive failure with concrete recovery. |

> **Implementation status**
>
> | Layer | Status |
> |---|---|
> | Identity Domain | Not started |
> | Identity Application | Not started |
> | Identity Infrastructure | Not started |
> | Identity Access integration | Not started |
> | Audit | Not started |
> | API and bootstrap | Not started |
> | Frontend | Not started |
>
> **Gaps vs spec:**
>
> | ID | Gap |
> |---|---|
> | GAP-001 | Token exchange, handoff, resumption, acceptance transaction, audit integration, cleanup, API/bootstrap routes, and client journeys are not implemented. |
>
> **Deferred follow-ups:** Only the separately owned capabilities under Out Of Scope are deferred.
>
> **Verification:** Not run; no acceptance row has current evidence.
>
> **Decisions:** Acceptance is a recipient-owned goal separate from invitation administration. The invitation is email-bound, single-use, and cannot elevate Organization authority or product permissions; the linked architecture contract owns security realization.
