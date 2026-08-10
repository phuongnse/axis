# Identity Governance Architecture

> **Navigation**: [docs/ARCHITECTURE.md](../ARCHITECTURE.md) · [docs/use-cases/identity-governance/README.md](../use-cases/identity-governance/README.md) · [docs/PLATFORM_STRATEGY.md](../PLATFORM_STRATEGY.md) · [AGENTS.md](../../AGENTS.md)

This file owns durable Identity Governance invariants and technical realization. Use cases own actor goals and observable behavior; [docs/TECH_STACK.md](../TECH_STACK.md) owns approved technologies.

## Model invariants

- `Organization` is the enterprise governance container. `Workspace` is the active data and isolation context; a personal Workspace has no Organization, while an organization Workspace belongs to exactly one Organization.
- `OrganizationMembership` governs organization lifecycle with `Owner`, `Administrator`, and `Member`. `WorkspaceMembership` is the sole Workspace access relationship. Organization membership never implies Workspace access.
- Organization Workspaces use `Administrator` or `Member` Workspace roles. A personal Workspace admits exactly one active `Owner` membership and no invitation or additional membership.
- Workspace lifecycle administration is role-contextual: the active `Owner` administers a personal Workspace, while an active `Administrator` administers an organization Workspace. This authority covers Workspace-scoped Solution, service-identity, and product-role lifecycle operations. It does not let a personal owner invite members, create Organization membership, or substitute an Identity lifecycle role for a product role.
- A Workspace invitation may establish only a missing or removed baseline Organization `Member`; it never grants or promotes Organization `Owner` or `Administrator`.
- Identity lifecycle roles do not encode product roles. Versioned Authorization policies own product action and resource decisions; Team remains collaboration/assignment vocabulary and Group remains IdP or authorization-group vocabulary.
- `Organization`, `Workspace`, `OrganizationMembership`, `WorkspaceMembership`, `WorkspaceInvitation`, and `WorkspaceContextTransition` are separate aggregates. Unique membership/invitation keys, aggregate revisions, and one Identity unit of work own concurrency.

## Subject authority and isolation

- A selected `workspace_id` is context, not authority. Every cookie- or bearer-authenticated Workspace operation passes one asynchronous subject-neutral Workspace-access policy after authentication and before module data access.
- The human-subject arm requires an active `WorkspaceMembership`. The service-subject arm requires an active intrinsic Workspace grant but is denied by every endpoint that composes only baseline Workspace access. A service subject is admitted only when that endpoint/application also composes an exact Authorization product-action decision; this restriction does not broaden human baseline behavior. Unknown subject kinds deny.
- Account, eligible-Workspace, transition recovery, token exchange, and OAuth bootstrap operations are the only explicit exceptions to Workspace-scoped enforcement. Claims, frontend visibility, and Organization membership never substitute for the policy.
- Cross-Workspace resource lookup returns a non-disclosing not-found outcome. A known forbidden Identity lifecycle action returns permission denied.

## Service identity realization

