# Authorization Architecture

> **Navigation**: [docs/ARCHITECTURE.md](../ARCHITECTURE.md) · [docs/use-cases/authorization/README.md](../use-cases/authorization/README.md) · [docs/architecture/identity-governance.md](./identity-governance.md) · [AGENTS.md](../../AGENTS.md)

This file owns the Authorization module's durable policy, assignment, and enforcement realization. Use cases own administrator and consumer outcomes; [docs/TECH_STACK.md](../TECH_STACK.md) owns approved technologies.

## Module boundary

- Authorization is a modular-monolith bounded context. It owns immutable versioned product policies, product-role assignments, policy evaluation, and their audit outcomes. Identity owns subject authentication, Workspace lifecycle, active human membership, and active service grants; Solutions owns the orchestration that invokes the Authorization policy-component adapter.
- `Axis.Identity.Contracts` exposes the stable human/service subject reference and active-authority read contract. `Axis.Authorization.Contracts` exposes typed policy-component installation and action-decision contracts. Product modules map that subject reference into their own persisted actor/owner representation and never reference Identity or Authorization internals.
- A policy is a versioned Solution component identified by semantic policy, role, action, and resource keys. A product role is a policy-defined semantic key, not an Identity lifecycle role. `Administrator`, `Applicant`, and `Caseworker` are roles in the reference solution only and have no platform-wide lifecycle meaning.
- Policies are installed only through the Solutions adapter. Authorization exposes no policy-authoring operation or UI and no mutable in-place policy behavior. A policy version remains immutable once installed; a changed product policy is a new versioned Solution component.
- An active Workspace `Administrator` may create or revoke one or more exact product-role assignments for an active human or service subject in that Workspace. Assignments never create membership, service grants, Identity authority, or cross-Workspace access. Revocation removes the assignment authority immediately.

## Evaluation and enforcement

- Server enforcement is authoritative. A client or UI may project an allowed action for convenience but cannot grant, preserve, or infer authority. Every protected product operation resolves authenticated subject, active Workspace access, installed policy versions, exact action/resource keys, and active matching assignments before business data access.
- The decision model is deny by default. Missing, unknown, stale, inactive, cross-Workspace, or otherwise non-matching subject, policy, role, action, resource, grant, or assignment denies. Unknown subject kinds deny. Cross-Workspace access remains non-disclosing.
- Policy grants use exact semantic keys. A grant may cover an exact action and optional exact resource/object key. Record access distinguishes `Own` from `All`: `Own` permits only records whose ownership resolves to the evaluated subject in the current Workspace; `All` permits every matching record in that Workspace. The result is determined before disclosure or mutation, and no broader scope is inferred from a missing key.
- An authorization decision is itself the audited outcome. Authorization commits and reads back its required redacted decision outbox before returning allow or deny; an allow records authority evaluation, not downstream business success. Product modules perform their own operation only after an allowed decision and retain ownership of business mutation and audit semantics.
- Wave 1 reference policy proves these distinct outcomes: product `Administrator` may manage definitions and read all records; `Applicant` may create, read, save, and submit own records; `Caseworker` may list and read all records and may not mutate records. Lifecycle administrator bootstrap installs Solutions and assigns initial product roles; it does not make lifecycle roles product roles.

## CQRS, persistence, and consistency

- Authorization Application exposes commands for assignment lifecycle and policy installation through the Solutions adapter, plus side-effect-free queries and deterministic authorization decisions. The API composes those contracts; consumers reference public Authorization contracts only, never module internals.
- Authorization persists module-owned policy versions, assignment lifecycle, revisions, and idempotency/audit-delivery state through reviewable migrations. Identity and product modules retain their own data; Authorization uses external subject, Workspace, and resource identifiers without cross-module entity ownership or hidden distributed writes.
- A policy component uses fixed-schema `authorization.policy.v1` JSON with one semantic policy key, exact roles, and exact grants. Each grant identifies one role key, action key, resource type, optional exact resource key, and `None`, `Own`, or `All` scope. Wildcards, expressions, inheritance, and implicit grants are invalid. Evaluation unions only exact grants from active assignments across policies installed in the current Workspace, including installed content classified `Noncompliant` by a later publisher revocation.
- Assignment mutations use optimistic concurrency. Idempotent retries return the canonical committed assignment outcome; changed-content reuse conflicts. A role, subject, Workspace, and policy-version race cannot create duplicate active assignment authority or silently overwrite a revocation.
- Policy installation validates complete semantic identities and rejects duplicate or incompatible components before a version becomes active. A policy-version install is atomic with its required audit state. Event sourcing, policy inference, policy fallback, and cross-module distributed transactions are not introduced.

## Audit and threat model

| Area | Contract |
|---|---|
| Assets | Workspace isolation, policy integrity, assignment authority, action/resource scope, and audit integrity. |
| Entry points | Solution policy installation, product-role assignment and revocation, and every policy-governed product operation. |
| Trust boundaries | Authenticated caller to API, API to Authorization, Authorization to its store and Audit contract, and Solutions adapter to Authorization. |
| Abuse cases | Lifecycle-role conflation, forged UI projection, stale grant or assignment, unknown subject kind, cross-Workspace probing, `Own`/`All` confusion, policy substitution, duplicate assignment, and audit omission. |
| Mitigations | Server-side exact-key evaluation, active Workspace-access recheck, immutable policy versions, optimistic concurrency, non-disclosing denial, deny-by-default resolution, idempotent audit delivery, and redaction validation. |
| Evidence | Owning AT rows prove positive and negative role outcomes, policy/version/assignment staleness, `Own` versus `All`, cross-Workspace non-disclosure, concurrent lifecycle changes, audit read-back, and client projection cannot bypass server enforcement. |

## Explicit exclusions

- Authorization does not author policies, define a global role catalog, manage Identity lifecycle roles, create Workspaces, or replace Identity authentication and Workspace access.
- Dynamic policy expressions, wildcard action/resource grants, permission inheritance, Group or Team semantics, IdP/SCIM mapping, bulk assignment, delegated administration, event sourcing, and client-side authorization as an enforcement boundary require separate contracts.
