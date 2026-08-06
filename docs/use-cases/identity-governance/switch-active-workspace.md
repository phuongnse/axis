# Switch Active Workspace

> **Navigation**: [docs/use-cases/identity-governance/README.md](./README.md) · [docs/use-cases/README.md](../README.md) · [docs/architecture/identity-governance.md](../../architecture/identity-governance.md) · [AGENTS.md](../../../AGENTS.md)

## Purpose

Let an authenticated user change the active Workspace safely without retaining stale authority or disclosing data from either context.

## Primary actor

- Authenticated Axis user with at least one active Workspace membership

## Supporting actors

- Audit receives requested and terminal transition outcomes.

## Preconditions

- The user has a valid current session or is in the explicit context-recovery state.
- The target appears in the user's server-derived eligible-Workspace projection.

## Trigger

- The user selects another eligible personal or organization Workspace.

## Success guarantee

- Exactly the confirmed target context authorizes subsequent requests, and the client renders no state from the prior Workspace.

## Minimal guarantee

- The protocol preserves at most one usable context or fails closed into recovery without disclosing an ineligible Workspace.

## Main flow

1. Workspace control lists only active eligible Workspaces, groups the personal Workspace separately, and marks the current context.
2. User selects an eligible target.
3. System revalidates active membership and begins a recoverable context transition.
4. Browser confirms receipt of the staged target context with antiforgery protection.
5. System durably completes the transition, records the terminal audit outcome, and invalidates source authority.
6. Client closes Workspace-bound windows, clears scoped state, refreshes identity and antiforgery state, and opens a safe target route before rendering target data.

