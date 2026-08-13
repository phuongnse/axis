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
- A record's owner is a product-module-owned discriminated subject reference, while every product mutation actor is a discriminated subject reference.

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
4. Authorization permits only an exact matching grant. For a record operation, `Own` resolves the module-owned persisted owner to the evaluated subject; `All` resolves any matching record in the same Workspace.
5. The owning product module filters collections before materialization and enforces record ownership for get, save, and submit inside its boundary. Creation stamps the authenticated subject server-side and never accepts caller-provided owner data.
6. Before returning an authoritative allow or deny decision, Authorization commits and reads back the required redacted audit outcome. A policy-resolution, persistence, or required-audit dependency failure returns `Unavailable` without product data access.
7. Product operation performs the allowed read or mutation and returns its normal result. Client projections may show the resulting affordance but do not replace this check.

Policy evaluation and product-boundary realization is owned by [Authorization architecture](../../architecture/authorization.md#evaluation-and-enforcement).

## Alternate / error flows

- Missing Workspace access, unknown subject kind, inactive membership/grant, absent assignment, unknown/stale policy/version, unknown role, action, resource, or object key denies by default before resource disclosure or mutation.
- A non-matching `Own` record denies; it never broadens to `All`. A missing scope or resource key never implies a broader grant.
- Forged or caller-supplied record ownership denies; service-created records are owned by the authenticated service subject. Existing product mutation actor metadata migrates as `Human` with the same identifier and does not retain a parallel user-only path.
- Cross-Workspace lookup and policy evaluation return a non-disclosing not-found style outcome.
- A role assignment, service grant, service identity, or key revoked while a client holds stale state denies at the server immediately.
- Authorization, policy-resolution, persistence, or required-audit dependency failure fails closed as `Unavailable`, not as an authoritative policy denial. The API returns a stable non-sensitive `503`; the client exposes a retryable recovery state and does not retry a mutation as allowed without a fresh authoritative response.

## Acceptance Criteria

*Happy path*

- **AC-001** Server enforcement evaluates active human membership or active service grant through the shared Workspace-access policy before any policy-governed product data access; unknown subject kinds deny.
- **AC-002** An active assigned product role can perform only the exact installed runtime action, resource type, and nullable resource/object key for the current Workspace. A missing grant key matches only a keyless request and never acts as a wildcard. Product-definition authoring is not a registered product-policy action and uses the separate module-neutral Workspace Product Builder decision.
- **AC-003** Product action descriptors classify actions as non-record or record-scoped. Policy installation permits only `None` for non-record actions and only `Own`/`All` for record actions. Matching grants union deterministically: `All` dominates `Own`; otherwise an exact `Own` match permits only the evaluated subject's records, while no exact match denies.
- **AC-004** The reference policy permits product `Administrator` the exact published-definition read and list/read-all-record actions; permits `Applicant` the exact published-definition read and create/read/save/submit-own-record actions; and permits `Caseworker` the exact published-definition read and list/read-all-record actions but no record mutation. The exact runtime action/resource literals and record/non-record classifications are the closed registry in [Authorization architecture](../../architecture/authorization.md#product-action-descriptor-registry); no module, policy, REST/MCP caller, or client may substitute an alias, case variant, implicit wildcard, or broader key.

*Validation and recovery*

- **AC-005** Missing, unknown, stale, inactive, or non-matching policy, policy version, role, assignment, subject, action descriptor, resource, nullable object key, Workspace access, or record ownership denies by default before data disclosure or mutation; invalid scope/action combinations reject at policy installation.
- **AC-006** Missing resource/object/scope input never grants a broader action; an `Own` mismatch never falls back to `All`.
- **AC-007** Cross-Workspace policy-governed reads and mutations are non-disclosing, and an inactive/revoked human membership, service identity, service grant, signing key, or product-role assignment denies immediately despite an existing token or client cache.
- **AC-008** Server enforcement remains authoritative; forged, stale, missing, or permissive UI/client projection cannot make a denied product operation succeed.
- **AC-009** Required policy-governed allow and deny outcomes are correlated, append-only, redacted, and read back before return. Policy-resolution, persistence, or required-audit failure returns `Unavailable`, never proceeds as allowed, and remains distinguishable from an authoritative policy denial.

*Client and boundaries*

- **AC-010** Product clients present only server-reported action affordances and provide accessible forbidden, non-disclosing-not-found, unavailable, and retryable-recovery states without revealing policy internals or cross-Workspace resources.
- **AC-011** Policies are immutable versioned Solution components installed only through the Solutions adapter; no policy-authoring API or UI exists in this slice.
- **AC-012** `Own` record ownership is a product-module-owned discriminated `Human`/`Service` subject reference stamped from authenticated server context. Business Objects and Rules preserve existing human actor identifiers as `Human`; public REST/OpenAPI/MCP/generated-client projections expose only `SubjectReferenceDto` actor fields, with no aliases or parallel path.
- **AC-013** Record collections filter `Own` in the owning module query/store before materialization; get/save/submit enforce owner in that module, creation ignores caller-supplied owner input, and a service subject owns a record it creates.
- **AC-014** A service subject is denied by every current product endpoint that composes only baseline WorkspaceAccess, including when it has no product-role assignment; it is admitted only when that endpoint/application composes an exact Authorization product action. Human baseline behavior is unchanged.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Application boundary | Exact policy/version/role/action/resource matching permits only explicitly granted current-Workspace runtime operations; omitted resource key matches only keyless requests; unknown descriptors and invalid `None`/`Own`/`All` combinations reject installation | AC-001, AC-002, AC-005, AC-006 | Application test | Yes |
| AT-002 | API boundary | Multi-role and multi-policy record grants resolve `All` over `Own`; otherwise `Own` filters collections and permits only the subject's record without broadening | AC-003, AC-006 | Application test + API integration test | Yes |
| AT-003 | API boundary | Reference Administrator, Applicant, and Caseworker outcomes prove their exact runtime operations and Caseworker mutation denial, while authoring remains independent of all three product roles | AC-004 | API integration test | Yes |
| AT-004 | API/Application boundaries | Revoked membership, service grant/key/identity, assignment, stale policy, and cross-Workspace requests deny immediately and non-disclosingly | AC-005, AC-007 | API integration test + Application test | Yes |
| AT-005 | API boundary | Forged or stale client affordances cannot bypass server enforcement; authoritative denial is distinct from retryable `503` unavailability; client exposes accessible denial/not-found/unavailable recovery without policy disclosure | AC-008, AC-010 | API integration test + UI component test | Yes |
| AT-006 | Infrastructure boundary | Policy-governed allows and denies have correlated redacted audit read-back; policy-resolution and audit-persistence failures return `Unavailable` and fail closed without being reported as an authoritative denial | AC-009 | Infrastructure integration test | Yes |
| AT-007 | Application boundary | Only the Solutions adapter installs immutable versioned policies; policy-authoring operations and UI are absent | AC-011 | Application test + API integration test | Yes |
| AT-008 | Application/Infrastructure boundaries | The clean discriminated-subject persistence and wire migration preserves existing actor IDs as `Human`, emits only the new `SubjectReferenceDto` fields through REST/OpenAPI/MCP/generated clients, retains a `Service` owner for service-created records, and rejects forged owner input | AC-012, AC-013 | Application test + Infrastructure integration test + API integration test + MCP contract test | Yes |
| AT-009 | API/Application boundaries | `Own` collection filtering occurs before materialization and get/save/submit deny foreign owner access; all current product endpoints deny an unassigned service subject when only baseline WorkspaceAccess is composed, while an exact assigned action admits it without changing human baseline access | AC-003, AC-005, AC-013, AC-014 | API integration test + Application test | Yes |

## Out Of Scope

- Authoring, editing, or browsing policy source; policies arrive only through the Solutions adapter.
- Global product roles, wildcard/inherited permissions, dynamic expressions, delegated administration, Group/Team mapping, IdP/SCIM mapping, and client-side authorization enforcement.
- Managing Identity lifecycle, service identity/key lifecycle, and product-role assignment lifecycle.

## Screen flow

| Surface | Required contract |
|---|---|
| Product collection/detail/action | Render only server-reported current-Workspace data and action affordances. A keyless collection projection may expose only `canStartCreate`; the keyed POST and every detail action re-evaluate exact authority rather than trusting cached permission state. |
| Forbidden or unavailable result | Explain that the action cannot proceed without exposing policy internals, foreign-resource existence, credentials, or cross-Workspace identifiers; offer an appropriate safe return or retry. |

> **Implementation status**
>
> | Layer | Status |
> |---|---|
> | Authorization Domain | Done |
> | Authorization Application | Done |
> | Authorization Infrastructure | Done |
> | Audit | Done |
> | Solutions adapter | Done |
> | Product module integration | Done |
> | API | Done |
> | Frontend | Done |
>
> **Gaps vs spec:** None.
>
> **Deferred follow-ups:** Only the separately owned capabilities under Out Of Scope are deferred.
>
> **Verification:** [Access Product By Policy evidence](./access-product-by-policy.evidence.md) records the passing exact-role API matrix, durable audit and failure semantics, server-reported BO/Rules affordances, safe `403`/`404`/`503` client states, service boundary, generated REST/MCP contracts, full repository verification, and browser recovery journeys.
>
> **Decisions:** The server evaluates exact immutable product-policy runtime grants after shared Workspace access. `Own` and `All` are distinct outcomes; product authoring uses Workspace Product Builder; `Unavailable` remains distinct from `Denied`; client projection is convenience only and policies are installed through Solutions.
