# Manage Product Role Assignments

> **Navigation**: [docs/use-cases/authorization/README.md](./README.md) · [docs/architecture/authorization.md](../../architecture/authorization.md) · [docs/use-cases/identity-governance/manage-workspace-service-identities.md](../identity-governance/manage-workspace-service-identities.md) · [AGENTS.md](../../../AGENTS.md)

## Purpose

Let an active Workspace lifecycle administrator assign or revoke one or more exact installed product roles for an active human or service subject in the current Workspace.

## Primary actor

- Active Workspace lifecycle administrator

## Supporting actors

- Solutions supplies installed immutable product policies.
- Existing human-administrator OAuth authorizes typed MCP role-assignment tools.
- Audit receives durable redacted assignment and decision outcomes.

## Preconditions

- The actor is the active `Owner` of the current personal Workspace or an active `Administrator` of the current organization Workspace.
- The target is an active human Workspace member or an active service identity with an active grant in that Workspace.
- The selected product roles are present in an installed current Solution policy for that Workspace.

## Trigger

- The administrator needs to grant or remove a product capability for a current Workspace subject.

## Success guarantee

- Each requested exact product-role assignment is created or revoked under the current Workspace, can be read back, and changes only product-policy authority.

## Minimal guarantee

- No lifecycle role, stale/inactive subject, unknown product role, cross-Workspace subject, client projection, or retry can grant unintended product authority.

## Main flow

1. Lifecycle administrator performs two explicit bootstrap steps: installs the applicable Solution policy and, only after installation succeeds, explicitly assigns exact initial product roles. Installation never derives an assignment from an Identity lifecycle role.
2. An active Workspace lifecycle administrator opens product-role assignment management for the current Workspace.
3. System shows only active current-Workspace human and service subjects and installed product roles with server-projected product-owned localized presentation for the exact current UI language or `en` fallback.
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

- **AC-001** Lifecycle administrator bootstrap explicitly installs a Solution and then explicitly assigns its initial product roles; installation itself creates no assignment, while `Administrator`, `Applicant`, and `Caseworker` remain reference-solution product-role keys rather than global or Identity lifecycle roles.
- **AC-002** The active `Owner` of a current personal Workspace or an active `Administrator` of a current organization Workspace can assign one or more exact installed product roles to one active human Workspace member or one active service subject in that Workspace.
- **AC-003** The same active Workspace lifecycle administrator can revoke an exact product-role assignment, and the role no longer grants product authority immediately.
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
- **AC-012** Each `authorization.policy.v1` role supplies product-owned BCP 47 language-tagged presentation entries with NFC-normalized bounded display name and optional description, includes at least `en`, and projects the exact current UI language or `en` fallback; neither the platform nor the client keeps a global/hardcoded role catalog.
- **AC-013** Product-role assignment administration is exposed through typed MCP tools authorized by the existing human administrator OAuth boundary; no service credential or token is accepted by or exposed through those tools.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | API boundary | Signed Solution installation creates no assignment; a later explicit personal `Owner` or organization `Administrator` request assigns one exact installed role without changing Identity lifecycle authority | AC-001, AC-002, AC-005, AC-007 | API integration test | Yes |
| AT-002 | Application/Infrastructure boundaries | Assignment, equivalent retry, stale revision, and concurrent assign/revoke preserve one canonical active authority outcome with audit read-back | AC-003, AC-004, AC-008, AC-009 | Application test + Infrastructure integration test | Yes |
| AT-003 | API boundary | Organization `Member`, missing actor/subject activity, unknown subject kind, unknown/stale policy/role, and cross-Workspace target deny without mutation or disclosure | AC-006 | Domain test + API integration test | Yes |
| AT-004 | API/Application boundaries | Revoked assignments deny the formerly permitted product operation immediately despite stale client state or projected UI affordance | AC-003, AC-010 | API integration test + Application test | Yes |
| AT-005 | Browser journey | Administrator assigns and revokes exact roles with accessible selection, pending, conflict, and recovery states | AC-004, AC-010, AC-011 | Browser automation | Yes |
| AT-006 | Infrastructure boundary | Assignment, revocation, denial, and failure audit outcomes are correlated, append-only, and redacted | AC-009 | Infrastructure integration test | Yes |
| AT-007 | API/Application boundaries | Invalid BCP 47 tags, missing `en`, non-NFC or over-bound presentation values reject policy installation; current-language projection follows the explicit fallback without platform/client role copy | AC-012 | Application test + API integration test | Yes |
| AT-008 | API/MCP boundaries | Typed product-role assignment tools use existing human-administrator OAuth and never accept or reveal service credentials or tokens | AC-005, AC-013 | API integration test + MCP contract test | Yes |

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
> | Authorization Domain | Done |
> | Authorization Application | Done |
> | Authorization Infrastructure | Done |
> | Audit | Done |
> | Solutions adapter | Done |
> | API | Done |
> | MCP | Done |
> | Frontend | Done |
>
> **Gaps vs spec:** None.
>
> **Deferred follow-ups:** Only the separately owned capabilities under Out Of Scope are deferred.
>
> **Verification:** Every required AT is mapped to current Application, PostgreSQL infrastructure, signed-Solution/API, MCP contract, durable audit, and focused browser evidence in [manage-product-role-assignments.evidence.md](./manage-product-role-assignments.evidence.md).
>
> **Decisions:** Assignment grants exact installed product roles only. Lifecycle administrator bootstrap may install a Solution and assign its initial product roles, but lifecycle authority never becomes product authority.