The cross-store state machine, recovery, cleanup, authorization, and audit realization is owned by [Identity Governance architecture](../../architecture/identity-governance.md#browser-context-transition-realization).

## Alternate / error flows

- Ineligible or unknown target: leave the prior valid context unchanged and disclose no target resource data.
- Session-store failure before staging: durably fail the attempt and preserve the prior context.
- Lost staging response: explicit or expiry-driven compensation invalidates the orphan target and restores only the still-valid source.
- Lost completion response: read the completed target context back and finish client cleanup idempotently.
- Completion races recovery: exactly one terminal result wins.
- Source cleanup fails after completion: completed state denies source authority while cleanup retries.
- Current membership becomes stale or revoked: deny module data access and show only eligible recovery choices or sign-out.
- Post-switch refresh fails: keep the new server context authoritative, retain cleared client state, and offer retry.

## Acceptance Criteria

*Happy path*

- **AC-001** The eligible projection contains the active personal Workspace and each active organization Workspace for which the subject has active Workspace membership.
- **AC-002** The user can switch in both directions between eligible personal and organization Workspaces.
- **AC-003** A switch becomes authoritative only after browser confirmation and durable completion of the target transition.
- **AC-004** Successful completion rotates browser session and antiforgery state without exposing token, ticket, secret, or session-correlation material.
- **AC-005** After completion, the client closes Workspace-bound windows, clears scoped state, refreshes identity, and opens a safe route before rendering target data.

*Validation and recovery*

- **AC-006** Every cookie- or bearer-authenticated Workspace operation revalidates current active membership server-side.
- **AC-007** An ineligible or unknown target preserves the prior valid context and discloses no target resource data.
- **AC-008** A stale or revoked current membership fails closed before Workspace data access and exposes an explicit recovery state.
- **AC-009** Failure before target staging preserves the prior active session.
- **AC-010** Lost staging, expiry, and confirm-versus-recover races reconcile idempotently to at most one usable context.
- **AC-011** Lost completion response and post-completion source cleanup failure recover from durable state without restoring source authority.
- **AC-012** A post-switch identity refresh failure never renders or reuses cached prior-Workspace data.

*Boundaries*

- **AC-013** Repeated switching and concurrent requests preserve server data, cache, managed-window, and audit isolation between Workspaces.
- **AC-014** Resource-specific cross-Workspace access returns a non-disclosing not-found outcome, while a known forbidden Identity lifecycle action returns permission denied.
- **AC-015** Each transition records correlated requested and terminal redacted audit outcomes; required audit persistence failure fails closed.
- **AC-016** Browser transition operations remain internal/bootstrap; MCP changes Workspace only through its OAuth authorization lifecycle.
- **AC-017** Workspace control and recovery states are keyboard and screen-reader operable, compact-layout safe, and never expose unavailable Workspace metadata.
- **AC-018** Identity audit outcomes are retained indefinitely with no product update or delete operation; any expiry, purge, mutation, or retention change requires a new owning contract and migration.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Browser journey | User switches personal to organization and back without prior-context UI or credential exposure | AC-001, AC-002, AC-004, AC-005, AC-017 | Browser automation | Yes |
| AT-002 | API/Application boundaries | Eligible transition confirms and completes before target authority while every later operation revalidates membership | AC-003, AC-006 | API integration test + Application test | Yes |
| AT-003 | Application/Infrastructure boundaries | Staging failure, lost staging, expiry, and confirm-versus-recover races retain at most one usable context | AC-009, AC-010 | Application test + Infrastructure integration test | Yes |
| AT-004 | API/Application boundaries | Unknown, inactive, removed, and stale memberships fail closed with the required non-disclosing recovery outcome | AC-007, AC-008, AC-014 | API integration test + Application test | Yes |
| AT-005 | Application/Infrastructure boundaries | Lost completion and session-store cleanup failure recover from durable completion without restoring source authority | AC-011 | Application test + Infrastructure integration test | Yes |
| AT-006 | UI component | Workspace control covers eligible, current, pending, success, refresh-failure, unavailable, and recovery states accessibly | AC-001, AC-005, AC-012, AC-017 | UI component test | Yes |
| AT-007 | Browser journey | Repeated switching during reads and mutations proves client and server isolation | AC-013 | Browser automation | Yes |
| AT-008 | Infrastructure boundary | Requested and terminal transition audit outcomes remain correlated, redacted, durable, immutable, unavailable to product update/delete operations, and retained under the current indefinite policy | AC-015, AC-018 | Infrastructure integration test | Yes |
| AT-009 | API/MCP boundaries | Browser transition routes remain internal and MCP changes context only through OAuth lifecycle | AC-016 | API integration test + MCP contract test | Yes |

## Out Of Scope

- Creating an Organization or Workspace, owned by [Create An Organization Workspace](./create-organization-workspace.md).
- Inviting, accepting, removing, or suspending members.
- Moving or copying definitions, records, Rules, or product state between Workspaces.
- Product-role, Team, Group, IdP/SCIM, and service-identity management.

## Screen flow

| Surface | Required contract |
|---|---|
| Workspace control | Show current and eligible choices only, grouped by Personal and Organizations. |
| Switch pending | Keep the current label visible, prevent competing transitions, and announce progress. |
| Switch success | Clear prior state and open a safe target route before target content renders. |
| Recovery | Show only eligible choices and sign-out, with a concrete retry where recovery is safe. |

> **Implementation status**
>
> | Layer | Status |
> |---|---|
> | Identity Domain | Done |
> | Identity Application | Done |
> | Identity Infrastructure | Done |
> | Audit | Done |
> | API and session | Done |
> | MCP | Done |
> | Frontend | Done |
>
> **Gaps vs spec:** N/A.
>
> **Deferred follow-ups:** Only the separately owned capabilities under Out Of Scope are deferred.
>
> **Verification:** Every required AT is mapped to current passing browser, UI component, Application, PostgreSQL, Audit, API/OpenAPI, and MCP evidence in [switch-active-workspace.evidence.md](./switch-active-workspace.evidence.md).
>
> **Decisions:** Context change is a recoverable two-request protocol whose durable completion is authoritative. The linked Identity Governance architecture contract owns its cross-store realization and threat model.
