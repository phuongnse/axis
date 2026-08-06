# Identity Governance Architecture

> **Navigation**: [docs/ARCHITECTURE.md](../ARCHITECTURE.md) · [docs/use-cases/identity-governance/README.md](../use-cases/identity-governance/README.md) · [docs/PLATFORM_STRATEGY.md](../PLATFORM_STRATEGY.md) · [AGENTS.md](../../AGENTS.md)

This file owns durable Identity Governance invariants and technical realization. Use cases own actor goals and observable behavior; [docs/TECH_STACK.md](../TECH_STACK.md) owns approved technologies.

## Model invariants

- `Organization` is the enterprise governance container. `Workspace` is the active data and isolation context; a personal Workspace has no Organization, while an organization Workspace belongs to exactly one Organization.
- `OrganizationMembership` governs organization lifecycle with `Owner`, `Administrator`, and `Member`. `WorkspaceMembership` is the sole Workspace access relationship. Organization membership never implies Workspace access.
- Organization Workspaces use `Administrator` or `Member` Workspace roles. A personal Workspace admits exactly one active `Owner` membership and no invitation or additional membership.
- A Workspace invitation may establish only a missing or removed baseline Organization `Member`; it never grants or promotes Organization `Owner` or `Administrator`.
- Identity lifecycle roles do not encode product roles. Versioned Authorization policies own product action and resource decisions; Team remains collaboration/assignment vocabulary and Group remains IdP or authorization-group vocabulary.
- `Organization`, `Workspace`, `OrganizationMembership`, `WorkspaceMembership`, `WorkspaceInvitation`, and `WorkspaceContextTransition` are separate aggregates. Unique membership/invitation keys, aggregate revisions, and one Identity unit of work own concurrency.

## Subject authority and isolation

- A selected `workspace_id` is context, not authority. Every cookie- or bearer-authenticated Workspace operation passes one asynchronous subject-neutral Workspace-access policy after authentication and before module data access.
- The human-subject arm requires an active `WorkspaceMembership`. A later service-identity arm supplies its active Workspace grant through the same policy and cannot replace or bypass it. Unknown subject kinds deny.
- Account, eligible-Workspace, transition recovery, token exchange, and OAuth bootstrap operations are the only explicit exceptions to Workspace-scoped enforcement. Claims, frontend visibility, and Organization membership never substitute for the policy.
- Cross-Workspace resource lookup returns a non-disclosing not-found outcome. A known forbidden Identity lifecycle action returns permission denied.

## Organization creation realization

- Organization creation atomically persists the Organization, owner Organization membership, initial organization Workspace, administrator Workspace membership, scoped idempotency record, and redacted audit-outbox event in one Identity transaction.
- The idempotency key is scoped to the authenticated subject and canonical request. An identical retry reads the committed result back; changed content conflicts; concurrent attempts commit at most one complete graph.
- Persistence or audit-outbox failure rolls back the complete graph. Success is reported only after persisted Organization, Workspace, memberships, and outbox state can be read back.

## Browser context-transition realization

- Browser switching is a two-request protocol across Identity persistence, Redis tickets, and HTTP; no distributed transaction is implied.
- Beginning a switch durably creates a `Pending` transition with source and target session correlations, expiry, and requested audit state. Source and staged-target tickets are recovery-only while pending and cannot authorize Workspace data.
- The target ticket is opaque and bound to the transition. Antiforgery-protected browser confirmation revalidates membership and correlation, then atomically records `Completed` plus terminal audit state. PostgreSQL completion is the authority commit point.
- Completion rotates and revokes the source session. The separately authenticated opaque transition ticket remains recovery-only until transition expiry so a lost completion response can re-establish the canonical target; idempotent cleanup removes the source immediately and the transition ticket after its recovery window. Cleanup failure cannot restore source authority because every request checks transition state before Workspace data access.
- Lost staging response is recovered by explicit or expiry-driven compensation. Lost completion response is recovered by reading the completed transition through the target ticket. Confirm/recover concurrency permits one terminal result.
- Terminal states are `Completed`, `Compensated`, or `Failed`; optimistic concurrency protects every terminal change. Terminal records retain no ticket secret and purge only after both absolute session lifetimes, confirmed audit projection, and Redis cleanup.
- After confirmed completion, the client closes Workspace-bound managed windows, clears Workspace-scoped state, refreshes identity and antiforgery state, and opens a safe target route before rendering target data.

## Audit delivery realization

- Identity persists every required security mutation and its versioned redacted audit envelope in one module transaction. Required denied or replayed outcomes without a business mutation use their own fail-closed Identity outbox transaction.
- Identity outbox records use durable `Pending`, `Delivered`, or `Poisoned` state. Delivery uses bounded batches and capped exponential backoff; one poisoned event never blocks unrelated IDs and no event is discarded.
- Audit ingests idempotently by event ID into its separate append-only store. Identity marks delivery only after Audit read-back confirms the matching immutable record.
- Human and service-identity events always require a resolved Workspace scope. Anonymous or system events that occur before a Workspace can be resolved may use a null Workspace scope with a generic attempt target; once an operation resolves a governed resource, its audit remains Workspace-scoped.
- Unsupported or invalid envelopes retain bounded non-sensitive attempt history as `Poisoned`; a compatible deployment or reviewed data migration may requeue named IDs.
- Dedicated audit-ingestion health and telemetry report poison immediately and overdue pending age against a required configured threshold without failing global API readiness.
- Audit contains stable identifiers, categorical action/outcome, timestamp, correlation identity, and bounded non-sensitive metadata only. Tokens, credentials, secrets, delivery envelopes, handoff identifiers, and sensitive payloads are forbidden. Audit is not a business-state replay source or event-sourcing mechanism.
- The current audit policy retains the approved immutable records indefinitely and exposes no product update or delete operation. Any future expiry, purge, mutation, or retention change requires a new owning contract and a reviewable migration.

