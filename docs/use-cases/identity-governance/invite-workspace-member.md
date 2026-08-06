# Invite A Workspace Member

> **Navigation**: [docs/use-cases/identity-governance/README.md](./README.md) · [docs/use-cases/README.md](../README.md) · [docs/architecture/identity-governance.md](../../architecture/identity-governance.md) · [AGENTS.md](../../../AGENTS.md)

## Purpose

Let an authorized Workspace administrator invite one real user into one governed Workspace with an explicit lifecycle role.

## Primary actor

- Active administrator of an organization Workspace

## Supporting actors

- Email delivery provider sends the invitation.
- Audit receives durable redacted lifecycle outcomes.

## Preconditions

- The inviter has active Organization membership and active administrator membership in the target organization Workspace.
- The target is not a personal Workspace.

## Trigger

- The administrator needs another real user to operate in the Workspace.

## Success guarantee

- One canonical pending invitation exists for the intended email and Workspace role, with durable delivery and audit work queued.

## Minimal guarantee

- No unauthorized membership is created, no parallel valid token is left behind, and token material is not exposed.

## Main flow

1. Administrator opens membership management for the active organization Workspace.
2. Administrator enters the recipient email, selects `Workspace administrator` or `Workspace member`, reviews the target, and confirms.
3. System validates current authority, email, target, role, rate limit, and idempotency.
4. System commits one pending invitation with its delivery and audit work.
5. A worker sends a localized invitation using the canonical token generation.
6. Membership management shows the non-secret pending and delivery outcome and offers authorized resend or pending revocation.

