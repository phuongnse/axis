# Install A Solution Version

> **Navigation**: [docs/use-cases/solutions/README.md](./README.md) · [docs/architecture/solutions.md](../../architecture/solutions.md) · [docs/PLATFORM_STRATEGY.md](../../PLATFORM_STRATEGY.md) · [AGENTS.md](../../../AGENTS.md)

> **Contract status:** Ready for implementation. Implementation layers and acceptance evidence are not started.

## Purpose

Allow a current-Workspace Administrator to install one already published trusted solution version through a durable, resumable operation that applies each typed component safely and reports its exact outcome.

## Primary actor

- Authenticated current-Workspace Administrator installing a published solution version

## Preconditions

- The administrator can access the current Workspace.
- The selected immutable solution version exists, is compatible with Axis, and its publisher/key remains trusted and non-revoked.
- The current Workspace has no installed version for that solution identity in this Wave 1 scope.

## Trigger

- An administrator chooses a published solution version for the current Workspace.

## Success guarantee

- The selected exact version is pinned to the current Workspace only after every deterministic plan entry is confirmed by module-owned apply and matching read-back; the administrator can inspect the durable operation and installation status.

## Minimal guarantee

- Failure, interruption, conflict, unavailable dependency, race, or publisher revocation never reports a successful installation or advances to an unverified component. Confirmed component content remains inspectable and a recoverable operation exposes precise next action.

## Main flow

1. The administrator opens the Solutions install experience, selects a published version, and Axis returns only its safe release details.
2. Axis revalidates current Workspace authority, compatibility, and publisher trust, then derives the deterministic ordered component plan through typed module Contracts adapters.
3. The experience shows the target Workspace, pinned version, ordered plan, component types, and the install consequence before explicit confirmation.
4. Confirmation creates or returns the scoped-idempotent durable installation operation.
5. Axis applies one pending component at a time through its owning adapter. An entry is confirmed only after idempotent apply and matching exact-hash read-back.
6. When every entry is confirmed, Axis marks the pinned installation installed, records the outcome, and the experience reads back the installation and operation result.
7. After an interruption or recoverable failure, an administrator resumes the same operation. Axis performs fresh trust and operation checks, preserves only previously matching confirmed entries, and continues the remaining deterministic plan.

## Alternate / error flows

- Missing authority, missing Workspace context, cross-Workspace lookup, unavailable or incompatible version, or untrusted/revoked publisher rejects without mutation or disclosure.
- Invalid typed component content or an adapter validation failure blocks before that component mutation and exposes component-local safe diagnostics.
- Adapter mutation, read-back mismatch, infrastructure failure, or client response loss records the durable completed/pending state and reports an incomplete operation; a resume is available only when its preconditions remain satisfied.
- Concurrent confirms/resumes of the same operation are single-flight. A second caller observes the canonical in-progress or terminal operation and cannot execute a duplicate plan.
- Publisher revocation before a next mutation halts the operation, marks the installation `Noncompliant`, audits that classification, and leaves already installed content usable. A revoked publisher cannot be used to begin or resume installation.
- A request to install another version of an already installed solution, upgrade, rollback, uninstall, or repair drift is unsupported in Wave 1 and makes no mutation.

## Acceptance Criteria

*Happy path*

- **AC-001** A current-Workspace Administrator can create a scoped-idempotent installation operation only for an existing compatible immutable version whose publisher/key is trusted and non-revoked at installation time.
- **AC-002** The installation pins the exact selected version and creates a durable operation with a deterministic topological plan over the declared typed component dependency DAG.
- **AC-003** Solutions applies every component solely through its owning public Contracts adapter; the first adapter set is Authorization policy, Business Object definition, and Rule definition/binding.
- **AC-004** Every plan entry is idempotently applied and read back through its adapter; it becomes confirmed only if the read-back matches the declared exact component hash.
- **AC-005** After all plan entries are confirmed, safe read-back reports the current Workspace, pinned version, installation state, ordered component outcomes, and durable operation status.
- **AC-006** An interrupted or failed operation is resumable with fresh trust checks and deterministic pending work; matching confirmed entries are not duplicated.

*Failure, recovery, and concurrency*

- **AC-007** There is no cross-module distributed transaction. A component failure, adapter unavailability, read-back mismatch, or lost client response records exact completed/pending operation state and never reports installation success from a write response alone.
- **AC-008** Concurrent resume or confirmation calls for one operation are single-flight, so no component plan entry is concurrently applied twice; callers receive the canonical in-progress or terminal state.
- **AC-009** A publisher/key that is revoked before an installation starts or resumes blocks it before mutation. Revocation discovered before a next mutation halts the operation, preserves usable confirmed content, marks the installation `Noncompliant`, and records the required audit outcome.
- **AC-010** Authorization, trust, compatibility, component, concurrency, and recovery outcomes are Workspace-isolated and expose no raw package bytes, signature material, secrets, or other Workspace data.

