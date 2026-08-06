# Create And Switch A Governed Workspace

> **Navigation**: [docs/use-cases/identity-governance/README.md](./README.md) · [docs/use-cases/README.md](../README.md) · [docs/PLATFORM_STRATEGY.md](../../PLATFORM_STRATEGY.md) · [docs/ARCHITECTURE.md](../../ARCHITECTURE.md) · [AGENTS.md](../../../AGENTS.md)

## Purpose

Let a verified Axis user create an organization with its initial governed workspace, enter that workspace, and switch safely between every eligible personal or organization workspace without leaking data or retaining stale authority.

## Primary actor

- Verified Axis user who owns an active personal workspace

## Trigger

- User needs a governed workspace for multiple real users or needs to change the active workspace for the current session.

## Main flow

1. User opens the workspace control from the authenticated app shell while their current workspace remains visible.
2. User chooses to create an organization, enters its name, and confirms creation of its initial workspace with the same display name.
3. System validates and normalizes the name, then atomically creates the Organization, active Organization owner membership, active organization Workspace, active Workspace administrator membership, and durable audit outbox records.
4. System reads the created organization, workspace, and memberships back before reporting creation success.
5. Client requests entry into the created workspace. Server validates the active Workspace membership and atomically persists a pending context transition, its source and target session correlations, expiry, and requested audit outbox record.
6. Server stages a new opaque Redis ticket bound to the pending transition and returns it in the browser cookie. While the transition is pending, both the source and target tickets are recovery-only and cannot authorize workspace data.
7. Browser confirms receipt through an antiforgery-protected request authenticated by the staged target ticket. Server revalidates membership and session correlation, atomically marks the transition completed with its audit outbox record, and idempotently revokes the source ticket. This durable completion is the authoritative commit point.
8. After confirmation, or after a lost confirmation response is resolved by reading the completed transition through the target ticket, client closes workspace-bound managed windows, clears workspace-scoped server state, refreshes the eligible workspace projection and antiforgery request token, and opens the safe dashboard in the new context.
9. Workspace control lists only active eligible workspaces, groups the personal workspace separately from organization workspaces, and marks the active workspace.
10. User selects the personal workspace or another eligible organization workspace and the same validated transition, confirmation, and cleanup flow repeats.
11. Every authenticated API operation validates the active Workspace membership and any associated transition state before using the workspace context; reads and mutations remain isolated to the selected workspace.

## Alternate / error flows

- Missing, blank, oversized, or otherwise invalid organization name: show a field-local error and perform no mutation.
- Repeated creation with the same idempotency key and payload: return the original completed outcome; the same key with different content fails closed.
- Concurrent creation or persistence conflict: create at most one complete organization/workspace graph and return a recoverable conflict without partial membership state.
- Audit outbox persistence failure: roll back the organization/workspace mutation and report a retryable failure.
- Session-store failure before a target ticket is staged: durably fail the transition, keep the new workspace available in the eligible list, retain the previous active context, and offer an explicit retry to enter the new workspace.
- Target-ticket response is lost before browser confirmation: the browser's source ticket enters recovery-only state; it may explicitly cancel the pending transition, invalidating the orphan target ticket and restoring the source context. An expired transition is compensated the same way by the reconciler.
- Confirmation response is lost after durable completion: a session read through the target ticket returns the completed target context so the client can finish cleanup without repeating the transition.
- Source-ticket revocation or other Redis cleanup fails after durable completion: the completed database state denies the source correlation before workspace data access and cleanup retries idempotently; it does not roll authority back to the source context.
- Confirmation and source-session recovery race: transition concurrency permits one terminal result. Completion keeps only the target context; compensation invalidates the target and restores only the still-valid source context.
- Inactive, removed, unknown, or otherwise ineligible target membership: keep the previous valid context, disclose no target resource data, and show a recoverable unavailable result.
- Current membership becomes invalid before a later request or reload: fail closed before module data access and present workspace recovery without silently selecting another workspace.
- Profile or workspace-list refresh failure after a successful switch: the rotated server session remains authoritative; clear workspace-bound client state and offer a retry without reverting locally to the old context.
- Personal workspace is unavailable: do not fabricate a personal context; list any other eligible workspaces or present an account-recovery state when none remain.

## Acceptance Criteria

*Happy path*

- **AC-001** A verified user with an active personal workspace can create one Organization and its initial active organization Workspace through one confirmed journey.
- **AC-002** Creation atomically establishes the creator's active Organization owner membership and active Workspace administrator membership without relying on legacy owner columns.
- **AC-003** Creation success requires persisted read-back of the Organization, Workspace, memberships, and durable audit outbox state.
- **AC-004** The user's eligible workspace projection includes the active personal workspace and every active organization Workspace for which the user has an active Workspace membership.
- **AC-005** The user can switch in both directions between personal and organization workspaces; each successful switch reaches durable completion only after browser confirmation with the staged target ticket, rotates the opaque browser session and antiforgery request token, and exposes no token, code, secret, ticket identifier, or session correlation.
- **AC-006** A successful switch closes workspace-bound managed windows, clears workspace-scoped client cache, refreshes workspace identity, and opens a safe route without rendering prior-workspace data.
- **AC-007** Every cookie- or bearer-authenticated workspace operation validates current active membership server-side; a workspace claim is context input rather than proof of authority.
- **AC-008** Organization creation and every active-context change produce durable append-only audit records with actor, subject, workspace, action, target, outcome, timestamp, and correlation identity without credentials or raw security tokens.

