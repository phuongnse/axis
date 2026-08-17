# Manage Workspace Product Builders

> **Navigation**: [docs/use-cases/identity-governance/README.md](./README.md) · [docs/architecture/identity-governance.md](../../architecture/identity-governance.md) · [docs/architecture/authorization.md](../../architecture/authorization.md) · [AGENTS.md](../../../AGENTS.md)

## Purpose

Give a Workspace an explicit human Product Builder authority for authoring reusable product definitions without requiring an installed product policy or treating a Workspace lifecycle role as product authority.

## Primary actor

- Active human Workspace member with Product Builder authority
- Active Workspace lifecycle administrator managing Product Builder authority for another active human member

## Preconditions

- The current subject is an active human member of the current Workspace.
- A grant or revocation actor is the active `Owner` of the current personal Workspace or an active `Administrator` of the current organization Workspace.
- A grant or revocation target is a different active human member of the same organization Workspace.

## Trigger

- A Workspace creator needs to author Business Object and Rule definitions before any Solution or product role exists.
- A lifecycle administrator needs to grant or revoke that authoring authority for another organization Workspace member.

## Success guarantee

- The exact active human membership has one server-authoritative Product Builder state, and authoring availability changes immediately without installing a policy or changing a lifecycle or product role.

## Minimal guarantee

- No lifecycle role, product role, service identity, client projection, inactive membership, or cross-Workspace identifier can imply or preserve Product Builder authority.

## Main flow

1. Personal Workspace registration atomically creates the active personal `Owner` membership with Product Builder authority.
2. Organization creation atomically creates the creator's active organization Workspace `Administrator` membership with Product Builder authority.
3. An organization Workspace lifecycle administrator opens membership administration and sees the Product Builder state of active human members.
4. The administrator explicitly grants or revokes Product Builder for another active human member using the target membership's current revision.
5. Identity revalidates actor authority, active current Workspace, distinct active human target, and revision, then commits the new state and required redacted audit outcome.
6. Every product-authoring module registered by the current composition resolves the same Identity-owned Product Builder decision for navigation, action projection, disclosure, and mutation. Identity neither knows nor persists the registered module list.
7. Product runtime operations continue to resolve their own exact installed product-policy grants. A current module may expose a separate runtime read contract without exposing its unpublished authoring state.

