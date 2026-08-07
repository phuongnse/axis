# Solutions Architecture

> **Navigation**: [docs/ARCHITECTURE.md](../ARCHITECTURE.md) · [docs/use-cases/solutions/README.md](../use-cases/solutions/README.md) · [docs/PLATFORM_STRATEGY.md](../PLATFORM_STRATEGY.md) · [AGENTS.md](../../AGENTS.md)

This file owns durable Solutions boundaries and realization. The publish and install use cases own administrator goals, observable outcomes, and acceptance evidence.

The normative package bytes, component schemas, and DSSE vectors are owned by [docs/architecture/solution-package-v1.md](./solution-package-v1.md).

## Boundary and ubiquitous language

- Solutions is the modular-monolith bounded context for a `Solution`, its immutable `SolutionVersion` and signed package, a workspace-scoped `SolutionInstallation`, and durable `SolutionInstallationOperation` records.
- A solution has a stable semantic solution key. A solution version is the exact SemVer and immutable package hash published for that key. Package bytes and the signed version are deployment-global; an installation and its operations are scoped to one Workspace.
- A package is an exact DSSE envelope and an exact UTF-8 deterministic Axis solution JSON payload. Its manifest declares solution and version identity, the exact committed Axis OpenAPI SHA-256 digest it targets, publisher and key identity, provenance/source revision/build metadata, and an ordered typed component inventory with component hashes and dependencies.
- A component is a module-owned typed document. A component dependency is an edge between declared component identities. Solutions does not interpret component business meaning, write module stores, or accept opaque components.
- A trusted publisher is a deployment-global, configuration-managed public key. Multiple active keys may coexist for rotation. Revocation is immutable: it records that a publisher/key must not be used for publication, new installation, or resumed installation.

## Package wire contract

- Uploads use `application/vnd.dsse.envelope.v1+json`; DSSE v1.0.2 verifies and hands the exact decoded payload bytes to the fixed Axis schema. [docs/architecture/solution-package-v1.md](./solution-package-v1.md) is the sole normative owner of envelope, payload, canonical JSON, components, hashing, limits, and test vectors.
- The package hash is lower-case SHA-256 over exact uploaded envelope bytes. The component hash is lower-case SHA-256 over exact decoded component bytes; neither is computed from a reserialized projection.

## Package trust and publication

- The publish command is an authenticated Workspace Administrator lifecycle operation. Authorization is evaluated before any package or version information is disclosed or changed.
- Solutions retains the exact uploaded DSSE envelope bytes in PostgreSQL `bytea`; no serialization, normalization, or reconstruction may replace those bytes. The payload must be exact UTF-8 deterministic Axis solution JSON.
- Verification fails closed before mutation: envelope structure and payload extraction, deterministic JSON/schema validity, declared identity and exact current committed Axis OpenAPI SHA-256 digest, package limits, ordered inventory/dependency validity, component hashes, DSSE PAE construction, ECDSA P-256/SHA-256 (`ES256`) signature, and currently trusted non-revoked publisher/key must all match.
- Limits are 10 MiB maximum package, 1 MiB maximum decoded component, 256 components, 512 dependency edges, and dependency-DAG depth 32. Package dependencies are not supported in Wave 1.
- Version identity is deduplicated by `(solution key, exact SemVer, package hash)`. An exact retry returns the existing immutable version. An attempt to publish another byte sequence for an existing `(solution key, exact SemVer)` conflicts; it never replaces or aliases the immutable version.
- Version persistence and its redacted audit-outbox record commit atomically. The safe read model exposes identity, manifest/provenance, trust classification, hashes, and lifecycle metadata, but not raw package bytes by default.

## Trusted publisher configuration

