# Publish A Signed Solution Version

> **Navigation**: [docs/use-cases/solutions/README.md](./README.md) · [docs/architecture/solutions.md](../../architecture/solutions.md) · [docs/PLATFORM_STRATEGY.md](../../PLATFORM_STRATEGY.md) · [AGENTS.md](../../../AGENTS.md)

> **Contract status:** Ready for implementation. Implementation layers and acceptance evidence are not started.

## Purpose

Allow a Workspace Administrator to publish one trusted, immutable signed solution package so it can later be installed into authorized Workspaces without exposing raw package bytes by default.

## Primary actor

- Authenticated Workspace Administrator publishing a solution release

## Preconditions

- The actor has current-Workspace Administrator authority.
- A deployment-valid trusted-publisher configuration contains the non-revoked publisher key used to sign the exact package.
- The uploaded package is within the declared package, component, inventory, dependency, compatibility, and signature contract.

## Trigger

- An administrator makes a signed solution release available for installation.

## Success guarantee

- Exactly one trusted immutable solution version is available by its solution identity, exact SemVer, and package hash, with safe status read-back and an auditable publication outcome.

## Minimal guarantee

- An invalid, untrusted, revoked, incompatible, duplicate-conflicting, or failed publication creates no version and no misleading success outcome.

## Main flow

1. The administrator opens the Solutions publishing experience and selects an exact signed package.
2. The experience identifies the target package and explains that publishing verifies publisher trust and creates an immutable version; it does not expose package contents from prior releases.
3. The administrator confirms publication.
4. Axis authenticates and authorizes the current Workspace Administrator, verifies the exact signed package and its declared compatibility, inventory, dependencies, hashes, and trusted non-revoked publisher.
5. Axis records the immutable version or returns an existing exact version for an idempotent repeat, with the required audit outcome.
6. The experience reads back the safe version projection and shows its identity, package hash, publisher/key, compatibility, provenance, and publish status.

## Alternate / error flows

- Missing authority or Workspace scope rejects without package/version disclosure or mutation.
- Malformed envelope/payload, invalid deterministic JSON, schema/inventory/dependency/limit violation, mismatched hash, unsupported compatibility, invalid signature, unknown key, or revoked publisher rejects before persistence with safe actionable diagnostics.
- An existing solution key and exact SemVer with different package bytes conflicts; no version is replaced, adopted, or aliased.
- A configured publisher key that is invalid or an atomically rejected configuration reload makes publishing unavailable; Axis retains no partial new trusted-key state.
- Persistence or required audit recording failure reports failure and does not create a published version.
- A lost client response is recovered by safe read-back using the published identity/hash; a repeat with the same immutable package is safe.

## Acceptance Criteria

*Happy path*

- **AC-001** A current-Workspace Administrator can publish a package only after Axis authenticates, authorizes, and verifies the exact DSSE envelope and its exact deterministic UTF-8 Axis solution JSON payload.
- **AC-002** The manifest contains solution/version identity, Axis compatibility, publisher/key identity, provenance/source revision/build metadata, and an ordered typed component inventory with hashes and dependencies.
- **AC-003** Axis accepts only a valid `ES256` DSSE PAE signature from a currently trusted, non-revoked deployment-configured public key.
- **AC-004** A successful publish persists one immutable version identified by solution key, exact SemVer, and package hash, atomically records a redacted audit outcome, and exposes safe identity/provenance/trust/hash read-back without raw package bytes by default.
- **AC-005** Repeating the exact immutable package returns the canonical existing version; a different package for the same solution key and exact SemVer is a conflict.

*Validation, trust, and failures*

- **AC-006** Axis rejects before mutation an invalid envelope, non-UTF-8 or non-deterministic payload, schema violation, invalid declared identity/compatibility, component/dependency error, hash mismatch, invalid signature, unknown publisher/key, or revoked publisher/key.
- **AC-007** Axis enforces a 10 MiB package limit, 1 MiB decoded-component limit, 256-component limit, 512-edge limit, and acyclic dependency graph with depth at most 32.
- **AC-008** Trusted publishers are deployment-global configuration-managed public keys; multiple active keys support rotation, invalid startup or reload candidates fail atomically, and no publisher mutation API or UI exists.
- **AC-009** Revocation is immutable and blocks publication. Revocation, validation failures, and failures to persist the required audit record never yield a successful publish outcome.
- **AC-010** Authorization, validation, trust, conflict, and dependency errors do not disclose raw package bytes, another Workspace's data, secrets, or signature material beyond safe diagnostics.

