# Solutions Architecture

> **Navigation**: [docs/ARCHITECTURE.md](../ARCHITECTURE.md) · [docs/use-cases/solutions/README.md](../use-cases/solutions/README.md) · [docs/PLATFORM_STRATEGY.md](../PLATFORM_STRATEGY.md) · [AGENTS.md](../../AGENTS.md)

This file owns durable Solutions boundaries and realization. The publish and install use cases own administrator goals, observable outcomes, and acceptance evidence.

## Boundary and ubiquitous language

- Solutions is the modular-monolith bounded context for a `Solution`, its immutable `SolutionVersion` and signed package, a workspace-scoped `SolutionInstallation`, and durable `SolutionInstallationOperation` records.
- A solution has a stable semantic solution key. A solution version is the exact SemVer and immutable package hash published for that key. Package bytes and the signed version are deployment-global; an installation and its operations are scoped to one Workspace.
- A package is an exact DSSE envelope and an exact UTF-8 deterministic Axis solution JSON payload. Its manifest declares solution and version identity, Axis compatibility, publisher and key identity, provenance/source revision/build metadata, and an ordered typed component inventory with component hashes and dependencies.
- A component is a module-owned typed document. A component dependency is an edge between declared component identities. Solutions does not interpret component business meaning, write module stores, or accept opaque components.
- A trusted publisher is a deployment-global, configuration-managed public key. Multiple active keys may coexist for rotation. Revocation is immutable: it records that a publisher/key must not be used for publication, new installation, or resumed installation.

## Package wire contract

- The upload content type is `application/vnd.dsse.envelope.v1+json`. The envelope contains exactly one Wave 1 signature and uses payload type `application/vnd.axis.solution.v1+json`. Its decoded payload bytes, not a parsed or reconstructed equivalent, are the bytes passed from signature verification to package validation.
- The Axis solution payload is a fixed-schema version-1 document with `schemaVersion`, semantic `solutionKey`, exact `solutionVersion`, exact `axisOpenApiSha256`, authenticated `publisherId` and `publisherKeyId`, source/build provenance, and `components`. Each component contains semantic `type` and `key`, a lower-case SHA-256, base64-encoded exact UTF-8 typed-document bytes, and ordered `(type, key)` dependencies. Package-to-package dependencies are not present.
- Payload JSON is UTF-8 without BOM and is byte-deterministic: the schema fixes property order; unknown or duplicate properties, insignificant whitespace, non-NFC or untrimmed strings, floating-point numbers, and non-canonical base64 are invalid. Components sort by ordinal `(type, key)` and dependencies sort by the same tuple. Each module adapter applies the same byte-equality rule to its typed component schema.
- The package hash is lower-case SHA-256 over the exact uploaded envelope bytes. A component hash is lower-case SHA-256 over its exact decoded typed-document bytes. The server never hashes a reserialized projection in place of either byte sequence.
- DSSE verification follows `PAE(UTF8(payloadType), payloadBytes)`. Wave 1 uses ECDSA NIST P-256 with SHA-256 and a 64-byte IEEE-P1363 `r || s` signature. Envelope `keyid` is only an unauthenticated lookup hint; the verified key must equal the authenticated payload's publisher/key identity and current trusted configuration.

## Package trust and publication

- The publish command is an authenticated Workspace Administrator lifecycle operation. Authorization is evaluated before any package or version information is disclosed or changed.
- Solutions retains the exact uploaded DSSE envelope bytes in PostgreSQL `bytea`; no serialization, normalization, or reconstruction may replace those bytes. The payload must be exact UTF-8 deterministic Axis solution JSON.
- Verification fails closed before mutation: envelope structure and payload extraction, deterministic JSON/schema validity, declared identity and compatibility, package limits, ordered inventory/dependency validity, component hashes, DSSE PAE construction, ECDSA P-256/SHA-256 (`ES256`) signature, and currently trusted non-revoked publisher/key must all match.
- Limits are 10 MiB maximum package, 1 MiB maximum decoded component, 256 components, 512 dependency edges, and dependency-DAG depth 32. Package dependencies are not supported in Wave 1.
- Version identity is deduplicated by `(solution key, exact SemVer, package hash)`. An exact retry returns the existing immutable version. An attempt to publish another byte sequence for an existing `(solution key, exact SemVer)` conflicts; it never replaces or aliases the immutable version.
- Version persistence and its redacted audit-outbox record commit atomically. The safe read model exposes identity, manifest/provenance, trust classification, hashes, and lifecycle metadata, but not raw package bytes by default.

## Trusted publisher configuration