*Validation & errors*

- **AC-009** Organization names are trimmed, Unicode-normalized, required, and bounded; invalid input performs no mutation and returns actionable field-local diagnostics.
- **AC-010** Creation is idempotent for one key and canonical request, while key reuse with different content and concurrent conflicting creation fail closed without duplicate or partial Organization, Workspace, membership, or audit state.
- **AC-011** Organization/workspace persistence and audit-outbox persistence share one transaction; any failure before commit leaves no partial graph.
- **AC-012** A session-store failure before target-ticket staging preserves the prior active session; a lost target-ticket response leaves both tickets recovery-only until explicit or expiry-driven compensation restores the still-valid source session and removes the orphan target ticket.
- **AC-013** Switching to an inactive, removed, unknown, or otherwise ineligible membership leaves the prior valid context unchanged and exposes no target workspace or resource data.
- **AC-014** A stale or revoked current membership fails closed before workspace data access and presents a recoverable context-selection state rather than silently repairing authority.
- **AC-015** A post-switch profile refresh failure never causes the client to reuse or display cached data from the prior workspace.

*Edge cases and boundaries*

- **AC-016** Existing personal workspace owners are migration-backed into the same active Workspace membership model before legacy ownership authorization is removed; no dual authorization path remains.
- **AC-017** A personal Workspace has no Organization parent, admits exactly its migrated or newly created owner membership, and cannot receive additional members or invitations.
- **AC-018** An organization Workspace belongs to exactly one Organization and may support multiple Workspace memberships without granting access from Organization membership alone.
- **AC-019** Organization owner/administrator roles govern Identity lifecycle only; product roles such as applicant or caseworker remain versioned Authorization policy assignments.
- **AC-020** Resource-specific cross-workspace access returns a non-disclosing not-found outcome, while a known forbidden lifecycle action returns permission denied; the UI never substitutes for server authorization.
- **AC-021** Switching preserves no dirty form or managed-window state across workspaces, remains keyboard and screen-reader operable, and does not overflow compact or desktop layouts.
- **AC-022** Personal and organization workspace source data, records, Rules, audit context, and server caches remain isolated through repeated switching and concurrent requests.
- **AC-023** Context switching uses `Pending`, `Completed`, `Compensated`, and `Failed` durable transition states with optimistic concurrency and expiry; PostgreSQL completion after target-ticket confirmation is authoritative, every Redis operation is idempotent, and no pending or stale source correlation authorizes workspace data.
- **AC-024** Audit records contain only stable identifiers, categorical action/outcome, timestamp, correlation identity, and bounded non-sensitive metadata; indefinite append-only retention with no update/delete operation is the explicit policy of this slice, and any future change requires a new owning contract and migration.
- **AC-025** Every terminal transition path records one correlated requested and terminal audit outcome; failure to persist a required audit outbox state fails closed without inventing a cross-store transaction.
- **AC-026** Pending transitions reconcile by expiry. A terminal transition retains no ticket secret and is purged only after source and target absolute session lifetimes have elapsed, terminal audit projection is confirmed, and Redis cleanup has completed.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Browser journey | User creates an organization workspace, enters it, switches to personal, and switches back without prior-context UI or credential exposure | AC-001, AC-004, AC-005, AC-006, AC-021 | Browser automation | Yes |
| AT-002 | Application/Infrastructure boundaries | Creation commits Organization, Workspace, owner/admin memberships, audit outbox, and read-back atomically | AC-002, AC-003, AC-011 | Application test + Infrastructure integration test | Yes |
| AT-003 | UI/API boundaries | Invalid names and idempotency-key reuse return field or conflict diagnostics with no partial mutation | AC-009, AC-010 | UI component test + API integration test | Yes |
| AT-004 | Application/Infrastructure boundaries | Concurrent identical and conflicting creation attempts produce one canonical graph or a recoverable conflict | AC-010, AC-011 | Application test + Infrastructure integration test | Yes |
| AT-005 | API/Application boundaries | Eligible switch stages a target ticket, confirms browser receipt, durably completes, and revokes the source; lost staging/confirmation responses, expiry, concurrent confirm/recover, Redis cleanup failure, and ineligible targets preserve exactly one usable context or fail closed and reconcile idempotently | AC-005, AC-007, AC-012, AC-013, AC-014, AC-023, AC-025 | API integration test + Infrastructure integration test | Yes |
| AT-006 | UI component | Workspace control covers loading, grouped eligible list, current selection, empty/recovery, pending, confirmation read-back, success, and failure with focus and keyboard behavior | AC-004, AC-006, AC-012, AC-013, AC-015, AC-021 | UI component test | Yes |
| AT-007 | Infrastructure boundary | Existing personal owners migrate to one active membership, personal constraints hold, and legacy owner authorization is absent | AC-016, AC-017 | Infrastructure integration test + Architecture test | Yes |
| AT-008 | API/Application boundaries | Organization membership alone grants no workspace data access and lifecycle roles remain separate from product-policy roles | AC-007, AC-018, AC-019, AC-020 | Application test + API integration test | Yes |
| AT-009 | Infrastructure boundary | Creation and switch audit events are durable, append-only, correlated, idempotently projected, retention-bound, and limited to the approved redacted schema | AC-008, AC-011, AC-024 | Infrastructure integration test | Yes |
| AT-010 | Browser journey | Repeated switching while reading and mutating workspace resources proves server, client cache, managed-window, and audit isolation | AC-006, AC-007, AC-015, AC-022 | Browser automation | Yes |
| AT-011 | Infrastructure boundary | Transition expiry and terminal cleanup retain no ticket secret, reconcile pending state, wait for both session lifetimes plus audit/Redis completion, and then purge operational state | AC-023, AC-025, AC-026 | Infrastructure integration test | Yes |

