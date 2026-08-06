# Invite And Accept A Workspace Member

> **Navigation**: [docs/use-cases/identity-governance/README.md](./README.md) · [docs/use-cases/README.md](../README.md) · [docs/PLATFORM_STRATEGY.md](../../PLATFORM_STRATEGY.md) · [docs/ARCHITECTURE.md](../../ARCHITECTURE.md) · [AGENTS.md](../../../AGENTS.md)

## Purpose

Let an authorized Workspace administrator invite a real user into one governed workspace, let the intended recipient accept exactly once, and make the workspace available for safe active-context selection.

## Primary actor

- Active Workspace administrator inviting a member
- Invited existing or new Axis user accepting membership

## Trigger

- An organization needs another real user to operate in a governed workspace.

## Main flow

1. Administrator opens membership management in an active organization Workspace and starts an invitation.
2. Administrator enters the recipient email, selects `Workspace administrator` or `Workspace member`, reviews the target Organization and Workspace, and confirms. This operation cannot select or elevate an Organization role; acceptance may establish only the baseline Organization `Member` prerequisite.
3. System verifies the inviter's active Organization membership and active administrator membership in the target Workspace, validates the target email and Workspace role, rate-limits and idempotently creates one pending invitation, stores the opaque token hash, and persists audit and durable email-delivery outbox records in the same transaction.
4. The delivery record holds the token only as an access-controlled, authenticated-encrypted envelope with expiry no later than the invitation. A worker sends a localized email using one stable delivery correlation key; after provider acceptance it deletes the envelope irreversibly. Ambiguous delivery retries the same generation and may produce duplicate messages, but never another valid token.
5. Recipient opens a product-owned link whose token is in the URL fragment. Before analytics, third-party content, or application routing, the client removes the fragment with history replacement and posts the token from memory for a single-use exchange under `Referrer-Policy: no-referrer` and explicit client/server log redaction.
6. Successful exchange consumes the email token and establishes a short-lived, single-browser handoff using an opaque HttpOnly, Secure, SameSite cookie whose server-side identifier is hashed. The handoff, not token material, survives sign-in or standalone registration and email verification.
7. An existing verified user signs in; a new user completes registration and verification through Identity Access, then resumes the handoff.
8. System requires the authenticated account email to match the invitation target and displays the Organization, Workspace, inviter, requested Workspace role, and expiry before acceptance.
9. Recipient confirms. System atomically consumes the invitation, creates an Organization `Member` membership when none or a previously removed membership exists, preserves every active Organization role, creates the invited Workspace membership or reactivates a previously removed one with the requested role, and persists an audit outbox record. A suspended Organization or Workspace membership blocks acceptance instead of being reactivated implicitly.
10. System reads back the accepted memberships and invitation state, then offers entry into the new workspace through the active-context contract owned by [docs/use-cases/identity-governance/create-and-switch-workspace.md](./create-and-switch-workspace.md).
11. Administrator can read current pending, delivery, accepted, or revoked outcomes and may idempotently revoke a pending invitation in one transaction with its audit outbox; recipient can switch to the workspace only while membership remains active.

## Alternate / error flows

- Invalid email, unsupported Workspace role, personal workspace target, or workspace outside the active Organization: reject before mutation with actionable diagnostics.
- Inviter lacks active Organization membership or target-Workspace administrator authority: return permission denied, create no invitation, and append an attempted-action audit outcome without disclosing unrelated membership data.
- An equivalent pending invitation exists: return that canonical pending outcome without sending parallel valid tokens; resend rotates the token and invalidates the prior token.
- Recipient already has active membership: do not create an invitation; return an existing-member outcome.
- Unknown, expired, used, revoked, superseded, or rate-limited token or handoff: fail closed, expose no Organization or Workspace metadata, and provide sign-in or contact-administrator recovery as applicable.
- Authenticated email does not match target email: do not consume the invitation and explain that the intended account must sign in.
- Inviter authority, Organization membership, or target Workspace eligibility changes before acceptance: reject acceptance, consume no membership change, and record a non-sensitive audit outcome.
- Concurrent acceptance or replay: exactly one request can consume the invitation and create memberships; later requests return the canonical already-accepted result without duplicates.
- Membership/audit persistence failure: roll back invitation consumption and membership changes so a valid invitation remains retryable.
- Process failure after invitation commit but before delivery: the durable encrypted delivery record retries the same token generation. Ambiguous provider outcomes keep the same generation and idempotency key until terminal success, terminal failure, or expiry.
- Email delivery reaches terminal failure: keep one pending invitation, report delivery failure, and allow an authorized rate-limited resend that explicitly supersedes the old generation, invalidates its handoff, and creates one new transactional delivery record.
- Audit-outbox persistence fails for a denied, replayed, stale-authority, revoked, or other required no-business-mutation outcome: fail closed with a generic retryable response; do not report a successful lifecycle result without its required durable audit outcome.
- Pending invitation revocation races with exchange or acceptance: optimistic concurrency permits one terminal outcome. Revocation invalidates token, handoff, and undelivered envelope; it never removes an already accepted membership.

