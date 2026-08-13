# Install A Solution Version

> **Navigation**: [docs/use-cases/solutions/README.md](./README.md) · [docs/architecture/solutions.md](../../architecture/solutions.md) · [docs/PLATFORM_STRATEGY.md](../../PLATFORM_STRATEGY.md) · [AGENTS.md](../../../AGENTS.md)

> **Contract status:** Implementation and scoped acceptance evidence are complete; authentic external release proof remains owned by Validate The Reference Product Solution Lifecycle.

## Purpose

Allow a current-Workspace lifecycle administrator to install one already published trusted solution version through a durable, resumable operation that applies each typed component safely and reports its exact outcome.

## Primary actor

- Authenticated current-Workspace lifecycle administrator installing a published solution version

## Preconditions

- The administrator is the active `Owner` of the current personal Workspace or an active `Administrator` of the current organization Workspace.
- The selected immutable solution version exists, targets the exact current committed Axis OpenAPI digest, and its publisher/key remains trusted and non-revoked.
- The current Workspace has no installed version for that solution identity under the current installation contract.

## Trigger

- An administrator chooses a published solution version for the current Workspace.

## Success guarantee

- The selected exact version is pinned to the current Workspace only after every deterministic plan entry is confirmed by module-owned apply and matching read-back; the administrator can inspect the durable operation and installation status.

## Minimal guarantee

- Failure, interruption, conflict, unavailable dependency, race, or publisher revocation never reports a successful installation or advances to an unverified component. Confirmed component content remains inspectable and a recoverable operation exposes precise next action.

## Main flow

1. The administrator opens the Solutions install experience, selects a published version, and Axis returns only its safe release details.
2. Axis revalidates current Workspace authority, the exact current committed Axis OpenAPI digest, and publisher trust, then derives the deterministic ordered component plan through typed module Contracts adapters.
3. The experience shows the target Workspace, pinned version, ordered plan, component types, and the install consequence before explicit confirmation.
4. Confirmation creates or returns the scoped-idempotent durable installation operation.
5. Axis applies one pending component at a time through its owning adapter. An entry is confirmed only after idempotent apply and matching exact-hash read-back.
6. When every entry is confirmed, Axis marks the pinned installation installed, records the outcome, and the experience reads back the installation and operation result.
7. After an interruption or recoverable failure, an administrator resumes the same operation. Axis performs fresh trust and operation checks, preserves only previously matching confirmed entries, and continues the remaining deterministic plan.

## Alternate / error flows

- Missing authority, missing Workspace context, cross-Workspace lookup, unavailable or OpenAPI-digest-mismatched version, or untrusted/revoked publisher rejects without mutation or disclosure.
- Invalid typed component content or an adapter validation failure blocks before that component mutation and exposes component-local safe diagnostics.
- Adapter mutation, read-back mismatch, infrastructure failure, or client response loss records the durable completed/pending state and reports an incomplete operation; a resume is available only when its preconditions remain satisfied.
- Concurrent confirms/resumes observe serialized canonical state. Lease fencing prevents a stale worker from producing a duplicate confirmed effect, while making no claim that attempted adapter calls cannot overlap.
- Publisher revocation before a next mutation halts the operation, marks the installation `Noncompliant`, audits that classification, and leaves already installed content usable. A revoked publisher cannot be used to begin or resume installation.
- A request to install another version of an already installed solution, upgrade, rollback, uninstall, or repair drift is unsupported and makes no mutation.

## Acceptance Criteria

*Happy path*

- **AC-001** The active `Owner` of a current personal Workspace or an active `Administrator` of a current organization Workspace can create a scoped-idempotent installation operation only for an existing compatible immutable version whose publisher/key is trusted and non-revoked at installation time.
- **AC-002** The installation pins the exact selected version and creates a durable operation with a deterministic topological plan over the declared typed component dependency DAG.
- **AC-003** Solutions applies every component solely through its owning public Contracts adapter; the first adapter set is Authorization policy, Business Object definition, and Rule binding to an existing immutable built-in Rule definition.
- **AC-004** Every plan entry is idempotently applied and read back through its adapter; it becomes confirmed only if the read-back matches the declared exact component hash.
- **AC-005** After all plan entries are confirmed, safe read-back reports the current Workspace, pinned version, installation state, ordered component outcomes, and durable operation status.
- **AC-006** An interrupted or failed operation is resumable with fresh trust checks and deterministic pending work; matching confirmed entries are not duplicated.