- Identity owns the lifecycle of a service identity, its active Workspace grant, and its public signing keys. A service identity belongs to exactly one current Workspace through one intrinsic active grant; creating it requires active Workspace lifecycle-administrator authority in that Workspace. It is not a human membership, Organization role, product role, Team, Group, or cross-Workspace principal. Revoking the identity irreversibly revokes that grant; the grant has no independent reassignment or reactivation path.
- A service identity may have multiple active ES256 public JWKs to permit controlled key overlap and rotation. Each active key has a unique `kid` within the identity. Revocation retains immutable tombstones for the revoked `kid` and the RFC 7638 public-JWK thumbprint: neither that `kid` nor that key material under a renamed `kid` can ever be accepted again for the identity. Identity never accepts, stores, displays, or exchanges a shared client secret.
- OAuth client-credentials authentication accepts only `private_key_jwt`. A short-lived assertion has `iss` and `sub` equal to the service identity's client identifier, the exact configured token-endpoint audience, an active known `kid`, and required `iat`, `exp`, and unique `jti` claims. `exp - iat` and assertion age are each at most five minutes; validation permits at most 30 seconds of clock skew. An optional `nbf` cannot be after effective current time or before `iat`. Identity validates the signature against the selected active public JWK and rejects a missing, malformed, overlong, expired, future-invalid, mismatched, unknown, revoked, or replayed assertion before issuing authority.
- The successful grant produces only a short-lived opaque/reference service access token carrying the service identity, Workspace, and immutable internal authenticating-key ID. It has no refresh-token, browser, authorization-code, or cross-Workspace grant path. Every service-token validation rechecks the identity, intrinsic Workspace grant, and signing-key lifecycle so revoking an identity or key denies immediately, including for a previously issued token.
- `ServiceIdentity` is the lifecycle source of truth. Its client identifier, active public-key set, client-credentials-only permission, and five-minute access-token lifetime are projected atomically into an OpenIddict application in the same Identity store; a projection mismatch or partial write rolls back. OpenIddict validates `private_key_jwt`, while an Identity handler adds replay-digest and current-lifecycle checks before token issuance.
- OpenIddict reference access tokens and token-entry validation provide one opaque bearer representation for both human and service bearer clients; existing browser cookies remain a separate session mechanism. The token principal carries a discriminated subject kind. Service principals carry no human email/profile claims and are limited to the configured five-minute lifetime; human bearer lifetime remains governed by the existing session contract.
- Service-identity creation, key addition, key revocation, identity revocation, authentication success, authentication denial, assertion replay, and token rejection persist required redacted audit outcomes. Required audit persistence or validation-dependency failure fails closed. Audit metadata never contains assertion bodies, token values, private key material, JWK private parameters, or replayable identifiers.
- Lifecycle mutations use aggregate revisions and identity-scoped idempotency where a caller can safely retry. Concurrent key addition preserves the unique-`kid` and unique-public-key-material invariants; concurrent revoke and authentication return one lifecycle-consistent outcome without resurrecting a revoked identity, grant, or key. Revocation is irreversible; recovery is creation of a new identity or key under current authority. Accepted assertion `jti` values are stored only as bounded non-reversible digests in Identity PostgreSQL through `exp + 30 seconds`, then purged.

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
| Assets | Subject session authority, service private-key proof, public-key and tombstone integrity, OpenIddict applications/token entries, replay-digest records, Workspace data isolation, membership state, transition integrity, and audit integrity. |
| Entry points | Create Organization, list eligible Workspaces, begin/confirm/recover/read transition, authenticated Workspace operations, invite/resend/revoke, service-key lifecycle, `/connect/token`, token validation, acceptance, and audit or email delivery. |
| Trust boundaries | Browser to API, service operator to `/connect/token`, API to Identity PostgreSQL (including replay store and OpenIddict state), API to Redis, Identity dispatcher to Audit contract/store, and email delivery. |
| Abuse cases | Forced target selection, stale or revoked membership, service key or algorithm confusion, renamed-key resurrection, client assertion replay, private-key compromise, token-entry confusion, credential-stuffing/rate or write amplification, generic-denial probing, source/target replay, confirm/recover race, invitation theft or replay, wrong-account acceptance, leaked token or correlation, CSRF, cross-Workspace lookup, poisoned audit envelope, and stale client cache disclosure. |
| Mitigations | Server-derived subject, baseline service denial unless exact product action is composed, active role-contextual lifecycle-administrator membership checks, active grant/key checks, fixed ES256 and public-JWK validation, immutable key tombstones, bounded PostgreSQL replay digests, generic denials, rate/write controls, non-disclosing lookup, opaque tickets and handoffs, fragment removal, antiforgery confirmation, recovery-only pending state, target-email binding, optimistic terminal concurrency, idempotent cleanup/delivery, redaction validation, and post-confirmation client purge. |
| Evidence | Required use-case AT rows must exercise personal-Owner and organization-Administrator lifecycle authority, organization-Member denial, human and service Workspace admission, no-assignment endpoint denial, service-key rotation/revocation/resurrection, assertion lifetime/skew/replay boundaries, algorithm/key confusion, dependency failure, policy action enforcement, audit redaction/read-back, and the existing isolation, recovery, expiry, race, and client-state-cleanup boundaries. |
