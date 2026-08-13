# Publish A Signed Solution Version

> **Navigation**: [docs/use-cases/solutions/README.md](./README.md) · [docs/architecture/solutions.md](../../architecture/solutions.md) · [docs/PLATFORM_STRATEGY.md](../../PLATFORM_STRATEGY.md) · [AGENTS.md](../../../AGENTS.md)

> **Contract status:** Implementation and acceptance evidence are complete.

## Purpose

Allow a Workspace lifecycle administrator to publish one trusted, immutable signed solution package so it can later be installed into authorized Workspaces without exposing raw package bytes by default.

## Primary actor

- Authenticated Workspace lifecycle administrator publishing a solution release

## Preconditions

- The actor is the active `Owner` of the current personal Workspace or an active `Administrator` of the current organization Workspace.
- A deployment-valid trusted-publisher configuration contains the non-revoked publisher key used to sign the exact package.
- The uploaded package is within the declared package, component, inventory, dependency, exact Axis OpenAPI digest, and signature contract.

## Trigger

- An administrator makes a signed solution release available for installation.

## Success guarantee

- Exactly one trusted immutable solution version is available by its solution identity, exact SemVer, and package hash, with safe status read-back and an auditable publication outcome.

## Minimal guarantee

- An invalid, untrusted, revoked, OpenAPI-digest-mismatched, duplicate-conflicting, or failed publication creates no version and no misleading success outcome.

## Main flow

1. The administrator opens the Solutions publishing experience and selects an exact signed package.
2. The experience identifies the target package and explains that publishing verifies publisher trust and creates an immutable version; it does not expose package contents from prior releases.
3. The administrator confirms publication.
4. Axis authenticates and authorizes the current Workspace lifecycle administrator, verifies the exact signed package and its declared exact committed Axis OpenAPI digest, inventory, dependencies, hashes, and trusted non-revoked publisher.
5. Axis records the immutable version or returns an existing exact version for an idempotent repeat, with the required audit outcome.
6. The experience reads back the safe version projection and shows its identity, package hash, publisher/key, Axis OpenAPI digest, provenance, and publish status.

## Alternate / error flows

- Missing authority or Workspace scope rejects without package/version disclosure or mutation.
- Malformed envelope/payload, invalid deterministic JSON, schema/inventory/dependency/limit violation, mismatched hash or current Axis OpenAPI digest, invalid signature, unknown key, or revoked publisher rejects before persistence with safe actionable diagnostics.
- An existing solution key and exact SemVer with different package bytes conflicts; no version is replaced, adopted, or aliased.
- A configured publisher key that is invalid or an atomically rejected configuration reload makes publishing unavailable; Axis retains no partial new trusted-key state.
- Persistence or required audit recording failure reports failure and does not create a published version.
- A lost client response is recovered by safe read-back using the published identity/hash; a repeat with the same immutable package is safe.

## Acceptance Criteria

*Happy path*

- **AC-001** The active `Owner` of a current personal Workspace or an active `Administrator` of a current organization Workspace can publish a package only after Axis authenticates, authorizes, and verifies the exact DSSE envelope and its exact deterministic UTF-8 Axis solution JSON payload.
- **AC-002** The manifest contains solution/version identity, the exact committed Axis OpenAPI SHA-256 digest, publisher/key identity, provenance/source revision/build metadata, and an ordered typed component inventory with hashes and dependencies.
- **AC-003** Axis accepts only a valid `ES256` DSSE PAE signature from a currently trusted, non-revoked deployment-configured public key.
- **AC-004** A successful publish persists one immutable version identified by solution key, exact SemVer, and package hash, atomically records a redacted audit outcome, and exposes safe identity/provenance/trust/hash read-back without raw package bytes by default.
- **AC-005** Repeating the exact immutable package returns the canonical existing version; a different package for the same solution key and exact SemVer is a conflict.

*Validation, trust, and failures*

- **AC-006** Axis rejects before mutation an invalid envelope, non-UTF-8 or non-deterministic payload, schema violation, invalid declared identity or mismatched current Axis OpenAPI digest, component/dependency error, hash mismatch, invalid signature, unknown publisher/key, or revoked publisher/key.
- **AC-007** Axis enforces a 10 MiB package limit, 1 MiB decoded-component limit, 256-component limit, 512-edge limit, and acyclic dependency graph with depth at most 32.
- **AC-008** Trusted publishers are deployment-global configuration-managed public keys; multiple active keys support rotation, invalid startup or reload candidates fail atomically, and no publisher mutation API or UI exists.
- **AC-009** Revocation is immutable and blocks publication. Revocation, validation failures, and failures to persist the required audit record never yield a successful publish outcome.
- **AC-010** Authorization, validation, trust, conflict, and dependency errors do not disclose raw package bytes, another Workspace's data, secrets, or signature material beyond safe diagnostics.
- **AC-011** Axis implements the fixed v1 package bytes and typed component contracts in [docs/architecture/solution-package-v1.md](../../architecture/solution-package-v1.md): DSSE v1.0.2, standard/base64url distinctions, exact PAE, unknown-envelope handling, exact verified-byte handoff, and fail-closed payload unknowns.
- **AC-012** Trusted publisher/key configuration reconciles through the monotonic deployment-global ledger with identity, SPKI fingerprint, Active/Revoked state, configuration revision, and tombstone; operations read its active database revision and reject resurrection, substitution, missing keys, and invalid atomic configuration candidates.
- **AC-013** Every denied/no-business-mutation publication outcome persists and reads back its required redacted audit-only outbox record before returning; ordinary success persists its audit outcome atomically with the immutable version.
- **AC-014** An authenticated MCP publisher tool accepts only an explicit regular local package file at or below the package limit, uploads bytes without surfacing contents, and returns only the safe projection; MCP exposes no raw-package or publisher-management tool.