- Trusted-publisher keys are supplied only by deployment configuration as semantic publisher ID, unique key ID, ES256 SubjectPublicKeyInfo PEM, and `Active` or `Revoked` status; no Solutions API or UI mutates them. Configuration validation parses every key and status, rejects duplicate/ambiguous identities and invalid key material, and validates the permitted signing algorithm.
- Startup and configuration reload validate the complete candidate snapshot before publication. An invalid candidate leaves the previously active valid snapshot intact; syntactically invalid startup configuration fails. An empty valid snapshot is allowed and trusts no publisher, so publish/install operations fail closed until a valid active key is configured.
- Revocation is immutable and auditable. Removing a key makes it unknown and cannot restore trust; changing a known `Revoked` key back to `Active` is rejected. A currently revoked, missing, or otherwise untrusted publisher/key blocks publication, new installation, and resumption before the next mutation. A reconciliation worker marks every affected existing installation `Noncompliant` and writes its required audit outcome; installed content and its active policy remain usable.
- Solutions persists a monotonic configuration-reconciled publisher/key ledger: identity, SPKI SHA-256 fingerprint, `Active`/`Revoked`, configuration revision, and tombstone. Configuration is its only mutation source. A database revision atomically activates a complete valid snapshot across replicas; operations read that active ledger revision, not process-local configuration. A tombstone forbids resurrection or substitution of a removed/revoked identity with a different SPKI and reconciliation classifies affected installations.

## Installation orchestration

- Installation is an authenticated current-Workspace Administrator operation with an idempotency key scoped to the Workspace and canonical request. Exact retry returns the durable operation; key reuse with different content conflicts.
- Installation revalidates the version's publisher trust and exact current committed Axis OpenAPI SHA-256 digest before creating work. It creates a durable resumable operation with a deterministic topological component plan. The installation pins the exact solution version and may not be silently retargeted.
- Before confirmation or the first apply, every adapter validates and plans every typed component. Solutions persists the complete ordered plan, component hashes/dependencies, and operation state successfully before any module mutation; an invalid final component therefore yields zero module mutations.
- Each plan entry invokes only the owning module's public Contracts adapter. The adapter owns typed document validation, module authorization assumptions, idempotent application, and read-back projection; Solutions owns orchestration and never accesses a module database or internal store.
- A component becomes confirmed only after the adapter's idempotent apply and its read-back match that component's exact declared hash. Failure records the completed and pending plan state; it does not report the installation as successful or infer success from a mutation response.
- Adapters persist installation/component provenance in their module-owned state. Their ordinary create, update, archive, disable, and delete paths reject an installed component identity; manually authored resources retain current behavior. Confirmed installed content remains active after publisher revocation.
- There is no cross-module distributed transaction. Resume rechecks trust and operation state, preserves confirmed entries after matching read-back, and continues only pending work. Database transitions and lease fencing serialize canonical progress; overlapping callers observe the canonical in-progress or terminal operation, and stale workers cannot create a duplicate confirmed effect even though attempted calls may overlap.
- Revocation discovered before a next component mutation halts the operation and leaves confirmed content intact. The installation is marked `Noncompliant`, the halt and classification are audited, and no automatic rollback, uninstall, or replacement mutation occurs.
- Creating an operation commits its deterministic pending steps and returns a durable operation projection; it does not hold the request open as the durability mechanism. A hosted worker acquires a database-backed expiring lease, advances at most one step per committed transition, renews its lease while active, and safely continues pending work after process restart or lease expiry.
- A lease has a monotonically increasing database epoch. Adapter invocation receipt is `(operationId, stepId, componentHash, leaseEpoch)` and adapters reject stale epochs. This fences stale workers and promises no duplicate confirmed effect; it does not claim impossible absence of concurrent attempted calls.
- After a crash between adapter apply and step confirmation, the next worker reads the component back first. A matching hash confirms without another mutation; a missing component invokes the adapter's idempotent apply; a mismatched component fails closed. Retryable adapter failure records `Failed` with safe diagnostics and requires an authorized resume; terminal validation/trust failure cannot be resumed while its condition remains.