## Invitation delivery realization

- Invitation creation is scoped to the authenticated Organization and Workspace authority, rate-limited and idempotent. A filtered database uniqueness constraint permits at most one pending invitation for each `(Workspace, normalized recipient email)`. An equivalent role request returns that canonical invitation; a different-role request conflicts without replacing it or creating another valid token.
- Validation stores only a token hash. Transactional delivery state may hold the sole authenticated-encrypted, access-controlled, expiring envelope needed for crash-safe email retry.
- Delivery uses one stable correlation key per token generation. Accepted provider delivery deletes the envelope; an ambiguous outcome retries the same generation. Retry exhaustion also deletes the envelope because no further automatic delivery may use its recoverable token material. Only explicit authorized resend supersedes the generation and invalidates its token and handoff.
- Invitation creation, resend, and pending revocation persist their lifecycle change, delivery state when applicable, and required audit outbox in one Identity transaction. Optimistic concurrency permits one terminal outcome when revocation races exchange or acceptance.

## Invitation exchange and acceptance realization

- The product-owned link carries the email token in the URL fragment. Before routing, telemetry, or third-party content, the client removes the fragment with history replacement and posts the token from memory under `Referrer-Policy: no-referrer`; client and server logging redact it.
- Exchange consumes the email token and creates a short-lived browser-bound handoff. Its opaque cookie is HttpOnly, Secure, and SameSite; only a hash of its server-side identifier is stored. Token or handoff material never enters local storage, session storage, IndexedDB, analytics, or logs.
- Valid-looking unknown-token and rate-limited exchange attempts persist a platform-scoped, metadata-free anonymous audit event before returning their generic failure. Token values, hashes, request partitions, network identifiers, email data, and handoff data are never written to that event; inability to confirm its outbox record fails closed.
- The handoff preserves invitation intent through sign-in or standalone registration and email verification. Invitation metadata is disclosed only after the authenticated normalized email matches the intended recipient.
- Acceptance revalidates inviter authority, Organization and Workspace eligibility, invitation status, target email, and requested Workspace role. It atomically consumes the invitation, creates or reactivates only the allowed memberships, and persists required audit outbox state.
- An active recipient Workspace membership must already have the requested role; a different active role conflicts without consuming the invitation or changing authority.
- An absent or removed Organization membership becomes baseline `Member`; any active Organization role is preserved. Suspended Organization or Workspace membership blocks acceptance and is never reactivated implicitly.
- Concurrent acceptance and replay produce at most one Organization membership and one Workspace membership. Later requests receive the canonical terminal classification without duplicate mutation.
- Terminal acceptance, revocation, or expiry deletes reversible token, handoff, envelope, and normalized-recipient material after required delivery and audit work. Only non-reversible replay digests/generations and the approved non-secret lifecycle record remain.

## Migration cutover

- The migration transaction preflights every personal owner, adds nullable Workspace-to-Organization and membership structures, preserves `OrganizationId = null` for migrated personal Workspaces, and backfills exactly one active owner membership.
- The same cutover drops `OwnerUserId`, `OwnerEmail`, and their authorization index. Production rollout quiesces Identity writes; no dual authorization or dual schema remains.
- Migration failure rolls back. After success, recovery uses a forward fix or reviewed database restore rather than a generated destructive down-migration.

## Threat model

| Area | Contract |
|---|---|
| Assets | Subject session authority, Workspace data isolation, membership state, transition integrity, and audit integrity. |
| Entry points | Create Organization, list eligible Workspaces, begin/confirm/recover/read transition, authenticated Workspace operations, invite/resend/revoke, token exchange, acceptance, and audit or email delivery. |
| Trust boundaries | Browser to API, API to Identity PostgreSQL, API to Redis, Identity dispatcher to Audit contract/store, and email delivery. |
| Abuse cases | Forced target selection, stale or revoked membership, source/target replay, confirm/recover race, invitation theft or replay, wrong-account acceptance, leaked token or correlation, CSRF, cross-Workspace lookup, poisoned audit envelope, and stale client cache disclosure. |
| Mitigations | Server-derived subject, active-membership checks, non-disclosing lookup, opaque tickets and handoffs, fragment removal, antiforgery confirmation, recovery-only pending state, target-email binding, optimistic terminal concurrency, idempotent cleanup/delivery, redaction validation, and post-confirmation client purge. |
| Evidence | Required use-case AT rows must exercise authorization, isolation, response loss, expiry, races, dependency failure, replay, audit redaction/read-back, and client-state cleanup at their owning boundaries. |