## Acceptance Criteria

*Happy path*

- **AC-001** A user with active Organization membership and active administrator membership in the target organization Workspace can invite one email address with `Workspace administrator` or `Workspace member`; the operation cannot select or elevate an Organization role, while acceptance may establish only baseline Organization `Member` when absent/removed.
- **AC-002** Invitation creation stores a hashed opaque single-use token, explicit expiry, normalized target email, inviter, Organization, Workspace, requested Workspace role, status, and concurrency state. The only reversible token representation is an access-controlled authenticated-encrypted delivery envelope with bounded lifetime in the durable email outbox.
- **AC-003** Invitation email is localized and identifies the Organization, Workspace, inviter, requested Workspace role, expiry, security context, and acceptance action without internal IDs, credentials, or another member's data.
- **AC-004** An existing verified target user can authenticate, review the invitation, accept once, preserve any active Organization role or receive Organization `Member` when absent or previously removed, and receive only the invited Workspace role.
- **AC-005** A new target user can complete standalone account registration and email verification, resume the invitation without losing intent, and accept through the same membership transaction.
- **AC-006** Successful acceptance requires read-back of the consumed invitation and active memberships before the workspace becomes an eligible switch target.
- **AC-007** Invite, delivery, resend, failed authorization, acceptance, replay, revocation, and resulting membership changes produce correlated append-only audit records without token material, handoff identifiers, delivery envelopes, or secrets.

*Validation & errors*

- **AC-008** Invalid email, unsupported Workspace role, personal workspace target, cross-Organization target, missing Organization membership, and missing target-Workspace administrator authority fail before invitation creation with non-disclosing diagnostics.
- **AC-009** Invitation creation is idempotent for one canonical request; an equivalent pending invitation returns one canonical outcome and never leaves multiple valid tokens.
- **AC-010** Already-active membership returns an existing-member outcome and creates no invitation or duplicate membership.
- **AC-011** Unknown, expired, used, revoked, and superseded tokens fail closed without Organization, Workspace, inviter, role, or membership disclosure.
- **AC-012** Acceptance requires the authenticated account's normalized email to equal the invitation target and never transfers an invitation to another account.
- **AC-013** Acceptance revalidates inviter authority, target Organization and Workspace eligibility, invitation status, and requested Workspace role; it preserves an active Organization role, creates or reactivates only Organization `Member` when absent or removed, and rejects suspended Organization or Workspace membership.
- **AC-014** Concurrent acceptance and replay create at most one Organization membership and one Workspace membership and return a canonical accepted outcome thereafter.
- **AC-015** Invitation consumption, membership mutation, and audit-outbox persistence share one transaction; failure leaves the still-valid invitation retryable.
- **AC-016** Invitation commit durably queues one encrypted, expiring delivery envelope. Crash and ambiguous provider recovery retry the same generation and stable delivery key; only an explicit authorized rate-limited resend supersedes it, invalidates prior token/handoff state, and records the new delivery outcome.

*Edge cases and boundaries*

