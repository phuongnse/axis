# Architecture

> **Navigation**: [docs/README.md](./README.md) · [AGENTS.md](../AGENTS.md)

This file owns durable source and runtime boundaries. Current behavior lives in [docs/use-cases/README.md](./use-cases/README.md); stack choices live in [docs/TECH_STACK.md](./TECH_STACK.md).

## Boundary Rules

- `frontend/` calls `Axis.Api` only.
- `Axis.Api` is the REST/OpenAPI gateway and composes module infrastructure at startup.
- `Axis.Mcp` is a host-side stdio adapter with one typed semantic tool per exposed authenticated OpenAPI operation. It calls `Axis.Api` over authenticated loopback HTTPS and must not reference module projects, MediatR, EF Core, DbContext, database storage, or arbitrary request paths.
- `Axis.Mcp` stdout is reserved for MCP JSON-RPC; diagnostics belong on stderr. It is local tooling, not a Compose service or a second HTTP gateway.
- Modules expose Application contracts to `Axis.Api`; module internals stay inside the module.
- Modules may expose optional `Axis.{Module}.Contracts` projects for stable cross-module contracts; consumers may reference another module's Contracts project only, not its Domain, Application, or Infrastructure projects.
- Module Domain models follow DDD tactical boundaries; aggregate roots own invariants and domain events.
- Module Application exposes CQRS commands and side-effect-free queries through handlers.
- Server-owned search is a CQRS read-side pattern: module Application owns searchable intent and provider ports, Infrastructure owns read-store translation and indexes, and shared code may contain storage-neutral primitives only.
- Cross-module global search, when required by an owning contract, uses one materialized read model built from published module contracts rather than querying module internals or merging provider-native scores.
- Domain projects have zero external dependencies.
- `Axis.Shared.*` is for shared primitives and cross-cutting helpers only, not product behavior.
- Module-owned data changes use EF Core migrations.
- Event sourcing is opt-in and requires an approved event store, replay, projection, and versioning design before source changes.
- New product behavior starts in an owning use-case spec before source changes.
- Development and test composition may replace infrastructure adapters, addresses, credentials, certificates, and data only; it preserves production authentication, authorization, isolation, TLS validation, migration, concurrency, failure, and recovery semantics.
- Every runtime boundary derives its deployment configuration, secret ownership, observable failure behavior, and recovery evidence from an owning production-grade contract; local-only behavior cannot become an application dependency.

## Dependency Direction

```text
frontend
  -> Axis.Api
    -> Module.Contracts
    -> Module.Application
      -> Module.Domain
    -> Module.Infrastructure
      -> Module.Application
      -> Module.Domain

Module.Application
  -> Same Module.Domain
  -> Other Module.Contracts only when a use case needs a public cross-module contract

Axis.Shared.* supports layers without owning product behavior.
```

## Rules boundary

Rules owns reusable definitions, bindings, and pure evaluation:

- A Rule is a versioned, deterministic, side-effect-free unit of logic that transforms declared typed inputs into declared typed outputs. `Inputs -> Logic -> Outputs` is the stable platform contract; validation, calculation, classification, eligibility, routing, and transformation are consumer meanings rather than separate Rule models.
- `RuleDefinition` owns metadata, stable typed input and output contracts, canonical bounded logic, validation, immutable versions, and activation. Built-in and workspace definitions use one semantic model; source and language capabilities are metadata.
- `RuleBinding` connects one exact rule version to a generic target type/id and use case/trigger. It owns typed input mappings, priority, enabled state, failure behavior, concurrency, audit data, and usage discovery.
- `RuleEvaluationResult` is deterministic, typed, explainable, bounded, and side-effect-free. Consumers interpret outputs and own every business mutation.

Rules persists definitions, immutable definition versions, and bindings in the Rules store. A binding keeps opaque consumer target identifiers rather than foreign keys or consumer entities; deleting or changing a binding never changes or deletes its definition. Canonical logic is the only persisted rule behavior. Syntax, visual composition, decision tables, and localized explanations are projections over that contract and never separate stored truths.

`Axis.Rules.Contracts` exposes only consumer-neutral definition, binding, typed context-provider, and evaluation contracts. A consumer implements its adapter and explicitly maps runtime context, target data, or fixed values into declared rule inputs. Execution-envelope data used only for isolation, authorization, audit, or tracing is not an ambient logic input. Rules projects may not reference Object, Workflow, another consumer module, or consumer domain types; consumers may reference Rules Contracts only.

`Axis.Api` composes consumer adapters and exposes REST/OpenAPI surfaces. Cross-database Object and Rules mutations remain explicit operations; no hidden dual write or distributed transaction is introduced.

## Identity, authorization, and audit boundaries