## States and MCP boundary

- `SolutionInstallation.ProvisioningStatus` is `Installing`, `Installed`, or `Failed`; `ComplianceStatus` is independently `Compliant` or `Noncompliant`. `Installing` becomes `Installed` only when every step is confirmed; retryable or blocked operation failure makes it `Failed`. An installed/noncompliant installation stays active. A partially applied revoked installation is `Failed`/`Noncompliant`, retained for diagnosis, and is never activated.
- `SolutionInstallationOperation` is `Pending`, `Running`, `Failed` (retryable), `Blocked` (terminal validation/trust), or `Succeeded`. Confirmation moves `Pending` to `Running`; a lease holder moves `Running` to `Succeeded`, `Failed`, or `Blocked`; an authorized resume moves only `Failed` to `Pending` after fresh preflight. Lease expiry returns an unfinished `Running` operation to `Pending` without changing confirmed steps. `Blocked` never resumes under the same condition.
- A step is `Pending`, `Applying`, `Confirmed`, or `Failed`. Lease holder claims `Pending` to `Applying`; matching adapter receipt/read-back confirms it; retryable failure returns it to `Pending` while recording operation `Failed`; validation, trust, or mismatch makes it `Failed` and the operation `Blocked`. Confirmed never regresses.
- MCP publishes only authenticated typed operations. `publish_solution_version` accepts an explicit local package file path, verifies it resolves to a regular file and is at most 10 MiB, uploads its bytes without reading package content into tool output, and returns only safe version status. `list_solution_versions`, `get_solution_version_status`, `install_solution_version`, `get_solution_installation_status`, and `resume_solution_installation` accept typed identifiers and return safe projections. MCP has no trusted-publisher mutation or raw-package tool.

## Persistence, consistency, and events

- Solutions owns migration-backed relational persistence for solutions, immutable versions, envelope bytes and manifest projection, installations, installation operations/steps, idempotency records, and their revisions/statuses. Immutable package bytes are never updated in place.
- Aggregate and request concurrency protect version creation, installation state, operation state, and step confirmation. Database uniqueness enforces immutable version identity and scoped idempotency; deterministic plan identity makes retries auditable.
- Commands and handlers own publication and installation mutations. Queries expose safe version, installation, and operation status read models. Failure mapping is stable and non-disclosing across invalid package, trust, compatibility, conflict, authorization, and recoverable operation failures.
- Solutions Application references `Axis.Authorization.Contracts`, `Axis.BusinessObjects.Contracts`, and `Axis.Rules.Contracts` only. Each contract exposes typed validate/plan, idempotent apply, and canonical read-back for its component kinds; Solutions Infrastructure and API compose adapters without giving Solutions access to another module's Application, Domain, Infrastructure, or database.
- Every required publication, trust-classification, installation lifecycle, and operation transition writes a versioned redacted audit-outbox envelope in the same Solutions transaction as its state change. Audit delivery/retry and retention follow the Audit boundary; package bytes, signatures, secrets, and component bodies are excluded from audit payloads.
- Required denied/no-business-mutation outcomes (authorization denial, invalid package, failed trust/configuration/compatibility preflight, or blocked resume) use a separate fail-closed audit-only outbox transaction and read it back before returning.
- Solutions domain events are integration facts, not event sourcing. Event sourcing, replay, package-body rebuild from events, and cross-module transactional events are out of scope.

## Explicit exclusions

- Wave 1 has no version upgrade, rollback, uninstall, marketplace, overlay, workspace trust policy, package dependency graph, product-data migration, drift detection, promotion, or trusted-publisher mutation surface.
- First module adapters are Authorization policy, Business Object definition, and Rule binding to an existing immutable built-in Rule definition. Each remains the owner of its typed schema and resulting module state.
- The Wave 0 reference-product provisioning path is retired only by its owning implementation and external-product evidence in a clean cutover; Solutions provides no compatibility shim or dual path.