- **AC-017** Personal Workspaces reject invitations and additional memberships by domain invariant.
- **AC-018** Organization membership alone does not grant Workspace access; acceptance may establish the baseline Organization `Member` prerequisite but creates or reactivates access only for the explicitly invited Workspace.
- **AC-019** Workspace invitation roles govern membership lifecycle only; acceptance may establish baseline Organization `Member` but cannot assign/elevate `Owner` or `Administrator` or any product-specific applicant, caseworker, row, field, record, or workflow permission.
- **AC-020** Removal or suspension after acceptance invalidates later workspace authorization even while an older cookie or bearer token still contains the workspace claim.
- **AC-021** Invitation, authentication, registration, review, acceptance, and workspace-entry screens preserve intent without placing token material in application-managed browser storage or logs; the navigation fragment is transient and removed before application routing.
- **AC-022** Invitation review and result states are keyboard and screen-reader operable, localized, compact-layout safe, and expose a recovery action for every terminal failure.
- **AC-023** The rate-limited email exchange removes the fragment by history replacement before routing, telemetry, or third-party content; it uses an HTTPS request body, `no-referrer`, client/server log redaction, no local/session/IndexedDB persistence, and a short-lived hashed server-side handoff bound to one browser.
- **AC-024** A target-Workspace administrator can idempotently revoke only a pending invitation; revocation, exchange, and acceptance use concurrency so exactly one terminal outcome wins, and revocation never removes an accepted membership.
- **AC-025** Required audit outcomes without a business mutation persist through an Identity audit-outbox transaction; audit failure returns a generic retryable failure and never permits the attempted lifecycle action.
- **AC-026** After acceptance, revocation, or expiry becomes terminal and required audit/delivery work completes, the system deletes delivery envelopes, handoffs, and normalized target email; it retains only non-reversible token digests/generations needed for replay classification plus non-secret lifecycle identifiers, status, role, timestamps, and accepted user identity when applicable.
- **AC-027** REST/OpenAPI and typed MCP operations expose current-workspace membership reads plus invite, resend, and revoke with server-derived Organization/Workspace authority; token exchange, browser handoff, account resumption, and recipient acceptance remain explicitly internal/bootstrap.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Browser journey | Administrator invites an existing verified user, fragment token is cleaned and exchanged, recipient reviews and accepts, then enters the workspace | AC-001, AC-003, AC-004, AC-006, AC-021, AC-022, AC-023 | Browser automation | Yes |
| AT-002 | Browser journey | New user exchanges the token, registers, verifies email, resumes the server-side handoff, accepts, and enters the workspace without browser token storage | AC-005, AC-006, AC-021, AC-022, AC-023 | Browser automation | Yes |
| AT-003 | Application/Infrastructure boundaries | Invitation stores one token hash and bounded encrypted delivery envelope with expiry, concurrency, idempotency, and one valid-generation invariant | AC-002, AC-009, AC-014, AC-016 | Application test + Infrastructure integration test | Yes |
| AT-004 | UI/API boundaries | Invalid input, unsupported role, personal/cross-Organization target, missing authority, and existing membership fail with correct non-disclosing outcomes | AC-008, AC-010, AC-017, AC-018 | UI component test + API integration test | Yes |
| AT-005 | API/Application boundaries | Unknown, expired, used, revoked, superseded, throttled, wrong-account, and stale-authority exchange or acceptance attempts fail before membership mutation | AC-011, AC-012, AC-013, AC-023 | Application test + API integration test | Yes |
| AT-006 | Application/Infrastructure boundaries | Concurrent acceptance, replay, and persistence failure prove atomic token consumption, membership uniqueness, retry, and canonical read-back | AC-006, AC-014, AC-015 | Application test + Infrastructure integration test | Yes |
| AT-007 | Infrastructure boundary | Commit/send crash, ambiguous provider response, localized delivery, terminal failure, and explicit resend preserve one valid generation, enforce rate limits, securely delete envelopes, and expose no secret/internal identifiers | AC-003, AC-016 | Infrastructure integration test | Yes |
| AT-008 | API boundary | Workspace invitation roles cannot grant Organization or product permissions, and a suspended/removed membership invalidates stale cookie and bearer authority | AC-019, AC-020 | API integration test | Yes |
| AT-009 | Infrastructure boundary | Mutation and no-mutation outcomes, including denied authority, stale acceptance, replay, delivery, and revocation, persist required outbox state and project idempotently into correlated append-only redacted audit records | AC-007, AC-015, AC-016, AC-024, AC-025 | Infrastructure integration test | Yes |
| AT-010 | UI component | Invitation exchange, review, wrong-account, expired, already-accepted, unavailable, pending, revoked, success, and recovery states preserve focus and intent without browser token storage | AC-011, AC-012, AC-021, AC-022, AC-023, AC-024 | UI component test | Yes |
| AT-011 | API/Application boundaries | Active Organization roles are preserved, absent/removed Organization membership becomes `Member`, suspended Organization/Workspace membership blocks acceptance, and Workspace role delegation never elevates Organization authority | AC-001, AC-004, AC-008, AC-013, AC-018, AC-019 | Application test + API integration test | Yes |
| AT-012 | Infrastructure boundary | Terminal acceptance, revocation, and expiry remove reversible token, handoff, delivery, and target-email material only after required audit/delivery work, while retaining replay digests and the approved non-secret lifecycle record | AC-002, AC-007, AC-016, AC-024, AC-026 | Infrastructure integration test | Yes |
| AT-013 | API/MCP boundaries | REST/OpenAPI and MCP coverage expose typed membership/invite/resend/revoke operations without caller authority arguments and classify token/handoff/account-resumption/acceptance operations as internal/bootstrap | AC-001, AC-007, AC-008, AC-027 | API integration test + MCP contract test | Yes |