- `Organization` is the enterprise governance container. `Workspace` is the active data and isolation context; a personal Workspace has no Organization, while an organization Workspace belongs to exactly one Organization.
- `OrganizationMembership` governs organization lifecycle with `Owner`, `Administrator`, and `Member`. `WorkspaceMembership` is the single access relationship for personal and organization Workspaces: organization Workspaces use `Administrator` or `Member`, while a personal Workspace has one `Owner`. Organization membership never implies Workspace access. A Workspace invitation may establish only the baseline Organization `Member` prerequisite when absent/removed; it never selects, grants, or promotes `Owner` or `Administrator`.
- Existing personal owners migrate to one active owner membership, after which `OwnerUserId` and `OwnerEmail` are not authorization inputs. Personal Workspaces admit no additional memberships or invitations.
- Identity lifecycle roles cover owner, administrator, and member authority only. Product roles and action/resource decisions belong to versioned Authorization policies; Team is reserved for collaboration/assignment and Group for IdP or authorization grouping.
- A selected `workspace_id` is context, not authority. Cookie and bearer requests validate active Workspace membership before module data access. Browser context changes rotate the opaque session and antiforgery state; clients clear all workspace-bound cache and managed state only after server confirmation.
- Identity persists security mutation and audit-outbox state in one module transaction. Required denied or replayed outcomes without a business mutation use their own fail-closed Identity audit-outbox transaction. Audit consumes versioned events idempotently into an append-only store; raw tokens, credentials, secrets, delivery envelopes, handoff identifiers, and sensitive payloads never enter audit records.
- Session context transitions span Identity persistence, Redis tickets, and HTTP without a distributed transaction. A durable pending transition makes source and staged-target tickets recovery-only; browser confirmation with the target ticket atomically completes the transition and audit state, after which source revocation is idempotent. Lost responses, expiry, and concurrent recovery reconcile through optimistic terminal states; no pending or stale source correlation authorizes workspace data.
- Email invitation delivery is a transactional outbox concern. Validation stores only a token hash; the delivery record may hold the sole authenticated-encrypted, access-controlled, expiring envelope required for crash-safe retry, and deletes it after accepted delivery or expiry. Ambiguous delivery retries the same token generation; explicit resend is the only operation that supersedes it.
- Outbox delivery is durable asynchronous integration, not event sourcing. Module data remains authoritative; Audit records explain actions and outcomes without becoming a replay source for Identity state.
- `Organization`, `Workspace`, `OrganizationMembership`, `WorkspaceMembership`, `WorkspaceInvitation`, and `WorkspaceContextTransition` are separate Identity aggregates. Unique membership/invitation keys, aggregate revisions, and one Identity unit of work own concurrency; application handlers coordinate multi-aggregate transactions without a generic saga or shared-domain abstraction.
- `Axis.Audit.Contracts` owns the versioned redacted ingestion envelope. Identity stores that envelope in an outbox with durable `Pending`, `Delivered`, or `Poisoned` delivery state; bounded batches and capped exponential backoff never discard an event, and one poisoned ID never blocks unrelated IDs. Unsupported or invalid envelopes retain a non-sensitive reason/attempt history as `Poisoned`; a compatible code or reviewed data-migration deployment requeues matching IDs, and idempotent Audit read-back confirms delivery. A dedicated audit-ingestion health check and telemetry report poison immediately and overdue pending age against a required configured threshold without failing the API host's global readiness. Audit persists one immutable record per event ID in its own database; neither module references the other's internals.
- Workspace-scoped API groups require one asynchronous subject-neutral workspace-access policy after authentication and before module handlers. The current human-subject arm requires active `WorkspaceMembership`; a later service-identity arm supplies its active workspace grant through the same policy and cannot replace or bypass it. Unknown subject kinds deny. Explicit account, eligible-workspace, transition recovery, token exchange, and OAuth bootstrap endpoints are the only allowlisted exceptions; frontend visibility and claims never replace the server policy.
- Browser tickets carry non-secret session correlation used only for transition enforcement and Redis revocation lookup. Pre-governance browser tickets are invalidated at the clean deployment cutover; bearer issue/refresh continues only after the same subject-neutral workspace-access decision, whose current human arm validates active membership.
- The Identity migration transaction preflights every personal owner, adds the nullable Workspace-to-Organization relationship and new Organization membership structures, preserves `OrganizationId = null` for every migrated Personal Workspace, creates and backfills exactly one active owner Workspace membership, then drops `OwnerUserId`, `OwnerEmail`, and their authorization index. Production rollout quiesces Identity writes for this incompatible cutover; migration failure rolls back, while rollback after success requires a forward fix or reviewed database restore rather than a dual-schema application path.

## Ownership

- Use-case docs own behavior, flows, acceptance criteria, and implementation status.
- Module code owns business rules and persistence details.
- [docs/TECH_STACK.md](./TECH_STACK.md) owns approved runtime and library categories.
- [docs/ENFORCEMENT.md](./ENFORCEMENT.md) owns recurring architecture enforcement status.
