# Invite And Accept A Workspace Member

> **Navigation**: [docs/use-cases/identity-governance/README.md](./README.md) · [docs/use-cases/README.md](../README.md) · [docs/PLATFORM_STRATEGY.md](../../PLATFORM_STRATEGY.md) · [docs/ARCHITECTURE.md](../../ARCHITECTURE.md) · [AGENTS.md](../../../AGENTS.md)

## Purpose

Let an authorized organization administrator invite a real user into one governed workspace, let the intended recipient accept exactly once, and make the workspace available for safe active-context selection.

## Primary actor

- Organization owner or administrator inviting a member
- Invited existing or new Axis user accepting membership

## Trigger

- An organization needs another real user to operate in a governed workspace.

## Main flow

1. Administrator opens membership management in an active organization Workspace and starts an invitation.
2. Administrator enters the recipient email, selects an Identity lifecycle role permitted by their own authority, reviews the target Organization and Workspace, and confirms.
3. System verifies active organization and workspace authority, validates the target email and role, rate-limits and idempotently creates one pending invitation, stores only a hash of its opaque token, and persists an audit outbox record in the same transaction.
4. System sends a localized invitation email naming the Organization, Workspace, inviter, role, expiry, security guidance, and one product-owned acceptance link without exposing internal identifiers or credentials.
5. Recipient opens the link. An existing verified user signs in; a new user completes registration and email verification through the Identity Access contract, then returns to the pending invitation.
6. System requires the authenticated account email to match the invitation target and displays the Organization, Workspace, inviter, role, and expiry before acceptance.
7. Recipient confirms. System atomically consumes the invitation, creates or reactivates the required Organization and Workspace memberships, and persists an audit outbox record.
8. System reads back the accepted memberships and invitation state, then offers entry into the new workspace through the active-context contract owned by [docs/use-cases/identity-governance/create-and-switch-workspace.md](./create-and-switch-workspace.md).
9. Administrator can read the current pending or accepted outcome without seeing token material; recipient can switch to the workspace only while membership remains active.

## Alternate / error flows

- Invalid email, unsupported role, personal workspace target, or workspace outside the active Organization: reject before mutation with actionable diagnostics.
- Inviter lacks current lifecycle authority: return permission denied, create no invitation, and append an attempted-action audit outcome without disclosing unrelated membership data.
- An equivalent pending invitation exists: return that canonical pending outcome without sending parallel valid tokens; resend rotates the token and invalidates the prior token.
- Recipient already has active membership: do not create an invitation; return an existing-member outcome.
- Unknown, expired, used, revoked, or superseded token: fail closed, expose no Organization or Workspace metadata, and provide sign-in or contact-administrator recovery as applicable.
- Authenticated email does not match target email: do not consume the invitation and explain that the intended account must sign in.
- Inviter authority or target Workspace eligibility changes before acceptance: reject acceptance, consume no membership change, and record a non-sensitive audit outcome.
- Concurrent acceptance or replay: exactly one request can consume the invitation and create memberships; later requests return the canonical already-accepted result without duplicates.
- Membership/audit persistence failure: roll back invitation consumption and membership changes so a valid invitation remains retryable.
- Email delivery failure after invitation commit: keep one pending invitation, report delivery failure, and allow an authorized rate-limited resend that rotates the token.

## Acceptance Criteria

*Happy path*

- **AC-001** An active Organization owner or administrator can invite one email address to one organization Workspace with an allowed Identity lifecycle role.
- **AC-002** Invitation creation stores a hashed opaque single-use token, explicit expiry, normalized target email, inviter, Organization, Workspace, role, status, and concurrency state without storing the raw token.
- **AC-003** Invitation email is localized and identifies the Organization, Workspace, inviter, role, expiry, security context, and acceptance action without internal IDs, credentials, or another member's data.
- **AC-004** An existing verified target user can authenticate, review the invitation, accept once, and receive active Organization and Workspace memberships matching the invitation.
- **AC-005** A new target user can complete standalone account registration and email verification, resume the invitation without losing intent, and accept through the same membership transaction.
- **AC-006** Successful acceptance requires read-back of the consumed invitation and active memberships before the workspace becomes an eligible switch target.
- **AC-007** Invite, resend, failed authorization, acceptance, replay, revocation, and resulting membership changes produce correlated append-only audit records without raw tokens or secrets.

*Validation & errors*

- **AC-008** Invalid email, unsupported role, personal workspace target, cross-Organization target, and missing inviter authority fail before invitation creation with non-disclosing diagnostics.
- **AC-009** Invitation creation is idempotent for one canonical request; an equivalent pending invitation returns one canonical outcome and never leaves multiple valid tokens.
- **AC-010** Already-active membership returns an existing-member outcome and creates no invitation or duplicate membership.
- **AC-011** Unknown, expired, used, revoked, and superseded tokens fail closed without Organization, Workspace, inviter, role, or membership disclosure.
- **AC-012** Acceptance requires the authenticated account's normalized email to equal the invitation target and never transfers an invitation to another account.
- **AC-013** Acceptance revalidates inviter authority, Workspace eligibility, invitation status, and role before mutation.
- **AC-014** Concurrent acceptance and replay create at most one Organization membership and one Workspace membership and return a canonical accepted outcome thereafter.
- **AC-015** Invitation consumption, membership mutation, and audit-outbox persistence share one transaction; failure leaves the still-valid invitation retryable.
- **AC-016** Email delivery failure preserves one pending invitation and an authorized rate-limited resend rotates the token, invalidates the prior token, and records delivery outcome.

*Edge cases and boundaries*