*Boundaries*

- **AC-011** The publish journey owns immutable package availability only; installing components, upgrade, rollback, uninstall, marketplace, overlays, workspace trust, package dependencies, and product data migrations are out of scope.
- **AC-012** Package dependencies are unsupported in Wave 1, and Solutions accepts only typed module-owned component documents rather than opaque generic JSON.
- **AC-013** The existing Wave 0 reference-product provisioning contract remains unchanged until its implementation and external-product evidence permit one clean cutover; Wave 1 adds no compatibility or dual runtime path.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Domain boundary | Exact envelope, deterministic payload, manifest, inventory, hash, package limits, DAG, PAE, and ES256 verification accept one valid package and reject every invalid form before a command mutation | AC-001, AC-002, AC-003, AC-006, AC-007, AC-012 | Domain test + Application test | Yes |
| AT-002 | Infrastructure boundary | Immutable version uniqueness, exact retry, conflicting bytes, exact envelope-byte retention, audit-outbox atomicity, and safe projection persistence | AC-004, AC-005, AC-009, AC-010 | Infrastructure integration test | Yes |
| AT-003 | Application boundary | Multiple active trusted keys rotate safely; invalid startup/reload candidate is atomic; unknown and revoked keys block publication; no mutation contract is registered | AC-003, AC-008, AC-009 | Application test | Yes |
| AT-004 | API boundary | Authenticated administrator publish/read-back enforces Workspace authority, stable safe errors, no raw package default, and generated contract parity | AC-001, AC-004, AC-006, AC-010 | API integration test | Yes |
| AT-005 | Browser journey | Publishing identifies the immutable release, confirms the consequence, shows safe success read-back, and supports validation/trust/conflict/retry states with keyboard and screen-reader recovery | AC-004, AC-005, AC-006, AC-009 | UI component test + Browser automation | Yes |
| AT-006 | API boundary | No install/upgrade/rollback/uninstall/marketplace/overlay/workspace-trust/package-dependency mutation behavior or Wave 0 compatibility path is introduced | AC-011, AC-012, AC-013 | Architecture test + API integration test | Yes |

## Out Of Scope

- Installation, upgrade, rollback, uninstall, marketplace, overlays, promotion, workspace trust management, package dependencies, product data migrations, drift detection, and raw-package download.
- Trusted-publisher management by an API or UI.
- Any replacement of the Wave 0 reference-product provisioning path before its clean-cutover evidence is ready.

## Screen flow

| Surface | Required contract |
|---|---|
| Publish entry | States the solution/version and package identity before confirmation; the primary action is unavailable until a package is selected. |
| Publish confirmation | Explains that the release becomes immutable and will be checked against trusted-publisher policy; confirmation receives focus only when actionable. |
| Verifying/publishing | Announces verification and mutation progress without repeatedly stealing focus. Cancellation/dismissal cannot imply that a server mutation was undone. |
| Result | Shows safe identity, version, hash, publisher/key, compatibility, provenance, and next action. It never renders raw package bytes, signatures, secrets, or another Workspace's data. |
| Failure/recovery | Identifies a safe package-local reason and recovery action; trust, conflict, validation, unavailable configuration, and retryable transport failures remain distinct. |

Required UI quality: status and result information are programmatically labelled; the full result and recovery path are keyboard-accessible and announced; compact and desktop layouts preserve long keys, hashes, and provenance without overflow; universal UI copy is localized; no secret, raw package, signature, or cross-Workspace data is rendered.

> **Implementation status**
>
> | Layer | Status |
> |---|---|
> | Domain | Not started |
> | Application | Not started |
> | Infrastructure | Not started |
> | API | Not started |
> | Frontend | Not started |
> | MCP | Not started |
> | Audit | Not started |
>
> **Gaps vs spec:**
>
> | ID | Gap |
> |---|---|
> | GAP-001 | Domain, application, infrastructure, API, frontend, MCP, and Audit implementation are not started. |
> | GAP-002 | No acceptance evidence exists. |
>
> **Deferred follow-ups:** The explicitly out-of-scope lifecycle capabilities above require their own use cases.
>
> **Verification:** Not run; implementation evidence does not exist yet.
>
> **Decisions:** [docs/architecture/solutions.md](../../architecture/solutions.md) owns signature, publisher trust, immutable persistence, audit-outbox, and safe-readback realization.
