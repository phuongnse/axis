# Manage Product Role Assignments

> **Navigation**: [docs/use-cases/authorization/README.md](./README.md) · [docs/architecture/authorization.md](../../architecture/authorization.md) · [docs/use-cases/identity-governance/manage-workspace-service-identities.md](../identity-governance/manage-workspace-service-identities.md) · [AGENTS.md](../../../AGENTS.md)

## Purpose

Let an active Workspace administrator assign or revoke one or more exact installed product roles for an active human or service subject in the current Workspace.

## Primary actor

- Active Workspace administrator

## Supporting actors

- Solutions supplies installed immutable product policies.
- Audit receives durable redacted assignment and decision outcomes.

## Preconditions

- The actor has active `Administrator` Workspace membership in the current Workspace.
- The target is an active human Workspace member or an active service identity with an active grant in that Workspace.
- The selected product roles are present in an installed current Solution policy for that Workspace.

## Trigger

- The administrator needs to grant or remove a product capability for a current Workspace subject.

## Success guarantee

- Each requested exact product-role assignment is created or revoked under the current Workspace, can be read back, and changes only product-policy authority.

## Minimal guarantee

- No lifecycle role, stale/inactive subject, unknown product role, cross-Workspace subject, client projection, or retry can grant unintended product authority.

## Main flow

1. Lifecycle administrator bootstrap installs the applicable Solution policy and assigns initial product roles; it does not convert any Identity lifecycle role into a product role.
2. An active Workspace administrator opens product-role assignment management for the current Workspace.
3. System shows only active current-Workspace human and service subjects and installed product roles with server-reported assignment state.
4. Administrator selects one or more exact product roles for one subject and confirms assignment or revocation.
5. System revalidates actor authority, current Workspace, subject activity, installed policy version, exact role keys, and concurrency state.
6. System commits the assignment lifecycle and required redacted audit outcome, then reads back the canonical result.

Policy and assignment realization is owned by [Authorization architecture](../../architecture/authorization.md#module-boundary).

## Alternate / error flows

- A missing or inactive administrator membership, target membership, service grant, or installed policy denies before assignment mutation and does not disclose a foreign subject or policy.
- An unknown, stale, inactive, or removed product role/policy version is not inferred from a display name and conflicts or denies without creating assignment authority.
- A cross-Workspace subject or assignment lookup returns a non-disclosing outcome.
- Equivalent retries return the canonical assignment result; changed-content reuse, stale revision, and conflicting concurrent assignment/revocation return a recoverable conflict or canonical terminal result without duplicate active authority.
- Required audit persistence or read-back failure fails closed and does not report the assignment lifecycle action as successful.

## Acceptance Criteria

*Happy path*

- **AC-001** Lifecycle administrator bootstrap installs a Solution and can assign its initial product roles, while `Administrator`, `Applicant`, and `Caseworker` remain reference-solution product-role keys rather than global or Identity lifecycle roles.
- **AC-002** An active current-Workspace administrator can assign one or more exact installed product roles to one active human Workspace member or one active service subject in that Workspace.
- **AC-003** An active current-Workspace administrator can revoke an exact product-role assignment, and the role no longer grants product authority immediately.
- **AC-004** Assignment management reads current subject, exact product-role key, policy/version identity, status, and canonical mutation result without credentials or cross-Workspace information.

*Validation and recovery*

- **AC-005** Identity lifecycle roles, Organization membership, client claims, UI visibility, Teams, and Groups neither grant nor imply a product-role assignment.
- **AC-006** Missing/inactive actor authority, inactive human membership, inactive service grant, unknown subject kind, unknown/stale policy or role, and cross-Workspace target deny by default before mutation.
- **AC-007** Product-role assignment never creates or changes a human membership, service identity, service grant, Organization role, Workspace lifecycle role, or Solution policy.
- **AC-008** Idempotent retry and assignment/revocation concurrency preserve a canonical per-subject, Workspace, policy-version, and role outcome without duplicate active authority or a silent overwrite.
- **AC-009** Assignment, revocation, denial, and failure outcomes are correlated, append-only, redacted, and fail closed when required audit work cannot persist.

*Client and boundaries*

- **AC-010** The assignment UI is a convenience projection only: it shows server-reported authority/state and cannot make a product operation succeed without server policy enforcement.
- **AC-011** Assignment screens support keyboard and screen-reader operation, clear pending/success/conflict/recovery states, compact layouts, and explicit selection of exact roles without presenting lifecycle roles as product roles.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Application boundary | Bootstrap assigns reference-solution initial roles without lifecycle-role conflation; active human and service subjects receive one or more exact installed roles | AC-001, AC-002, AC-005, AC-007 | Application test | Yes |
| AT-002 | Application/Infrastructure boundaries | Assignment, equivalent retry, stale revision, and concurrent assign/revoke preserve one canonical active authority outcome with audit read-back | AC-003, AC-004, AC-008, AC-009 | Application test + Infrastructure integration test | Yes |
| AT-003 | API boundary | Missing actor/subject activity, unknown subject kind, unknown/stale policy/role, and cross-Workspace target deny without mutation or disclosure | AC-006 | API integration test | Yes |
| AT-004 | API/Application boundaries | Revoked assignments deny the formerly permitted product operation immediately despite stale client state or projected UI affordance | AC-003, AC-010 | API integration test + Application test | Yes |
| AT-005 | Browser journey | Administrator assigns and revokes exact roles with accessible selection, pending, conflict, and recovery states | AC-004, AC-010, AC-011 | Browser automation | Yes |
| AT-006 | Infrastructure boundary | Assignment, revocation, denial, and failure audit outcomes are correlated, append-only, and redacted | AC-009 | Infrastructure integration test | Yes |

## Out Of Scope

- Policy authoring, policy editing, policy UI, policy inference, and installation mechanisms other than the Solutions adapter.
- Creating or changing Identity lifecycle roles, human memberships, service identities, service keys, or service grants.
- Group/Team mapping, bulk assignment, delegated administration, wildcard roles, role inheritance, and IdP/SCIM provisioning.

## Screen flow

| Surface | Required contract |
|---|---|
| Product-role assignments | Identify the current Workspace, active target subjects, installed exact product roles, and current assignment state from the server. |
| Assign roles | Permit selection of one or more exact product roles for one active subject, distinguish product roles from lifecycle roles, and require one explicit confirmation. |
| Revoke role | Name the exact product role and subject, require irreversible-action confirmation, then show immediate authoritative outcome and recovery guidance. |

> **Implementation status**
>
> | Layer | Status |
> |---|---|
> | Authorization Domain | Not started |
> | Authorization Application | Not started |
> | Authorization Infrastructure | Not started |
> | Audit | Not started |
> | Solutions adapter | Not started |
> | API | Not started |
> | Frontend | Not started |
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
> **Decisions:** Assignment grants exact installed product roles only. Lifecycle administrator bootstrap may install a Solution and assign its initial product roles, but lifecycle authority never becomes product authority.