- **AC-017** Personal Workspaces reject invitations and additional memberships by domain invariant.
- **AC-018** Organization membership alone does not grant Workspace access; acceptance creates only the explicitly invited Workspace membership.
- **AC-019** Identity invitation roles govern organization/workspace lifecycle only and cannot assign product-specific applicant, caseworker, row, field, record, or workflow permissions.
- **AC-020** Removal or suspension after acceptance invalidates later workspace authorization even while an older cookie or bearer token still contains the workspace claim.
- **AC-021** Invitation, authentication, registration, review, acceptance, and workspace-entry screens preserve intent without placing token material in browser storage or logs.
- **AC-022** Invitation review and result states are keyboard and screen-reader operable, localized, compact-layout safe, and expose a recovery action for every terminal failure.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Browser journey | Administrator invites an existing verified user, recipient reviews and accepts, then enters the workspace | AC-001, AC-003, AC-004, AC-006, AC-021, AC-022 | Browser automation | Yes |
| AT-002 | Browser journey | New user registers, verifies email, resumes the invitation, accepts, and enters the workspace without losing intent | AC-005, AC-006, AC-021, AC-022 | Browser automation | Yes |
| AT-003 | Application/Infrastructure boundaries | Invitation stores only hashed token and canonical state with expiry, concurrency, idempotency, and one valid-token invariant | AC-002, AC-009, AC-014 | Application test + Infrastructure integration test | Yes |
| AT-004 | UI/API boundaries | Invalid input, unsupported role, personal/cross-Organization target, missing authority, and existing membership fail with correct non-disclosing outcomes | AC-008, AC-010, AC-017, AC-018 | UI component test + API integration test | Yes |
| AT-005 | API/Application boundaries | Unknown, expired, used, revoked, superseded, wrong-account, and stale-authority acceptance attempts fail before membership mutation | AC-011, AC-012, AC-013 | Application test + API integration test | Yes |
| AT-006 | Application/Infrastructure boundaries | Concurrent acceptance, replay, and persistence failure prove atomic token consumption, membership uniqueness, retry, and canonical read-back | AC-006, AC-014, AC-015 | Application test + Infrastructure integration test | Yes |
| AT-007 | Infrastructure boundary | Localized email and resend preserve one invitation, rotate token, enforce rate limit, and expose no secret/internal identifiers | AC-003, AC-016 | Infrastructure test | Yes |
| AT-008 | API boundary | Identity lifecycle roles cannot grant product permissions and a suspended/removed membership invalidates stale cookie and bearer authority | AC-019, AC-020 | API integration test | Yes |
| AT-009 | Infrastructure boundary | Every invitation and membership outcome is idempotently projected into correlated append-only redacted audit records | AC-007, AC-015, AC-016 | Infrastructure integration test | Yes |
| AT-010 | UI component | Invitation review, wrong-account, expired, already-accepted, unavailable, pending, success, and recovery states preserve focus and intent without browser token storage | AC-011, AC-012, AC-021, AC-022 | UI component test | Yes |

## Out Of Scope

- Creating an Organization or switching active workspace, owned by [docs/use-cases/identity-governance/create-and-switch-workspace.md](./create-and-switch-workspace.md).
- IdP/SCIM provisioning, domain-verified invitations, Team assignment, or Group synchronization.
- Product roles, policy authoring, delegated authority, row/field access, or workflow assignment.
- Bulk invitation, CSV import, public invitation links, anonymous acceptance, or invitation transfer.
- Service identities and non-human invitation targets.
- Organization/Workspace rename, deletion, transfer, or billing behavior.

## Screen flow

| Surface | Required contract |
|---|---|
| Membership management | Identify Organization and Workspace, list current/pending membership outcomes without token material, and expose Invite only when the server reports lifecycle authority. |
| Invite member | Request email and one allowed lifecycle role, keep target Organization/Workspace visible, show field-local errors, and prevent duplicate submission while pending. |
| Invitation handoff | Preserve the opaque token only for the immediate server exchange; require sign-in or registration when needed and resume the same invitation afterward without browser storage. |
| Invitation review | Show Organization, Workspace, inviter, role, and expiry only after the server validates the token and authenticated account; provide one explicit Accept action. |
| Acceptance result | On success show the new membership and workspace-entry action. For expired, revoked, wrong-account, stale-authority, or unavailable states, show a non-sensitive explanation and concrete recovery. |

Required UI quality: forms and invitation facts are programmatically labelled; role choices include their lifecycle effect; pending, delivery, acceptance, and error feedback is announced; focus returns predictably after dialogs; links and actions remain keyboard reachable; compact layouts do not overflow; localized copy never exposes raw token, internal ID, stack trace, another member's data, or product permission claims.

> **Implementation status**
>
> | Layer | Status |
> |---|---|
> | Identity Domain | Not started |
> | Identity Application | Not started |
> | Identity Infrastructure | Not started |
> | Audit | Not started |
> | API and authentication | Not started |
> | Frontend | Not started |
>
> **Gaps vs spec:**
>
> | ID | Gap |
> |---|---|
> | GAP-001 | Invitations, lifecycle authorization, email delivery, acceptance, membership mutation, durable audit, public operations, and client journeys are not implemented. |
>
> **Deferred follow-ups:** Only the separately owned capabilities listed under Out Of Scope are deferred; no required production property of invitation or acceptance is deferred.
>
> **Verification:** Not run; implementation has not started and no acceptance row has current evidence.
>
> **Decisions:** Invitations target normalized email and one explicit organization Workspace. Existing and new users converge on one authenticated acceptance transaction. Raw tokens exist only in delivery and immediate exchange; persisted tokens are hashed, single-use, expiring, revocable, and rotated on resend. Organization and Workspace memberships are distinct; no Organization-wide implicit data access exists. Identity roles govern lifecycle only. Acceptance and audit-outbox persistence are atomic, while email delivery failure is an explicit recoverable outcome. Event sourcing is not introduced.
