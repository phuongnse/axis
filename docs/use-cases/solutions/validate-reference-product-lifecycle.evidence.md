# Validate The Reference Product Solution Lifecycle Evidence

> **Navigation**: [docs/use-cases/solutions/validate-reference-product-lifecycle.md](./validate-reference-product-lifecycle.md) · [docs/use-cases/solutions/README.md](./README.md) · [docs/architecture/solutions.md](../../architecture/solutions.md) · [docs/architecture/solution-package-v1.md](../../architecture/solution-package-v1.md) · [AGENTS.md](../../../AGENTS.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `external://axis-reference-product@b8568d9ded047f497ac62d4df56ac682fc0b33f8/scripts/build-solution.test.mjs`, `external://axis-reference-product@b8568d9ded047f497ac62d4df56ac682fc0b33f8/scripts/local-dev.test.mjs` | `external://axis-reference-product@b8568d9ded047f497ac62d4df56ac682fc0b33f8/npm run test:unit` |
| AT-002 | `external://axis-reference-product@b8568d9ded047f497ac62d4df56ac682fc0b33f8/scripts/build-solution.test.mjs`, `external://axis-reference-product@b8568d9ded047f497ac62d4df56ac682fc0b33f8/tests/product.pw.ts` | `external://axis-reference-product@b8568d9ded047f497ac62d4df56ac682fc0b33f8/npm run test:unit`; `external://axis-reference-product@b8568d9ded047f497ac62d4df56ac682fc0b33f8/npm run test:e2e -- -- -g "administrator installs the signed release before the applicant submits through the product BFF"` |
| AT-003 | `external://axis-reference-product@b8568d9ded047f497ac62d4df56ac682fc0b33f8/tests/product.pw.ts` | `external://axis-reference-product@b8568d9ded047f497ac62d4df56ac682fc0b33f8/npm run test:e2e -- -- -g "administrator installs the signed release before the applicant submits through the product BFF"` |
| AT-004 | `tests/Architecture/Axis.Architecture.Tests/ReferenceProductBoundaryTests.cs` | `python scripts/axis.py dotnet test tests/Architecture/Axis.Architecture.Tests/Axis.Architecture.Tests.csproj --filter FullyQualifiedName~ReferenceProductBoundaryTests` |

## Immutable Checkpoint

- External source commit: `b8568d9ded047f497ac62d4df56ac682fc0b33f8`.
- Stable publication intent: `reference_application` version `0.1.0`.
- Signed development solution identity: `reference_application` version `0.1.0-dev.gb8568d9ded04`; build ID `reference-product-0.1.0-dev.gb8568d9ded04`; publisher/key `axis_reference_product/release`.
- Signed payload `sourceRevision`: `b8568d9ded047f497ac62d4df56ac682fc0b33f8`.
- Axis OpenAPI SHA-256 in the signed payload, external source, and Axis configuration: `5ca3b6e62c1950ed3a1524f04f4e38226a187f7e0119ec9d5ac9a101b7135548`.
- The ignored private signing key was preserved at mode `0600`; it was not rotated or written to repository evidence. Stable version selection, development snapshot identity, and clean cutover remain independent lifecycle decisions.

## Current Verification

- The external `test:unit` workflow passes frontend 9/9 and Node contract/workflow tests 29/29; the production frontend build passes.
- The explicitly approved clean cutover removes the prior local volumes once, recreates the current initial schemas, and brings every product-owned service to healthy without changing stable publication intent.
- The focused external `test:e2e` workflow passes 1/1 against a newly created Organization Workspace. The administrator publishes and installs signed development snapshot `0.1.0-dev.gb8568d9ded04`, assigns Applicant, completes the authenticated record journey through the BFF, revokes the exact role, and signs out without exposing OAuth artifacts.
- Axis `ReferenceProductBoundaryTests` passes 1/1, and the external architecture checks pass 2/2. Together they reject product identifiers or provisioning paths in Axis production, module-internal Axis dependencies in the product, and any browser lifecycle path outside the public Solutions surface.