## Out Of Scope

- Creating an Organization or switching active workspace, owned by [docs/use-cases/identity-governance/create-and-switch-workspace.md](./create-and-switch-workspace.md).
- IdP/SCIM provisioning, domain-verified invitations, Team assignment, or Group synchronization.
- Product roles, policy authoring, delegated authority, row/field access, or workflow assignment.
- Bulk invitation, CSV import, public invitation links, anonymous acceptance, or invitation transfer.
- Service identities and non-human invitation targets.
- Organization/Workspace rename, deletion, transfer, or billing behavior.
- Removing or suspending an accepted membership; revocation in this use case applies only to a pending invitation.

## Screen flow

| Surface | Required contract |
|---|---|
| Membership management | Identify Organization and Workspace, list current/pending membership outcomes without token material, and expose Invite only when the server reports target-Workspace administrator authority. |
| Invite member | Request email and one allowed Workspace role, keep target Organization/Workspace visible, show field-local errors, and prevent duplicate submission while pending. |
| Invitation handoff | Read the token only from the URL fragment, remove it before routing/telemetry/third-party content, exchange it from memory, and preserve only the HttpOnly server-side handoff through sign-in or registration. |
| Invitation review | Show Organization, Workspace, inviter, requested Workspace role, and expiry only after the server validates the handoff and authenticated account; provide one explicit Accept action. |
| Acceptance result | On success show the new membership and workspace-entry action. For expired, revoked, wrong-account, stale-authority, or unavailable states, show a non-sensitive explanation and concrete recovery. |

Required UI quality: forms and invitation facts are programmatically labelled; role choices include their lifecycle effect; pending, delivery, acceptance, and error feedback is announced; focus returns predictably after dialogs; links and actions remain keyboard reachable; compact layouts do not overflow; localized copy never exposes raw token, internal ID, stack trace, another member's data, or product permission claims.

## Role delegation

| Scope | Roles | Invitation effect |
|---|---|---|
| Organization membership | `Owner`, `Administrator`, `Member` | Not selectable here. Preserve an active role; establish only baseline `Member` when absent/removed; never reactivate a suspended membership or promote to `Owner`/`Administrator`. |
| Organization Workspace membership | `Administrator`, `Member` | An active Workspace `Administrator` may grant either role only in that Workspace. |
| Personal Workspace membership | `Owner` | Exactly one owner; invitations are rejected. |

## API and MCP classification

- Subject/current-workspace membership reads plus invite, resend, and revoke are authenticated REST/OpenAPI product operations with typed `[READ]`/`[WRITE]` MCP tools. MCP derives Organization, Workspace, inviter, and authority from its access token; tool arguments accept email, Workspace role, or invitation resource identity but never `userId` or an authority-bearing `workspaceId`.
- Email-token exchange, handoff resolution, registration/sign-in resumption, and recipient acceptance are intentionally internal/bootstrap because exposing token or browser-handoff workflows through MCP would create a second authentication path.

> **Implementation status**
>
> | Layer | Status |
> |---|---|
> | Identity Domain | Not started |
> | Identity Application | Not started |
> | Identity Infrastructure | Not started |
> | Audit | Not started |
> | API and authentication | Not started |
> | MCP | Not started |
> | Frontend | Not started |
>
> **Gaps vs spec:**
>
> | ID | Gap |
> |---|---|
> | GAP-001 | Invitations, lifecycle authorization, email delivery, acceptance, membership mutation, durable audit, REST/OpenAPI and MCP operations, and client journeys are not implemented. |
>
> **Deferred follow-ups:** Only the separately owned capabilities listed under Out Of Scope are deferred; no required production property of invitation or acceptance is deferred.
>
> **Verification:** Not run; implementation has not started and no acceptance row has current evidence.
>
> **Decisions:** Invitations target normalized email and one explicit organization Workspace role. Organization roles are never selectable or elevatable here: acceptance preserves an active role, establishes only baseline `Member` when absent/removed, and rejects suspended Organization or Workspace membership. Email tokens are hashed for validation; only the durable delivery outbox may hold an authenticated-encrypted, access-controlled, expiring envelope, deleted after accepted delivery or expiry. Ambiguous delivery retries the same generation; explicit resend supersedes it. A fragment-to-POST exchange removes token material before routing, telemetry, referrers, or logs and replaces it with a short-lived browser-bound handoff. Pending revocation is owned here. Required mutation and no-mutation audit outcomes fail closed if their outbox cannot persist. Terminal cleanup removes recipient email and all reversible token/handoff/delivery material after required work while preserving replay digests and the approved non-secret lifecycle record. Event sourcing is not introduced.