## Out Of Scope

- Inviting or accepting additional members, owned by [docs/use-cases/identity-governance/invite-and-accept-workspace-member.md](./invite-and-accept-workspace-member.md).
- Team collaboration/assignment constructs and IdP/SCIM Groups.
- Product-specific applicant, caseworker, or other policy assignments.
- Service identities, Solutions installation, trusted publishers, and publisher revocation.
- Moving or copying definitions, records, Rules, or product state between workspaces.
- Renaming, suspending, deleting, restoring, or transferring Organizations and Workspaces.

## Screen flow

| Surface | Required contract |
|---|---|
| Workspace control | Show the current workspace first, group eligible choices into Personal and Organizations, expose one Create organization action, and never present an ineligible workspace discovered through another context. |
| Create organization | Request one organization display name, explain that an initial workspace with the same name will be created, show field-local validation, disable duplicate submission while pending, and keep retry state after recoverable failure. |
| Creation result | Identify the created Organization and Workspace, distinguish complete creation from a failed context switch, and provide one explicit enter/retry action. |
| Switch pending | Keep the current context visible until the server confirms rotation; prevent competing switch actions and announce progress without repeatedly stealing focus. |
| Switch success | Close workspace-bound windows, clear scoped cache, refresh current identity, and open the dashboard before rendering target-workspace content. |
| Switch failure/recovery | Preserve the prior valid context when possible; for stale current authority, show only eligible recovery choices and sign-out without exposing unavailable workspace data. |

Required UI quality: workspace labels and current state are programmatic; controls are keyboard reachable; selected/current state is not color-only; focus enters and returns from the create surface predictably; pending and result feedback is announced; compact layouts do not overflow; localized interface copy distinguishes Organization from Workspace; raw IDs, claims, tokens, secrets, and another workspace's metadata never render.

## API and MCP classification

- Organization/workspace creation and the subject-scoped eligible-workspace read are authenticated product operations exposed through REST/OpenAPI and typed `[WRITE]`/`[READ]` MCP tools. They derive user and current workspace authority from the access token and accept no `userId` or authority-bearing `workspaceId` argument.
- Browser switch, confirmation, compensation, session read, and antiforgery operations are intentionally internal/bootstrap. MCP changes workspace only through its OAuth authorization lifecycle; it never calls the browser ticket-transition protocol.

> **Implementation status**
>
> | Layer | Status |
> |---|---|
> | Identity Domain | Not started |
> | Identity Application | Not started |
> | Identity Infrastructure | Not started |
> | Audit | Not started |
> | API and session | Not started |
> | Frontend | Not started |
>
> **Gaps vs spec:**
>
> | ID | Gap |
> |---|---|
> | GAP-001 | Organization, unified Workspace memberships, migration, active-context rotation, durable audit, public operations, and client experience are not implemented. |
>
> **Deferred follow-ups:** Only the separately owned capabilities listed under Out Of Scope are deferred; no required production property of creation or switching is deferred.
>
> **Verification:** Not run; implementation has not started and no acceptance row has current evidence.
>
> **Decisions:** Organization is the governance container; Workspace is the active data/isolation context. Personal Workspaces have no Organization parent and use the same membership authorization model. Team is reserved for collaboration/assignment and Group for IdP or authorization grouping. Existing personal ownership is migrated in one clean cutover. Membership is validated server-side for cookie and bearer access. Browser switching is a two-request state machine: a recovery-only target ticket is staged, browser receipt is confirmed, and PostgreSQL completion becomes authoritative; response loss, concurrent recovery, Redis cleanup, and bounded operational-state purge are reconciled idempotently because PostgreSQL, Redis, and HTTP do not share a transaction. Client state clears only after confirmed completion. Identity lifecycle roles do not encode product roles. Security mutations write a transactional outbox that Audit projects idempotently into append-only records; indefinite retention of the approved minimal audit schema without product update/delete operations is the explicit policy of this slice. Event sourcing is not introduced.
