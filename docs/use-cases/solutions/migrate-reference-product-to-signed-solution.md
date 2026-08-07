# Migrate The Reference Product To A Signed Solution

> **Navigation**: [docs/use-cases/solutions/README.md](./README.md) · [docs/architecture/solutions.md](../../architecture/solutions.md) · [docs/architecture/solution-package-v1.md](../../architecture/solution-package-v1.md) · [AGENTS.md](../../../AGENTS.md)

> **Contract status:** Ready for implementation. All implementation layers and acceptance evidence are not started.

## Purpose

Replace the Wave 0 external reference-product manifest/provisioner with one signed Wave 1 solution publish/install journey, then retire every Wave 0 reference-product provisioning surface in the same release.

## Primary actor

- Authenticated Workspace Administrator releasing and installing the reference product

## Preconditions

- The external reference-product repository contains a v1 signed package built from its source-owned reference product, using the public Axis contracts.
- A trusted active publisher key is deployment-configured, and a blank authorized Workspace is available.

## Trigger

- The reference product is ready to prove the Wave 1 signed release lifecycle.

## Success guarantee

- A blank Workspace publishes and installs the signed reference solution, completes its browser journey, and Axis/reference-product source contains only the signed lifecycle path.

## Minimal guarantee

- A failed publish, install, read-back, browser journey, or retirement sweep does not claim cutover success and leaves the prior product evidence inspectable until the one clean replacement is ready.

## Main flow

1. The product build emits the deterministic v1 package from source-owned reference-product definitions, typed Business Object/Rule-binding/Authorization components, provenance, exact Axis OpenAPI digest, and DSSE signature.
2. A Workspace Administrator publishes the exact package through the publish contract and reads back its safe immutable release projection.
3. The administrator installs the release into a blank Workspace and reads back every confirmed component and operation state.
4. Product actors complete the externally owned authenticated browser journey against installed content.
5. The implementation deletes the Wave 0 manifest/provisioner and all Axis use-case/evidence links that describe it, then sweeps both repositories for retired identifiers.
6. The signed publish/install/browser checkpoint and clean retirement evidence become the sole reference-product lifecycle proof.

## Alternate / error flows

- Invalid signature/trust/package, incomplete operation, component mismatch, browser failure, or noncompliant publisher blocks cutover and preserves diagnostic state; it never enables a dual provisioning path.
- Any remaining Wave 0 manifest, provisioner, documentation/evidence link, test-only setup, compatibility shim, or live caller fails the retirement sweep.
- A reference-product behavior not representable by the v1 typed components is a blocking product/architecture decision, not a product-specific Axis fallback.

## Acceptance Criteria

- **AC-001** The external reference product builds one deterministic signed v1 package from its source-owned component documents, with no Axis product code or module-internal dependency.
- **AC-002** A blank Workspace administrator publishes and installs that package through the public signed lifecycle, with safe read-back and the required audit outcomes.
- **AC-003** The installed reference product completes its authenticated browser journey through installed contracts, including recovery evidence owned by the product journey.
- **AC-004** The same release deletes the Wave 0 external manifest/provisioner, the Wave 0 Axis provision-reference-solution use case/evidence links, callers, tests, and compatibility behavior; no dual path, shim, flag, or fallback remains.
- **AC-005** Invalid package/trust/install/browser/retirement evidence blocks the cutover rather than retaining Wave 0 as a supported escape path.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Application boundary | External product source produces deterministic typed v1 package bytes and valid DSSE/provenance from public contracts | AC-001 | Application test | Yes |
| AT-002 | API/Application boundaries | Blank Workspace publish/install read-back confirms the signed package, plan, component provenance, and audits | AC-002 | API integration test + Application test | Yes |
| AT-003 | Browser journey | External authenticated reference-product browser journey completes using installed content and exposes recovery | AC-003 | Browser automation | Yes |
| AT-004 | Application boundary | Two-repository retirement sweep proves Wave 0 manifest/provisioner/use-case/evidence links and compatibility paths are absent | AC-004, AC-005 | Architecture test | Yes |

## Out Of Scope

- Upgrade, rollback, uninstall, marketplace, overlays, workspace trust, package dependencies, data migration, drift repair, and any compatibility overlap.

## Screen flow

The publish/install administrator screens follow their owning contracts. The reference product owns its post-install browser journey and accessibility evidence; Axis owns no product-specific UI.

> **Implementation status**
>
> | Layer | Status |
> |---|---|
> | External reference product | Not started |
> | Solutions Domain | Not started |
> | Solutions Application | Not started |
> | Solutions Infrastructure | Not started |
> | API | Not started |
> | Frontend | Not started |
> | MCP | Not started |
> | Audit | Not started |
>
> **Gaps vs spec:**
>
> | ID | Gap |
> |---|---|
> | GAP-001 | Signed reference-product build, lifecycle implementation, browser checkpoint, and retirement sweep are not started. |
>
> **Deferred follow-ups:** Only the explicitly out-of-scope lifecycle capabilities above.
>
> **Verification:** Not run; implementation evidence does not exist yet.
>
> **Decisions:** This use case owns the one clean Wave 0-to-Wave 1 replacement. [docs/architecture/solution-package-v1.md](../../architecture/solution-package-v1.md) owns package bytes and [docs/architecture/solutions.md](../../architecture/solutions.md) owns lifecycle realization.