Token storage, delivery retry, resend, concurrency, audit, and cleanup realization is owned by [Identity Governance architecture](../../architecture/identity-governance.md#invitation-delivery-realization).

## Alternate / error flows

- Invalid email, unsupported role, personal Workspace, or cross-Organization target: reject before mutation.
- Missing authority: deny without disclosing unrelated membership data and persist the required attempted-action audit outcome.
- Equivalent pending invitation: return the canonical pending outcome without another valid token.
- Existing active Workspace membership: return an existing-member outcome and create no invitation.
- Delivery interruption or ambiguous provider outcome: retry the same token generation and delivery key.
- Terminal delivery failure: keep the invitation pending and allow an authorized rate-limited resend that supersedes the old generation.
- Resend or revocation races exchange or acceptance: optimistic concurrency permits one terminal lifecycle outcome.
- Required mutation or no-mutation audit persistence fails: fail closed and do not report the lifecycle action as successful.

## Acceptance Criteria

*Happy path*

- **AC-001** An active target-Workspace administrator with active Organization membership can invite one normalized email address with `Workspace administrator` or `Workspace member`.
- **AC-002** Invitation creation records one canonical pending invitation with inviter, Organization, Workspace, requested Workspace role, expiry, status, and concurrency state.
- **AC-003** Creation durably queues a localized email delivery that identifies the Organization, Workspace, inviter, requested role, expiry, security context, and acceptance action without internal IDs or secrets.
- **AC-004** The administrator can read pending, delivery, accepted, or revoked lifecycle outcomes without token material.
- **AC-005** An authorized administrator can rate-limited resend a pending invitation, explicitly superseding the prior token generation.
- **AC-006** An authorized administrator can idempotently revoke only a pending invitation.

*Validation and recovery*

- **AC-007** Invalid email, unsupported Workspace role, personal Workspace target, and cross-Organization target fail before invitation creation with non-disclosing diagnostics.
- **AC-008** Missing Organization membership or target-Workspace administrator authority denies creation and durably records the required redacted attempt outcome.
- **AC-009** An equivalent pending request returns one canonical outcome and never leaves multiple valid tokens.
- **AC-010** Existing active membership returns an existing-member outcome without an invitation or duplicate membership.
- **AC-011** Crash and ambiguous provider recovery reuse the same token generation and delivery correlation key.
- **AC-012** Revocation, exchange, and acceptance concurrency permits one terminal outcome; revocation never removes an accepted membership.

*Boundaries*

- **AC-013** Invitation roles cannot select, assign, or elevate an Organization role or product permission.
- **AC-014** Personal Workspaces reject invitations and additional memberships.
- **AC-015** Invite, resend, revoke, denied authority, delivery, and terminal outcomes produce correlated append-only redacted audit records.
- **AC-016** REST/OpenAPI and typed MCP operations expose invite, resend, revoke, and lifecycle reads with server-derived Organization, Workspace, inviter, and authority.
- **AC-017** Membership management and invitation forms are keyboard and screen-reader operable, compact-layout safe, and provide recovery for terminal delivery failure.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Browser journey | Administrator creates an invitation and reads its pending delivery outcome accessibly | AC-001, AC-003, AC-004, AC-017 | Browser automation | Yes |
| AT-002 | Application/Infrastructure boundaries | Creation persists one canonical pending invitation, encrypted delivery work, concurrency state, and audit outbox | AC-002, AC-009, AC-015 | Application test + Infrastructure integration test | Yes |
| AT-003 | UI/API boundaries | Invalid target, role, email, authority, and existing membership return non-disclosing outcomes without invitation mutation | AC-007, AC-008, AC-010, AC-014 | UI component test + API integration test | Yes |
| AT-004 | Infrastructure boundary | Crash and ambiguous provider outcomes retry the same localized generation without exposing internal or secret material | AC-003, AC-011 | Infrastructure integration test | Yes |
| AT-005 | Application/Infrastructure boundaries | Authorized resend supersedes one generation and concurrent revoke, exchange, or acceptance produces one terminal lifecycle outcome | AC-005, AC-006, AC-012 | Application test + Infrastructure integration test | Yes |
| AT-006 | Application boundary | Workspace-role invitation cannot assign Organization roles or product permissions | AC-013 | Application test | Yes |
| AT-007 | Infrastructure boundary | Mutation and denied/no-mutation outcomes project into correlated append-only redacted audit records | AC-008, AC-015 | Infrastructure integration test | Yes |
| AT-008 | API/MCP boundaries | Typed invite, resend, revoke, and lifecycle-read operations derive all authority from authentication | AC-016 | API integration test + MCP contract test | Yes |

## Out Of Scope

- Recipient authentication and acceptance, owned by [Accept A Workspace Invitation](./accept-workspace-invitation.md).
- Creating or switching Workspaces.
- Removing or suspending an accepted membership.
- Organization-role assignment, product permissions, Team or Group assignment, IdP/SCIM, bulk import, and public invitation links.
- Service identities and non-human invitation targets.

## Screen flow

| Surface | Required contract |
|---|---|
| Membership management | Identify the target, show non-secret membership and invitation outcomes, and expose actions only with server-reported authority. |
| Invite member | Request email and one allowed Workspace role, keep the target visible, show field errors, and prevent duplicate submission. |
| Delivery recovery | Explain terminal failure without secrets and offer authorized resend or pending revocation. |

> **Implementation status**
>
> | Layer | Status |
> |---|---|
> | Identity Domain | Not started |
> | Identity Application | Not started |
> | Identity Infrastructure | Not started |
> | Audit | Not started |
> | API | Not started |
> | MCP | Not started |
> | Frontend | Not started |
>
> **Gaps vs spec:**
>
> | ID | Gap |
> |---|---|
> | GAP-001 | Invitation lifecycle, authorization, delivery, audit integration, REST/OpenAPI, MCP, and the client journey are not implemented. |
>
> **Deferred follow-ups:** Only the separately owned capabilities under Out Of Scope are deferred.
>
> **Verification:** Not run; no acceptance row has current evidence.
>
> **Decisions:** Invitation grants one explicit Workspace lifecycle role only. Delivery uses one canonical token generation; resend supersedes it and revocation applies only while pending. Technical realization is owned by the linked architecture contract.