*Boundaries*

- **AC-015** The publish journey owns immutable package availability only; installing components, upgrade, rollback, uninstall, marketplace, overlays, workspace trust, package dependencies, and product data migrations are out of scope.
- **AC-016** Package dependencies are unsupported by this contract, and Solutions accepts only typed module-owned component documents rather than opaque generic JSON.
- **AC-017** Signed publication is the only release-availability path; no compatibility, fallback, flag, or alternate publication path exists.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Application boundary | Exact v1 byte schema, deterministic payload, typed component schema, limits, DAG, DSSE v1.0.2 PAE/base64 vectors, and ES256 verification accept only conformance vectors before a command mutation | AC-001, AC-002, AC-003, AC-006, AC-007, AC-011, AC-016 | Application test + API integration test | Yes |
| AT-002 | Infrastructure boundary | Immutable version uniqueness, exact retry, conflicting bytes, exact envelope-byte retention, audit-outbox atomicity/read-back, and safe projection persistence | AC-004, AC-005, AC-009, AC-010, AC-013 | Infrastructure integration test | Yes |
| AT-003 | Application boundary | Ledger reconciliation activates a valid revision atomically across replicas; rejects invalid config, missing/revived/substituted keys; and blocks unknown/revoked publication | AC-003, AC-008, AC-009, AC-012 | Application test | Yes |
| AT-004 | API boundary | Authenticated personal `Owner` and organization `Administrator` publish/read-back authority succeeds, while organization `Member` and inactive membership deny with stable safe errors, no raw package default, and generated contract parity | AC-001, AC-004, AC-006, AC-010 | Domain test + API integration test | Yes |
| AT-005 | Browser journey | Publishing identifies the immutable release, confirms the consequence, shows safe success read-back, and supports validation/trust/conflict/retry states with keyboard and screen-reader recovery | AC-004, AC-005, AC-006, AC-009 | UI component test + Browser automation | Yes |
| AT-006 | API/MCP boundaries | MCP local-path regular-file/size checks upload bytes without raw-content output, while API/MCP expose no publisher mutation or excluded lifecycle/compatibility behavior | AC-014, AC-015, AC-016, AC-017 | Architecture test + MCP contract test | Yes |

## Out Of Scope

- Installation, upgrade, rollback, uninstall, marketplace, overlays, promotion, workspace trust management, package dependencies, product data migrations, drift detection, and raw-package download.
- Trusted-publisher management by an API or UI.

## Screen flow

| Surface | Required contract |
|---|---|
| Collection entry | The Solutions route presents one primary solution-version table that combines immutable release identity with current-Workspace installation state. Publish is owned by that table's toolbar, and the collection remains available while a focused publishing task is open. |
| Publish entry | Opens as a stable managed task, states the solution/version and package identity before confirmation, and keeps the primary action unavailable until a package is selected. |
| Publish confirmation | Explains that the release becomes immutable and will be checked against trusted-publisher policy; confirmation receives focus only when actionable. |
| Verifying/publishing | Announces verification and mutation progress without repeatedly stealing focus. Cancellation/dismissal cannot imply that a server mutation was undone. |
| Result | Shows safe identity, version, hash, publisher/key, Axis OpenAPI digest, provenance, and next action. It never renders raw package bytes, signatures, secrets, or another Workspace's data. |
| Failure/recovery | Identifies a safe package-local reason and recovery action; trust, conflict, validation, unavailable configuration, and retryable transport failures remain distinct. |

Required UI quality: the collection follows the [Collection Page](../../foundations/data-display/collection-page.md) contract and publishing follows the [Managed Dialog](../../foundations/overlays/managed-dialog.md) task contract; status and result information are programmatically labelled; the full result and recovery path are keyboard-accessible and announced; compact and desktop layouts preserve long keys, hashes, and provenance without overflow; universal UI copy is localized; no secret, raw package, signature, or cross-Workspace data is rendered.

> **Implementation status**
>
> | Layer | Status |
> |---|---|
> | Domain | Done |
> | Application | Done |
> | Infrastructure | Done |
> | API | Done |
> | Frontend | Done |
> | MCP | Done |
> | Audit | Done |
>
> **Gaps vs spec:** None.
>
> **Deferred follow-ups:** The explicitly out-of-scope lifecycle capabilities above require their own use cases.
>
> **Verification:** [publish-signed-solution-version.evidence.md](./publish-signed-solution-version.evidence.md) records the exact package-conformance fixture/vectors, typed preflight, immutable persistence, trust, API, MCP, frontend, and governed browser evidence.
>
> **Decisions:** [docs/architecture/solutions.md](../../architecture/solutions.md) owns signature, publisher trust, immutable persistence, audit-outbox, and safe-readback realization.
