# Validate The Reference Product Solution Lifecycle Evidence

> **Navigation**: [docs/use-cases/solutions/validate-reference-product-lifecycle.md](./validate-reference-product-lifecycle.md) · [docs/use-cases/solutions/README.md](./README.md) · [docs/architecture/solutions.md](../../architecture/solutions.md) · [docs/architecture/solution-package-v1.md](../../architecture/solution-package-v1.md) · [AGENTS.md](../../../AGENTS.md)

## Acceptance Evidence

| AT ID | Evidence | Commands |
|---|---|---|
| AT-001 | `external://axis-reference-product@217cbc6a072977a250b013eecdd9997c3e62266e/scripts/build-solution.test.mjs` | `external://axis-reference-product@217cbc6a072977a250b013eecdd9997c3e62266e/npm run test:unit` |
| AT-002 | `external://axis-reference-product@217cbc6a072977a250b013eecdd9997c3e62266e/scripts/build-solution.test.mjs`, `external://axis-reference-product@217cbc6a072977a250b013eecdd9997c3e62266e/tests/product.pw.ts` | `external://axis-reference-product@217cbc6a072977a250b013eecdd9997c3e62266e/npm run test:unit`, `external://axis-reference-product@217cbc6a072977a250b013eecdd9997c3e62266e/npm run test:e2e` |
| AT-003 | `external://axis-reference-product@217cbc6a072977a250b013eecdd9997c3e62266e/tests/product.pw.ts` | `external://axis-reference-product@217cbc6a072977a250b013eecdd9997c3e62266e/npm run test:e2e` |
| AT-004 | `tests/Architecture/Axis.Architecture.Tests/ReferenceProductBoundaryTests.cs` | `python scripts/axis.py dotnet test tests/Architecture/Axis.Architecture.Tests/Axis.Architecture.Tests.csproj --filter FullyQualifiedName~ReferenceProductBoundaryTests` |

## Immutable Checkpoint

- External source commit: `217cbc6a072977a250b013eecdd9997c3e62266e`.
- Signed solution identity: `reference_application` version `0.1.2`; build ID `reference-product-0.1.2`; publisher/key `axis_reference_product/release`.
- Signed payload `sourceRevision`: `217cbc6a072977a250b013eecdd9997c3e62266e`.
- Axis OpenAPI SHA-256 in the signed payload, external source, and Axis configuration: `ee28c66776e404441c6458244824e50b1e2dc32c00407ea2746703cf9134a47f`.
- Preserved release `0.1.1` remains unchanged. The ignored private release key was reused at mode `0600`; it was not rotated or written to repository evidence.

## Current Verification

- The external `check` workflow passes package tests 23/23, production build, BFF tests 28/28, generated-client synchronization, and both architecture checks; `test:unit` passes frontend 9/9 and package tests 23/23.
- The external `test:e2e` workflow passes 1/1 against a newly created Organization Workspace. The administrator creates and revokes a service identity, publishes and installs signed release `0.1.2`, assigns Applicant, completes the authenticated record journey through the BFF, revokes the exact role, and signs out without exposing OAuth artifacts.
- Axis `ReferenceProductBoundaryTests` passes 1/1, and the external architecture checks pass 2/2. Together they reject product identifiers or provisioning paths in Axis production, module-internal Axis dependencies in the product, and any browser lifecycle path outside the public Solutions surface.
