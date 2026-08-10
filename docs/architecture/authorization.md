# Authorization Architecture

> **Navigation**: [docs/ARCHITECTURE.md](../ARCHITECTURE.md) · [docs/use-cases/authorization/README.md](../use-cases/authorization/README.md) · [docs/architecture/identity-governance.md](./identity-governance.md) · [AGENTS.md](../../AGENTS.md)

This file owns the Authorization module's durable policy, assignment, and enforcement realization. Use cases own administrator and consumer outcomes; [docs/TECH_STACK.md](../TECH_STACK.md) owns approved technologies.

## Module boundary

- Authorization is a modular-monolith bounded context. It owns immutable versioned product policies, product-role assignments, policy evaluation, and their audit outcomes. Identity owns subject authentication, Workspace lifecycle, active human membership, and active service grants; Solutions owns the orchestration that invokes the Authorization policy-component adapter.
- `Axis.Identity.Contracts` exposes `SubjectReference` as discriminated `Human` or `Service` plus a `Guid`, its wire projection `SubjectReferenceDto { kind: "Human"|"Service", subjectId: Guid }`, and the active-authority read contract. `Axis.Authorization.Contracts` exposes typed policy-component installation and action-decision contracts. Product modules map that subject reference into their own persisted actor/owner representation and never reference Identity or Authorization internals.
- A policy is a versioned Solution component identified by semantic policy, role, action, and resource keys. A product role is a policy-defined semantic key, not an Identity lifecycle role. `Administrator`, `Applicant`, and `Caseworker` are roles in the reference solution only and have no platform-wide lifecycle meaning.
- Policies are installed only through the Solutions adapter. Authorization exposes no policy-authoring operation or UI and no mutable in-place policy behavior. A policy version remains immutable once installed; a changed product policy is a new versioned Solution component.
- An active Workspace lifecycle administrator, resolved under [Identity Governance](./identity-governance.md#model-invariants), may create or revoke one or more exact product-role assignments for an active human or service subject in that Workspace. Assignments never create membership, service grants, Identity authority, or cross-Workspace access. Revocation removes the assignment authority immediately.

## Evaluation and enforcement

- Server enforcement is authoritative. A client or UI may project an allowed action for convenience but cannot grant, preserve, or infer authority. Every protected product operation resolves authenticated subject, active Workspace access, installed policy versions, exact action/resource keys, and active matching assignments before business data access.
- The decision model is fail closed with three explicit outcomes: `Allowed`, `Denied`, or `Unavailable`. Missing, unknown, stale, inactive, cross-Workspace, or otherwise non-matching subject, policy, role, action, resource, grant, or assignment produces the authoritative `Denied` outcome. Unknown subject kinds deny. A policy-resolution, persistence, or required-audit dependency failure produces `Unavailable`; it never aliases `Denied` or permits product work. Cross-Workspace access remains non-disclosing.
- Policy grants use exact semantic keys. `resourceType` and nullable `resourceKey` compare exactly; an omitted grant key matches only a request whose resource key is also absent and is never a wildcard. Each product Contracts boundary registers its fixed action descriptors and whether each action is non-record or record-scoped. Policy installation rejects unknown descriptors, `None` on a record-scoped action, and `Own`/`All` on a non-record action. For one exact request, active matching grants union deterministically: a non-record action yields `None`; a record action yields `All` when any grant is `All`, otherwise `Own` when any grant is `Own`, otherwise deny. Record access then distinguishes `Own` from `All`: `Own` permits only records whose persisted discriminated owner resolves to the evaluated subject in the current Workspace; `All` permits every matching record in that Workspace. Record collection filtering happens in the owning module query/store before materialization, while get/save/submit enforce the owner inside the module boundary and never accept a caller-provided owner. The result is determined before disclosure or mutation, and no broader scope is inferred from a missing key.
- An authorization decision is itself the audited outcome. Authorization commits and reads back its required redacted decision outbox before returning `Allowed` or `Denied`; an allow records authority evaluation, not downstream business success. Failure to persist that mandatory audit state returns `Unavailable`. Product modules perform their own operation only after an `Allowed` decision and retain ownership of business mutation and audit semantics. REST maps an authoritative operation denial to `403`, a capability denial to `200` with `false`, and `Unavailable` to a stable non-sensitive `503` recovery response.
- The reference policy proves these distinct outcomes: product `Administrator` may perform the exact Business Object definition read/manage actions, exact Rules definition/binding manage actions, and read-all-records action; `Applicant` may perform the exact published-definition read action and create/read/save/submit-own-record actions; `Caseworker` may perform the exact published-definition read and list/read-all-records actions but no record-mutation action. Lifecycle administrator bootstrap installs Solutions and assigns initial product roles; it does not make lifecycle roles product roles.

### Product action descriptor registry

The following literals are the complete product-action registry for the implemented product modules. They are ordinal, case-sensitive contract values; modules, policy components, REST/OpenAPI, MCP, and clients do not define aliases.

| Action key | Resource type | Kind |
|---|---|---|
| `business-object.definition.read` | `business-object.definition` | Non-record |
| `business-object.definition.read-published` | `business-object.definition` | Non-record |
| `business-object.definition.manage` | `business-object.definition` | Non-record |
| `business-object.record.create` | `business-object.record` | Record |
| `business-object.record.list` | `business-object.record` | Record |
| `business-object.record.read` | `business-object.record` | Record |
| `business-object.record.save` | `business-object.record` | Record |
| `business-object.record.submit` | `business-object.record` | Record |
| `rule.definition.read` | `rule.definition` | Non-record |
| `rule.definition.manage` | `rule.definition` | Non-record |
| `rule.binding.read` | `rule.binding` | Non-record |
| `rule.binding.manage` | `rule.binding` | Non-record |

For Business Object actions, `resourceKey` is the exact `objectKey` when an operation targets one definition or its records; a genuinely collection-wide request uses `null` and therefore requires a separate keyless grant. For Rule definition actions it is the exact Rule Definition Key when one is known, otherwise `null` for an explicitly collection-wide operation. For `rule.binding.read` and `rule.binding.manage`, it is the exact Rule Definition Key of the definition/version family being bound. A binding UUID is only a REST locator, and the longer signed-package binding `componentKey` is only a Solutions artifact identity; neither is an Authorization resource key. Create authorizes the validated requested definition key, get/delete authorize the persisted binding's definition key after a non-disclosing current-Workspace lookup, and a retargeting update authorizes both the persisted and requested definition keys. A keyless `business-object.definition.manage` or `rule.definition.manage` grant authorizes only its module's collection `canStartCreate` capability projection. It never authorizes a create or other keyed mutation: create derives the semantic key from validated input and re-evaluates the exact keyed action before persistence. A missing key never matches a keyed grant or acts as a wildcard. `create` with `Own` means that the module stamps the authenticated subject as owner before persistence; `list` with `Own` means the repository filters by that persisted owner before materialization.

The reference policy grants product `Administrator` definition read/manage, Rules definition/binding read/manage, and record list/read with `All`; grants `Applicant` published-definition read plus record create/read/save/submit with `Own`; and grants `Caseworker` published-definition read plus record list/read with `All`. It grants no other action implicitly.

## CQRS, persistence, and consistency

- Authorization Application exposes commands for assignment lifecycle and policy installation through the Solutions adapter, plus side-effect-free queries and deterministic authorization decisions. The API composes those contracts; consumers reference public Authorization contracts only, never module internals.
- Authorization persists module-owned policy versions, assignment lifecycle, revisions, and idempotency/audit-delivery state through reviewable migrations. Identity and product modules retain their own data; Authorization uses external subject, Workspace, and resource identifiers without cross-module entity ownership or hidden distributed writes.
- Product modules persist a discriminated subject reference for all product mutation actor metadata. The migration converts existing human actor identifiers to `Human` with the same `Guid`; no existing identifier changes. Business Objects additionally persists a discriminated record owner, stamps it from the authenticated subject server-side at creation, and uses that owner only for `Own` record decisions. This is a clean migration, not a dual user/service metadata path.
- The public wire contract exposes `publishedBySubject` for Business Object and Rule versions, plus `createdBySubject`, `updatedBySubject`, and nullable `submittedBySubject` for Business Object records. Workspace-authored Rule versions require a `SubjectReferenceDto`; code-owned built-in versions expose `publishedBySubject: null` because no Human or Service subject performed that publication. REST/OpenAPI, generated frontend types, MCP response projections, tests, and external product consumers use only this subject-aware contract; aliases, fallback parsing, and dual response shapes do not exist.
- A policy component uses fixed-schema `authorization.policy.v1` JSON with one semantic policy key, exact roles, and exact grants. Every role entry contains a product-owned localized presentation map from BCP 47 language tag to NFC-normalized, bounded `displayName` and optional bounded `description`, with at least `en`. The server projects the exact current UI-language entry when present and otherwise falls back to `en`; no platform role catalog or hardcoded role copy exists. Each grant identifies one role key, action key, resource type, optional exact resource key, and `None`, `Own`, or `All` scope. Wildcards, expressions, inheritance, and implicit grants are invalid. Evaluation unions only exact grants from active assignments across policies installed in the current Workspace, including installed content classified `Noncompliant` by a later publisher revocation.
- Assignment mutations use optimistic concurrency. Idempotent retries return the canonical committed assignment outcome; changed-content reuse conflicts. A role, subject, Workspace, and policy-version race cannot create duplicate active assignment authority or silently overwrite a revocation.
- Policy installation validates complete semantic identities and rejects duplicate or incompatible components before a version becomes active. A policy-version install is atomic with its required audit state. Event sourcing, policy inference, policy fallback, and cross-module distributed transactions are not introduced.

## Audit and threat model

| Area | Contract |
|---|---|
| Assets | Workspace isolation, policy integrity, assignment authority, action/resource scope, and audit integrity. |
| Entry points | Solution policy installation, product-role assignment and revocation, and every policy-governed product operation. |
| Trust boundaries | Authenticated caller to API, API to Authorization, Authorization to its store and Audit contract, and Solutions adapter to Authorization. |
| Abuse cases | Lifecycle-role conflation, forged UI projection, stale grant or assignment, unknown subject kind, cross-Workspace probing, `Own`/`All` confusion, policy substitution, duplicate assignment, and audit omission. |
| Mitigations | Server-side exact-key evaluation, active role-contextual lifecycle-administrator and Workspace-access rechecks, immutable policy versions, optimistic concurrency, non-disclosing denial, deny-by-default resolution, idempotent audit delivery, and redaction validation. |
| Evidence | Owning AT rows prove personal-Owner and organization-Administrator lifecycle authority, organization-Member denial, positive and negative product-role outcomes, policy/version/assignment staleness, `Own` versus `All`, cross-Workspace non-disclosure, concurrent lifecycle changes, audit read-back, and client projection cannot bypass server enforcement. |

## Explicit exclusions

- Authorization does not author policies, define a global role catalog, manage Identity lifecycle roles, create Workspaces, or replace Identity authentication and Workspace access.
- Dynamic policy expressions, wildcard action/resource grants, permission inheritance, Group or Team semantics, IdP/SCIM mapping, bulk assignment, delegated administration, event sourcing, and client-side authorization as an enforcement boundary require separate contracts.