- Trusted-publisher keys are supplied only by deployment configuration as semantic publisher ID, unique key ID, ES256 SubjectPublicKeyInfo PEM, and `Active` or `Revoked` status; no Solutions API or UI mutates them. Configuration validation parses every key and status, rejects duplicate/ambiguous identities and invalid key material, and validates the permitted signing algorithm.
- Startup and configuration reload validate the complete candidate snapshot before publication. An invalid candidate leaves the previously active valid snapshot intact; syntactically invalid startup configuration fails. An empty valid snapshot is allowed and trusts no publisher, so publish/install operations fail closed until a valid active key is configured.
- Revocation is immutable and auditable. Removing a key makes it unknown and cannot restore trust; changing a known `Revoked` key back to `Active` is rejected. A currently revoked, missing, or otherwise untrusted publisher/key blocks publication, new installation, and resumption before the next mutation. A reconciliation worker marks every affected existing installation `Noncompliant` and writes its required audit outcome; installed content and its active policy remain usable.

## Installation orchestration

- Installation is an authenticated current-Workspace Administrator operation with an idempotency key scoped to the Workspace and canonical request. Exact retry returns the durable operation; key reuse with different content conflicts.
- Installation revalidates the version's publisher trust and compatibility before creating work. It creates a durable resumable operation with a deterministic topological component plan. The installation pins the exact solution version and may not be silently retargeted.
- Each plan entry invokes only the owning module's public Contracts adapter. The adapter owns typed document validation, module authorization assumptions, idempotent application, and read-back projection; Solutions owns orchestration and never accesses a module database or internal store.
- A component becomes confirmed only after the adapter's idempotent apply and its read-back match that component's exact declared hash. Failure records the completed and pending plan state; it does not report the installation as successful or infer success from a mutation response.
- There is no cross-module distributed transaction. Resume rechecks trust and operation state, preserves confirmed entries after matching read-back, and continues only pending work. Concurrent resumes of one operation are single-flight; an additional caller receives the canonical in-progress or terminal operation rather than a second plan execution.
- Revocation discovered before a next component mutation halts the operation and leaves confirmed content intact. The installation is marked `Noncompliant`, the halt and classification are audited, and no automatic rollback, uninstall, or replacement mutation occurs.
- Creating an operation commits its deterministic pending steps and returns a durable operation projection; it does not hold the request open as the durability mechanism. A hosted worker acquires a database-backed expiring lease, advances at most one step per committed transition, renews its lease while active, and safely continues pending work after process restart or lease expiry.
- After a crash between adapter apply and step confirmation, the next worker reads the component back first. A matching hash confirms without another mutation; a missing component invokes the adapter's idempotent apply; a mismatched component fails closed. Retryable adapter failure records `Failed` with safe diagnostics and requires an authorized resume; terminal validation/trust failure cannot be resumed while its condition remains.

## Persistence, consistency, and events

- Solutions owns migration-backed relational persistence for solutions, immutable versions, envelope bytes and manifest projection, installations, installation operations/steps, idempotency records, and their revisions/statuses. Immutable package bytes are never updated in place.
- Aggregate and request concurrency protect version creation, installation state, operation state, and step confirmation. Database uniqueness enforces immutable version identity and scoped idempotency; deterministic plan identity makes retries auditable.
- Commands and handlers own publication and installation mutations. Queries expose safe version, installation, and operation status read models. Failure mapping is stable and non-disclosing across invalid package, trust, compatibility, conflict, authorization, and recoverable operation failures.
- Solutions Application references `Axis.Authorization.Contracts`, `Axis.BusinessObjects.Contracts`, and `Axis.Rules.Contracts` only. Each contract exposes typed validate/plan, idempotent apply, and canonical read-back for its component kinds; Solutions Infrastructure and API compose adapters without giving Solutions access to another module's Application, Domain, Infrastructure, or database.
- Every required publication, trust-classification, installation lifecycle, and operation transition writes a versioned redacted audit-outbox envelope in the same Solutions transaction as its state change. Audit delivery/retry and retention follow the Audit boundary; package bytes, signatures, secrets, and component bodies are excluded from audit payloads.
- Solutions domain events are integration facts, not event sourcing. Event sourcing, replay, package-body rebuild from events, and cross-module transactional events are out of scope.

## Explicit exclusions

- Wave 1 has no version upgrade, rollback, uninstall, marketplace, overlay, workspace trust policy, package dependency graph, product-data migration, drift detection, promotion, or trusted-publisher mutation surface.
- First module adapters are Authorization policy, Business Object definition, and Rule definition/binding. Each remains the owner of its typed schema and resulting module state.
- The Wave 0 reference-product provisioning path is retired only by its owning implementation and external-product evidence in a clean cutover; Solutions provides no compatibility shim or dual path.
