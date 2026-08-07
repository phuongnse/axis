# Access Product By Policy

> **Navigation**: [docs/use-cases/authorization/README.md](./README.md) · [docs/architecture/authorization.md](../../architecture/authorization.md) · [docs/use-cases/authorization/manage-product-role-assignments.md](./manage-product-role-assignments.md) · [AGENTS.md](../../../AGENTS.md)

## Purpose

Let an active human or service subject access a product capability only when an installed immutable product policy grants its exact action and resource scope in the current Workspace.

## Primary actor

- Active human or service subject using a product capability

## Supporting actors

- Product client projects server-reported affordances for convenience.
- Audit receives durable redacted policy-governed allow and deny outcomes.

## Preconditions

- The subject is authenticated and has active Workspace access through the shared Identity policy.
- The current Workspace has an installed current Solution policy and any required active exact product-role assignment.

## Trigger

- The subject requests a product action or reads a product resource in the current Workspace.

## Success guarantee

- The operation proceeds only when server-authoritative evaluation matches the subject's active exact product-role assignment, installed immutable policy version, exact action/resource keys, and record scope.

## Minimal guarantee

- Missing, unknown, stale, inactive, or cross-Workspace inputs deny by default without disclosing a foreign resource or treating client visibility as authority.

## Main flow

1. Subject requests a product operation in its current Workspace.
2. System authenticates the subject and passes the shared Workspace-access policy before product data access.
3. Product operation asks Authorization to evaluate the exact installed policy/version, subject's active assignments, action key, resource/object key, and record scope.
4. Authorization permits only an exact matching grant. For a record operation, `Own` resolves ownership to the evaluated subject; `All` resolves any matching record in the same Workspace.
5. Before returning the decision, Authorization commits and reads back the required redacted allow or deny audit outcome. Audit failure denies without product data access.
6. Product operation performs the allowed read or mutation and returns its normal result. Client projections may show the resulting affordance but do not replace this check.

Policy evaluation and product-boundary realization is owned by [Authorization architecture](../../architecture/authorization.md#evaluation-and-enforcement).

## Alternate / error flows

- Missing Workspace access, unknown subject kind, inactive membership/grant, absent assignment, unknown/stale policy/version, unknown role, action, resource, or object key denies by default before resource disclosure or mutation.
- A non-matching `Own` record denies; it never broadens to `All`. A missing scope or resource key never implies a broader grant.
- Cross-Workspace lookup and policy evaluation return a non-disclosing not-found style outcome.
- A role assignment, service grant, service identity, or key revoked while a client holds stale state denies at the server immediately.
- Authorization, policy-resolution, or required audit dependency failure fails closed. The client exposes a stable recovery state and does not retry a mutation as allowed without a fresh authoritative response.

## Acceptance Criteria

*Happy path*

- **AC-001** Server enforcement evaluates active human membership or active service grant through the shared Workspace-access policy before any policy-governed product data access; unknown subject kinds deny.
- **AC-002** An active assigned product role can perform only the exact installed policy action and optional exact resource/object-key grant for the current Workspace.
- **AC-003** Record policy distinguishes `Own` from `All`: `Own` permits only the evaluated subject's matching current-Workspace records, while `All` permits all matching records in that Workspace.
- **AC-004** The Wave 1 reference policy permits product `Administrator` to manage definitions and read all records; permits `Applicant` to create, read, save, and submit own records; and permits `Caseworker` to list and read all records but not mutate records.

*Validation and recovery*

- **AC-005** Missing, unknown, stale, inactive, or non-matching policy, policy version, role, assignment, subject, action, resource, object key, Workspace access, or record ownership denies by default before data disclosure or mutation.
- **AC-006** Missing resource/object/scope input never grants a broader action; an `Own` mismatch never falls back to `All`.
- **AC-007** Cross-Workspace policy-governed reads and mutations are non-disclosing, and an inactive/revoked human membership, service identity, service grant, signing key, or product-role assignment denies immediately despite an existing token or client cache.
- **AC-008** Server enforcement remains authoritative; forged, stale, missing, or permissive UI/client projection cannot make a denied product operation succeed.
- **AC-009** Required policy-governed allow, deny, and failure outcomes are correlated, append-only, redacted, and fail closed when required audit work cannot persist.

*Client and boundaries*

- **AC-010** Product clients present only server-reported action affordances and provide accessible forbidden, non-disclosing-not-found, unavailable, and retryable-recovery states without revealing policy internals or cross-Workspace resources.
- **AC-011** Policies are immutable versioned Solution components installed only through the Solutions adapter; no policy-authoring API or UI exists in this slice.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Application boundary | Exact policy/version/role/action/resource matching permits only explicitly granted current-Workspace operations and denies unknown subjects/keys by default | AC-001, AC-002, AC-005 | Application test | Yes |
| AT-002 | API boundary | `Own` permits an Applicant's own record only; `All` permits the intended current-Workspace collection/read outcome and `Own` never broadens | AC-003, AC-006 | Application test + API integration test | Yes |
| AT-003 | API boundary | Reference Administrator, Applicant, and Caseworker outcomes prove permitted actions and Caseworker mutation denial | AC-004 | API integration test | Yes |
| AT-004 | API/Application boundaries | Revoked membership, service grant/key/identity, assignment, stale policy, and cross-Workspace requests deny immediately and non-disclosingly | AC-005, AC-007 | API integration test + Application test | Yes |
| AT-005 | API boundary | Forged or stale client affordances cannot bypass server enforcement; client exposes accessible denial/not-found/unavailable recovery without policy disclosure | AC-008, AC-010 | API integration test + UI component test | Yes |
| AT-006 | Infrastructure boundary | Policy-governed allows, denies, and dependency failures have correlated redacted audit read-back and fail closed when audit persistence is unavailable | AC-009 | Infrastructure integration test | Yes |
| AT-007 | Application boundary | Only the Solutions adapter installs immutable versioned policies; policy-authoring operations and UI are absent | AC-011 | Application test + API integration test | Yes |

## Out Of Scope

- Authoring, editing, or browsing policy source; policies arrive only through the Solutions adapter.
- Global product roles, wildcard/inherited permissions, dynamic expressions, delegated administration, Group/Team mapping, IdP/SCIM mapping, and client-side authorization enforcement.
- Managing Identity lifecycle, service identity/key lifecycle, and product-role assignment lifecycle.

## Screen flow

| Surface | Required contract |
|---|---|
| Product collection/detail/action | Render only server-reported current-Workspace data and action affordances; re-evaluate after server response rather than trusting cached permission state. |
| Forbidden or unavailable result | Explain that the action cannot proceed without exposing policy internals, foreign-resource existence, credentials, or cross-Workspace identifiers; offer an appropriate safe return or retry. |

> **Implementation status**
>
> | Layer | Status |
> |---|---|
> | Authorization Domain | Not started |
> | Authorization Application | Not started |
> | Authorization Infrastructure | Not started |
> | Audit | Not started |
> | Solutions adapter | Not started |
> | Product module integration | Not started |
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
> **Decisions:** The server evaluates exact immutable product-policy grants after shared Workspace access. `Own` and `All` are distinct outcomes; client projection is convenience only and policies are installed, never authored, through Solutions.