*Boundaries*

- **AC-011** Installing another version for the same solution, upgrade, rollback, uninstall, marketplace, overlays, workspace trust, package dependency graph, product data migrations, and drift repair are out of scope and have no hidden mutation path.
- **AC-012** Solutions orchestrates but does not write any consuming module store or interpret its business semantics; adapters validate and apply typed module-owned component documents.
- **AC-013** Existing Wave 0 provisioning remains untouched until its owning clean-cutover implementation and external-product evidence are ready; no compatibility shim or dual install path is introduced.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Application boundary | Current-Workspace authority, compatible trusted version revalidation, scoped idempotency, immutable version pinning, deterministic topological plan, and safe rejection paths | AC-001, AC-002, AC-010 | Application test | Yes |
| AT-002 | Application boundary | Each first-wave adapter validates and applies only its typed document; Solutions reaches no module internals or stores | AC-003, AC-012 | Application test | Yes |
| AT-003 | Application/Infrastructure boundaries | Apply plus exact-hash read-back confirms each step; injected apply/read-back/interruption failure records completed/pending state and resumes without duplicate matching work | AC-004, AC-005, AC-006, AC-007 | Application test + Infrastructure integration test | Yes |
| AT-004 | Infrastructure boundary | Migration-backed operation, installation, step, idempotency, revision, and audit-outbox persistence survives response loss and enforces one single-flight execution | AC-005, AC-007, AC-008 | Infrastructure integration test | Yes |
| AT-005 | API/Application boundaries | Revocation before begin/resume blocks before mutation; revocation between steps halts before the next mutation, marks `Noncompliant`, audits it, and leaves confirmed content usable | AC-001, AC-009, AC-010 | Application test + API integration test | Yes |
| AT-006 | API boundary | Authenticated Workspace-isolated install, status, and resume operations preserve idempotency/error semantics and generated contract parity without raw package exposure | AC-001, AC-005, AC-006, AC-010 | API integration test | Yes |
| AT-007 | Browser journey | Administrator can inspect the deterministic plan, confirm, observe progress/results, recover from failed/interrupted/noncompliant states, and resume with keyboard, screen-reader, compact, and desktop support | AC-002, AC-005, AC-006, AC-007, AC-009 | UI component test + Browser automation | Yes |
| AT-008 | API boundary | No mutation path implements another-version install, upgrade, rollback, uninstall, marketplace, overlays, workspace trust, package dependencies, data migration, drift repair, or Wave 0 compatibility | AC-011, AC-013 | Architecture test + API integration test | Yes |

## Out Of Scope

- Installing another version of the same solution, upgrades, rollback, uninstall, marketplace, overlays, workspace trust, package dependencies, product data migrations, drift detection/repair, promotion, and automatic rollback.
- Module data migration, module-store access by Solutions, opaque component support, and any cross-module distributed transaction.
- Wave 0 provision-reference-solution replacement or compatibility behavior.

## Screen flow

| Surface | Required contract |
|---|---|
| Version selection | Shows only safe release identity/provenance/trust details and the target Workspace; unavailable, incompatible, untrusted, and revoked releases have no install action. |
| Install preflight | Leads with the pinned version and Workspace, then presents the ordered component plan and explicit confirmation consequence. |
| Operation progress | Shows current, confirmed, and pending entries as a sequence; progress is announced without focus theft and can be revisited after response loss. |
| Result | Separates Installed, incomplete/recoverable, and `Noncompliant` outcomes. It exposes ordered safe component outcomes and the next permitted action, not raw package or signature material. |
| Resume | Shows the existing operation identity and completed/pending entries before a resume action. A revoked publisher, unavailable dependency, or already-running operation explains why resume is unavailable. |

Required UI quality: identity, plan, state, and recovery information are programmatically labelled; confirmation and resume have deliberate focus behavior; every progress/result state is keyboard and screen-reader inspectable; long component identities/hashes remain readable without compact-layout overflow; universal UI copy is localized; no raw package, signature material, secret, or cross-Workspace data is rendered.

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
> **Decisions:** [docs/architecture/solutions.md](../../architecture/solutions.md) owns operation durability, adapter boundaries, concurrency, trust revalidation, persistence, and audit-outbox realization.
