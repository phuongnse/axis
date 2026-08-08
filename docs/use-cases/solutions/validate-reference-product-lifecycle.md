# Validate The Reference Product Solution Lifecycle

> **Navigation**: [docs/use-cases/solutions/README.md](./README.md) · [docs/architecture/solutions.md](../../architecture/solutions.md) · [docs/architecture/solution-package-v1.md](../../architecture/solution-package-v1.md) · [AGENTS.md](../../../AGENTS.md)

> **Contract status:** Axis and reference-product implementation and runtime proof are complete at the current two-repository checkpoint.

## Purpose

Continuously prove that an independently owned product can build, publish, install, and operate through public Axis contracts without product-specific behavior in the Axis repository.

## Primary actor

- Authenticated Workspace Administrator releasing and installing the reference product

## Preconditions

- The external reference-product repository produces a signed solution from its source-owned product definition using public Axis contracts.
- A trusted active publisher key is deployment-configured, and a blank authorized Workspace is available.

## Trigger

- The reference product is ready to validate the supported solution lifecycle against the current Axis contract.

## Success guarantee

- A blank Workspace publishes and installs an immutable signed reference release, and product actors complete its authenticated browser journey using only supported public contracts.

## Minimal guarantee

- A failed build, publish, install, read-back, or browser journey does not claim lifecycle success and preserves safe diagnostic state.

## Main flow

1. The product build emits deterministic v1 component and payload bytes from source-owned product definitions, typed Business Object, Rule-binding, and Authorization components, provenance, and the exact Axis OpenAPI digest, then signs those exact payload bytes in a valid DSSE envelope.
2. A Workspace Administrator publishes the exact package and reads back its safe immutable release projection.
3. The administrator installs the release into a blank Workspace and reads back every confirmed component and operation state.
4. Product actors complete the externally owned authenticated browser journey against installed content.
5. Architecture verification confirms that Axis contains no reference-product-specific key, route, seed, provisioning behavior, fallback, or module-internal consumer contract.
6. The build, lifecycle, browser, and architecture checkpoints become the reference-product proof for the current public contract.

## Alternate / error flows

- Invalid signature, trust, package, incomplete operation, component mismatch, browser failure, or noncompliant publisher blocks success and preserves diagnostic state.
- Any reference-product-specific Axis behavior, alternate provisioning path, compatibility shim, test-only setup, or module-internal dependency fails the architecture boundary.
- A product behavior not representable by the supported typed components is a blocking product or architecture decision, not a product-specific Axis fallback.

## Acceptance Criteria

- **AC-001** The external reference product builds deterministic canonical v1 component and payload bytes from source-owned component documents and places those exact bytes in a valid signed DSSE envelope, with no Axis product code or module-internal dependency.
- **AC-002** A blank Workspace administrator publishes and installs that package through the public signed lifecycle, with safe read-back and the required audit outcomes.
- **AC-003** The installed reference product completes its authenticated browser journey through installed contracts, including recovery evidence owned by the product journey.
- **AC-004** Axis contains no reference-product-specific key, route, seed, provisioning behavior, fallback, compatibility path, or module-internal consumer contract.
- **AC-005** Invalid build, package, trust, install, browser, or architecture evidence blocks the lifecycle claim.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Application boundary | External product source produces deterministic typed component and payload bytes plus a valid DSSE envelope and provenance from public contracts | AC-001 | Application test | Yes |
| AT-002 | API/Application boundaries | Blank Workspace publish and install read-back confirms the signed package, plan, component provenance, and audits | AC-002 | API integration test + Application test | Yes |
| AT-003 | Browser journey | External authenticated reference-product browser journey completes using installed content and exposes recovery | AC-003 | Browser automation | Yes |
| AT-004 | Architecture boundary | Both repositories contain only public lifecycle integration and no product-specific Axis behavior or alternate provisioning path | AC-004, AC-005 | Architecture test | Yes |

## Out Of Scope

- Upgrade, rollback, uninstall, marketplace, overlays, workspace trust, package dependencies, data migration, drift repair, and product authoring.

## Screen flow

The publish and install administrator screens follow their owning contracts. The reference product owns its post-install browser journey and accessibility evidence; Axis owns no product-specific UI.

> **Implementation status**
>
> | Layer | Status |
> |---|---|
> | External reference product | Done |
> | Solutions Domain | Done |
> | Solutions Application | Done |
> | Solutions Infrastructure | Done |
> | API | Done |
> | Frontend | Done |
> | MCP | Done |
> | Audit | Done |
>
> **Gaps vs spec:** None.
>
> **Deferred follow-ups:** Only the explicitly out-of-scope lifecycle capabilities above.
>
> **Verification:** [Validate The Reference Product Solution Lifecycle evidence](./validate-reference-product-lifecycle.evidence.md) records the exact signed draft release, source revision, Axis OpenAPI digest, blank-database publish/install journey, product BFF browser flow, and Axis architecture/verification gates.
>
> **Decisions:** This use case owns recurring proof that the external reference product consumes the same supported solution lifecycle as any customer product. [docs/architecture/solution-package-v1.md](../../architecture/solution-package-v1.md) owns package bytes and [docs/architecture/solutions.md](../../architecture/solutions.md) owns lifecycle realization.