Product Builder persistence, creator bootstrap, membership lifecycle interaction, and grant/revoke authority are owned by [Identity Governance architecture](../../architecture/identity-governance.md#product-builder-realization). Product record authorization remains owned by [Authorization architecture](../../architecture/authorization.md#evaluation-and-enforcement).

## Alternate / error flows

- An organization `Administrator` without an explicit Product Builder grant may manage eligible builders but cannot author product definitions.
- An organization `Member` with Product Builder authority may author product definitions but cannot manage members, builders, Solutions, service identities, or product-role assignments.
- An inactive, suspended, removed, foreign, unknown, or service target is denied without changing or disclosing authority.
- An actor cannot grant or revoke Product Builder for themselves. A personal `Owner` retains creator authority and has no additional human target.
- Suspending a membership makes its Product Builder authority immediately ineffective. Reactivation restores the unchanged explicit state; removal clears Product Builder so invitation-based restoration cannot resurrect it.
- An equivalent requested state returns the canonical current projection; a stale revision or conflicting concurrent change returns a recoverable conflict without overwriting the current state.
- An authorization dependency or required persistence, audit, or read-back failure fails closed and does not report success.

## Acceptance Criteria

*Bootstrap and authoring*

- **AC-001** A newly registered personal Workspace creator has Product Builder authority before any Solution, product policy, or product-role assignment exists.
- **AC-002** A newly created organization Workspace creator has Product Builder authority before any Solution, product policy, or product-role assignment exists.
- **AC-003** Product Builder authority is a module-neutral decision consumed consistently by every currently registered authoring adapter; the current Rules and Business Objects contributions and server operations prove that integration without defining the capability.
- **AC-004** Published Business Object definition reads required by a product runtime remain governed by exact product policy and return only published runtime-readable state when the subject is not a Product Builder.

*Management and isolation*

- **AC-005** An active organization Workspace lifecycle administrator can list active human members and explicitly grant or revoke Product Builder for a different active member using optimistic concurrency.
- **AC-006** Product Builder authority is independent of `Owner`, `Administrator`, and `Member` role values: an administrator is not implicitly a builder, and a member may be an explicit builder.
- **AC-007** Product Builder grants apply only to active human membership in the exact Workspace and never create or change Workspace access, lifecycle roles, Organization roles, product roles, service grants, or installed policies.
- **AC-008** Self-change, service identity, inactive membership, unknown target, stale revision, and cross-Workspace target fail closed without authority change or foreign-resource disclosure.
- **AC-009** Suspension disables Product Builder immediately, reactivation restores the unchanged explicit state, and membership removal clears it before any later restoration.
- **AC-010** Equivalent requests and concurrent grant/revoke preserve one canonical membership state without duplicate or silently overwritten authority.

*Security and client boundaries*

- **AC-011** Navigation and client action projections consume the same server-authoritative Product Builder decision as authoring operations and never grant authority themselves.
- **AC-012** Every currently registered authoring adapter requires the same generic Product Builder decision; current Rules and Business Objects tests are integration evidence rather than Identity-owned module semantics.
- **AC-013** Grant, revocation, denial, and failure outcomes are correlated, redacted, and fail closed when required audit work cannot be confirmed.
- **AC-014** The management UI is keyboard and screen-reader operable, names the current Workspace and target member, distinguishes lifecycle role from Product Builder, and exposes pending, success, conflict, denial, and recovery states.
- **AC-015** Identity Product Builder persistence, application contracts, API projections, and authority decisions contain no module, route, resource-type, or action registry; adding or removing an authoring adapter requires no Identity data migration.
- **AC-016** Membership administration exposes each active human membership's complete server-owned current-state metadata in the Resource Workspace order: revision, created actor/time, and modified actor/time. Actor cells show the authenticated user's display name; membership and Product Builder mutations preserve one truthful provenance trail.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Application/Infrastructure boundaries | Personal registration and organization creation commit creator Product Builder state in the same transaction and read it back without any product-policy state | AC-001, AC-002, AC-006 | Application test + Infrastructure integration test | Yes |
| AT-002 | API boundary | A blank Personal creator and a blank organization Workspace creator receive Rules and Business Objects navigation and can begin authoring | AC-001, AC-002, AC-003, AC-011 | API integration test | Yes |
| AT-003 | API/Application boundaries | A non-builder administrator can manage another member's Product Builder state but cannot author; a builder member can author but cannot administer | AC-005, AC-006, AC-007, AC-011 | API integration test + Application test | Yes |
| AT-004 | API/Application boundaries | Current Rules definition/binding and Business Object unpublished reads/mutations deny a non-builder and use the same Product Builder decision | AC-003, AC-012 | API integration test + Application test | Yes |
| AT-005 | API/Application boundaries | A non-builder with exact published-definition product authority can read published Business Object runtime metadata but cannot read unpublished state or author | AC-004, AC-012 | API integration test + Application test | Yes |
| AT-006 | Application boundary | Suspend/reactivate/remove, self-change, service, inactive, stale, concurrent, and foreign targets preserve fail-closed authority invariants | AC-008, AC-009, AC-010 | Domain test + Application test | Yes |
| AT-007 | Infrastructure boundary | Grant, revoke, denial, and failure audit outcomes are durable, correlated, append-only, and redacted | AC-013 | Infrastructure integration test | Yes |
| AT-008 | UI/API boundaries | Membership administration projects independent role and Product Builder state with accessible grant/revoke, pending, conflict, recovery, and complete current-state metadata behavior | AC-005, AC-006, AC-014, AC-016 | UI component test + API integration test | Yes |
| AT-009 | Browser journey | Authenticated blank Personal and organization creator Workspaces show Rules and Business Objects; a granted member gains and a revoked member loses both authoring surfaces after authoritative refresh | AC-001, AC-002, AC-003, AC-005, AC-011, AC-014 | Browser automation | Yes |
| AT-010 | Architecture boundary | Identity Product Builder code and storage contain no dependency on current authoring modules or their routes/actions; current modules depend only on the generic Identity contract | AC-003, AC-015 | Architecture test | Yes |

## Out Of Scope

- Product policy authoring, Solution packaging/signing, Solution installation, and product-role assignment.
- Product record access, workflow permissions, Team or Group semantics, service-identity Product Builders, and cross-Workspace grants.
- Additional Workspace capabilities or a generic role/permission designer.
- Changing Organization or Workspace lifecycle roles, membership invitation semantics, or personal Workspace membership cardinality.

## Screen flow

| Surface | Required contract |
|---|---|
| Module navigation | Show Rules and Business Objects only when the current active human subject has the server-projected Product Builder authority; runtime product routes remain separately projected. |
| Membership administration | Add one Product Builder state for each active human member, visually separate from lifecycle role, with an explicit grant or revoke action only for eligible non-self targets. |
| Grant/revoke confirmation | Name the member and effect on product authoring, preserve the current membership revision, and show the authoritative result or recoverable conflict. |

Required UI quality: use the existing Resource Workspace and Managed Task Window foundations, keep state server-derived, preserve focus and table state, require confirmation for revocation, and avoid document scrolling or horizontal overflow at supported widths.

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
> | Current authoring adapters | Done |
> | Frontend | Done |
>
> **Gaps vs spec:** None.
>
> **Deferred follow-ups:** Solution web authoring and generic product runtime UI remain the next Wave 2 vertical slice after this bootstrap authority is complete.
>
> **Verification:** Every required AT is mapped to current Domain, Application, PostgreSQL Infrastructure, API/OpenAPI, MCP, architecture, frontend component, reference-package, and governed browser evidence in [manage-workspace-product-builders.evidence.md](./manage-workspace-product-builders.evidence.md).
>
> **Decisions:** Product Builder is an Identity-owned, module-neutral explicit state on active human Workspace membership, bootstrapped only for Workspace creators. It is not a lifecycle role, installed product-policy grant, or fixed list of current authoring implementations.