*Failure, recovery, and concurrency*

- **AC-007** There is no cross-module distributed transaction. A component failure, adapter unavailability, read-back mismatch, or lost client response records exact completed/pending operation state and never reports installation success from a write response alone.
- **AC-008** Concurrent resume or confirmation calls observe serialized canonical operation transitions. Lease epochs fence stale workers so no duplicate confirmed effect is recorded, while attempted adapter calls may overlap and remain safe through adapter receipts, idempotency, and read-back.
- **AC-009** A publisher/key that is revoked before an installation starts or resumes blocks it before mutation. Revocation discovered before a next mutation halts the operation, preserves usable confirmed content, marks the installation `Noncompliant`, and records the required audit outcome.
- **AC-010** Authorization, trust, compatibility, component, concurrency, and recovery outcomes are Workspace-isolated and expose no raw package bytes, signature material, secrets, or other Workspace data.
- **AC-011** Before confirmation or any adapter apply, every component is validated/planned through its owning adapter and the whole deterministic plan/hashes persist successfully; an invalid final component produces zero module mutations.
- **AC-012** Installed component provenance is persisted by the owning adapter. Ordinary mutation paths reject an installed definition, policy, or binding identity, including update, disable, archive, and delete of installed Rule bindings; manually created module resources retain their current lifecycle.
- **AC-013** A database lease epoch is included in every adapter receipt `(operationId, stepId, componentHash, leaseEpoch)` and stale workers are rejected. Expiry atomically reclaims the expired epoch's `Applying` step to `Pending`; the next holder must read back before any reapply. This prevents duplicate confirmed effects but makes no impossible claim that attempts cannot overlap.
- **AC-014** Installation, operation, and step use the explicit provisioning, compliance, operation, and step states/transitions in [docs/architecture/solutions.md](../../architecture/solutions.md#states-and-mcp-boundary). An installed `Noncompliant` version remains active; a revoked partial installation is retained `Failed`/`Noncompliant` and is not activated.
- **AC-015** Publisher-ledger reconciliation marks affected installations noncompliant and audits that outcome. Missing/revoked/substituted keys block new and resumed mutations while confirmed installed content remains active.
- **AC-016** Typed MCP list/status/install/resume tools preserve current-Workspace authority, idempotency, safe projections, and the same trust/recovery semantics; raw package bytes and publisher mutation remain unavailable.

*Boundaries*

- **AC-017** Installing another version for the same solution, upgrade, rollback, uninstall, marketplace, overlays, workspace trust, package dependency graph, product data migrations, and drift repair are out of scope and have no hidden mutation path.
- **AC-018** Solutions orchestrates but does not write any consuming module store or interpret its business semantics; adapters validate and apply typed module-owned component documents.
- **AC-019** This durable operation is the only installation path; no compatibility, fallback, flag, or alternate install path exists.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Application boundary | Personal `Owner` and organization `Administrator` current-Workspace authority, organization `Member` denial, compatible trusted version revalidation, scoped idempotency, immutable version pinning, deterministic topological plan, and safe rejection paths | AC-001, AC-002, AC-010 | Domain test + Application test | Yes |
| AT-002 | Application boundary | Every required adapter validates/plans its typed document before confirmation; invalid last component proves zero module mutations and Solutions reaches no module internals/stores | AC-003, AC-011, AC-018 | Application test | Yes |
| AT-003 | Application/Infrastructure boundaries | Apply plus exact-hash read-back/provenance confirms each step; injected apply/read-back/interruption failure records state and resumes without duplicate confirmed effects | AC-004, AC-005, AC-006, AC-007, AC-012, AC-013 | Application test + Infrastructure integration test | Yes |
| AT-004 | Infrastructure boundary | Migration-backed operation, installation, step, idempotency, ledger/reconciliation, revision, fencing receipt, state transitions, and audit-outbox persistence survive response loss; lease expiry atomically reclaims `Running`/`Applying` to `Pending`, then read-back confirms, reapplies missing content, or blocks mismatch | AC-005, AC-007, AC-008, AC-013, AC-014, AC-015 | Infrastructure integration test | Yes |
| AT-005 | API/Application boundaries | Revocation before begin/resume blocks before mutation; revocation between steps halts before next mutation, audits/marks noncompliant, and leaves confirmed active content usable | AC-001, AC-009, AC-010, AC-014, AC-015 | Application test + API integration test | Yes |
| AT-006 | API/MCP boundaries | Authenticated Workspace-isolated install/status/resume and typed MCP tools preserve idempotency/error semantics without raw package exposure | AC-001, AC-005, AC-006, AC-010, AC-016 | API integration test + MCP contract test | Yes |
| AT-007 | Browser journey | Administrator can inspect plan, confirm, observe progress/results, recover from failed/interrupted/noncompliant states, and resume accessibly | AC-002, AC-005, AC-006, AC-007, AC-009, AC-014 | UI component test + Browser automation | Yes |
| AT-008 | API boundary | No mutation path implements another-version install, upgrade, rollback, uninstall, marketplace, overlays, workspace trust, package dependencies, data migration, drift repair, or an alternate installation contract | AC-017, AC-019 | Architecture test + API integration test | Yes |

## Out Of Scope

- Installing another version of the same solution, upgrades, rollback, uninstall, marketplace, overlays, workspace trust, package dependencies, product data migrations, drift detection/repair, promotion, and automatic rollback.
- Module data migration, module-store access by Solutions, opaque component support, and any cross-module distributed transaction.

## Screen flow

| Surface | Required contract |
|---|---|
| Collection selection | One primary solution-version table combines immutable release identity with current-Workspace provisioning, compliance, and operation state. Search state is shareable; selecting a release or an existing installation opens a stable task while preserving collection context. |
| Version selection | The release task shows only safe identity/provenance/trust details and the target Workspace; unavailable, incompatible, untrusted, and revoked releases have no install action. |
| Install preflight | The release task leads with the pinned version and Workspace, then presents the ordered component plan and explicit confirmation consequence. |
| Operation progress | The installation task shows current, confirmed, and pending entries as an ordered sequence; progress is announced without focus theft and can be revisited from the installation collection after response loss. |
| Result | Separates Installed, incomplete/recoverable, and `Noncompliant` outcomes. It exposes ordered safe component outcomes and the next permitted action, not raw package or signature material. |
| Resume | Shows the existing operation identity and completed/pending entries before a resume action. A revoked publisher, unavailable dependency, or already-running operation explains why resume is unavailable. |

Required UI quality: the collection follows the [Collection Page](../../foundations/data-display/collection-page.md) contract and release/installation work follows the [Managed Dialog](../../foundations/overlays/managed-dialog.md) task contract; identity, plan, state, and recovery information are programmatically labelled; confirmation and resume have deliberate focus behavior; every progress/result state is keyboard and screen-reader inspectable; long component identities/hashes remain readable without compact-layout overflow; universal UI copy is localized; no raw package, signature material, secret, or cross-Workspace data is rendered.

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
> | Architecture evidence | Done |
>
> **Gaps vs spec:** None.
>
> **Deferred follow-ups:** The explicitly out-of-scope lifecycle capabilities above require their own use cases.
>
> **Verification:** Every required AT is mapped to current Application, PostgreSQL infrastructure, API, Architecture, MCP contract, frontend component, and focused browser evidence in [install-solution-version.evidence.md](./install-solution-version.evidence.md). Authentic reference-product release/provenance validation remains separately owned by [Validate Reference Product Lifecycle](./validate-reference-product-lifecycle.md).
>
> **Decisions:** [docs/architecture/solutions.md](../../architecture/solutions.md) owns operation durability, adapter boundaries, concurrency, trust revalidation, persistence, and audit-outbox realization.
