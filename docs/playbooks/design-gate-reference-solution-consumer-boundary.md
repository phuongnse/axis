# Design Gate: Reference Solution Consumer Boundary

> **Navigation**: [docs/playbooks/design-gate.md](./design-gate.md) · [provision-reference-solution.md](../use-cases/solutions/provision-reference-solution.md) · [docs/PLATFORM_STRATEGY.md](../PLATFORM_STRATEGY.md) · [docs/README.md](../README.md)

## Risk and scope

This is a full Design Gate for Wave 0 of [docs/PLATFORM_STRATEGY.md](../PLATFORM_STRATEGY.md#delivery-sequence). The slice is high-risk because it creates a public product-source contract, changes first-party OAuth client registration, proves behavior across independently versioned source, and retires the current Axis-owned sample route and product experience.

The slice reuses current Business Objects and Rules mutation operations and adds one generic authenticated Rule Binding read operation needed for honest canonical read-back. It does not create a Solutions module, persistence model, install endpoint, batch transaction, signature model, workflow engine, or package-upgrade contract.

## Governing rules

- Product behavior follows [provision-reference-solution.md](../use-cases/solutions/provision-reference-solution.md), including AC-001 through AC-021 and AT-001 through AT-010.
- Platform/product ownership and Wave 0 exit proof follow [docs/PLATFORM_STRATEGY.md](../PLATFORM_STRATEGY.md#platform-and-product-boundary) and [docs/PLATFORM_STRATEGY.md](../PLATFORM_STRATEGY.md#delivery-sequence).
- Source priority, enterprise-production fitness, evidence-backed clean replacement, migrations, stack approval, and honest evidence follow [AGENTS.md](../../AGENTS.md#critical-rules).
- Current module, API, MCP, and Rules boundaries follow [docs/ARCHITECTURE.md](../ARCHITECTURE.md#boundary-rules) and [docs/ARCHITECTURE.md](../ARCHITECTURE.md#rules-boundary).
- REST/OpenAPI parity follows [docs/ENFORCEMENT.md](../ENFORCEMENT.md#ledger). Auth/client meaning, cross-repository evidence, clean-cutover completeness, and product separation remain semantic review obligations owned by this dossier and the use case.

## Blast radius

The pre-edit and review sweep covers:

```text
frontend/src/features/applications/
frontend/src/routes/_authenticated/applications.lazy.tsx
frontend/src/lib/module-navigation-registry.ts
frontend/src/lib/managed-window-registry.ts
frontend/src/features/preferences/translations.ts
frontend/src/routeTree.gen.ts
frontend/tests/applications-page.test.tsx
frontend/e2e/submit-business-object-record.pw.ts
src/Modules/Identity/Axis.Identity.Infrastructure/Services/OpenIddictSeeder.cs
src/Modules/Identity/Axis.Identity.Infrastructure/Extensions/
src/Axis.Api/Extensions/AxisApiServiceExtensions.cs
src/Axis.Api/appsettings*.json
docker-compose.yml
scripts/axis.py
scripts/tests/test_policy_gates.py
docs/playbooks/local-dev.md
docs/playbooks/scripts.md
src/Axis.Api/Endpoints/RuleBindingEndpoints.cs
src/Modules/Rules/Axis.Rules.Contracts/RuleBindingContracts.cs
src/Modules/Rules/Axis.Rules.Application/Queries/
src/Axis.Mcp/Tools/
tests/Api/Axis.Api.Tests/Identity/
tests/Modules/Identity/Axis.Identity.Infrastructure.Tests/
tests/Modules/BusinessObjects/
tests/Tools/Axis.Mcp.Tests/
openapi.json
docs/use-cases/business-objects/submit-business-object-record*
docs/use-cases/business-objects/define-business-object*
docs/use-cases/solutions/
independently versioned reference-solution source and evidence
```

## Product-source and wire decisions

- The reference solution owns its manifest, provisioning planner/client, product client, localized copy, routes, and acceptance journeys in independently versioned source. Axis never imports that source into a module or frontend feature.
- The Wave 0 manifest is a product-source schema, not an Axis REST DTO or persisted package. Its semantic identities, exact OpenAPI lock, canonical projection, generated-field exclusions, and duplicate handling are owned by [provision-reference-solution.md § Wave 0 manifest contract](../use-cases/solutions/provision-reference-solution.md#wave-0-manifest-contract); runtime database IDs come only from authenticated discovery/read-back.
- REST/OpenAPI remains canonical. Current Rule Binding usage reads omit input mappings and revision, so Wave 0 adds `GET /api/rule-bindings/{bindingId}` returning the existing full `RuleBindingDto`, authenticated and workspace-isolated with `401`, `403`, and cross-workspace/not-found `404` behavior. The product discovers IDs from the current exact-rule-version usage read, then uses this operation for canonical comparison. No product-specific query, Solutions install/batch/upgrade endpoint, or hidden setup API is added.
- Required `OpenIddict:PublicClientCatalog` owns deployment-configured public clients whose approved threat model requires PKCE-only browser or native behavior. It is valid for those client types but is not the final reference-product authentication boundary: the enterprise business application requires the separately approved BFF architecture, and its confidential client registration, credentials, session, and callback contract must be designed before auth implementation continues. Exact redirect/origin validation, fail-closed reconciliation, platform-owned configuration, and the prohibition on product identity in Axis source remain required; no public-browser result can substitute for the BFF acceptance boundary.
- Local deployment composition uses a generic, repeatable `python scripts/axis.py local-dev --compose-overlay <path> ...` command. Axis resolves every explicitly supplied overlay to a unique existing YAML file before Docker is invoked, applies the same ordered overlay set to the selected lifecycle/browser operation, and otherwise preserves the committed stack. `local-dev e2e --service <name>` may select a trusted overlay-owned verification service after validating its Compose service name; the wrapper still owns build/run, CA-enabled browser evidence remains in that service, and arbitrary shell execution is not exposed. Axis does not discover overlays, infer a sibling product, or import product identity. The independently versioned product owns its overlay and finite local-development command; production deployments provide the same catalog semantics through their deployment configuration. Reusing `axis_spa`, adding reference-product values to Axis source, raw Docker side paths, disabled TLS verification, and silently dropping the overlay during recreate/down are not permitted.
- The reference solution uses one separately governed verification command surface. Axis acceptance records immutable product-source revision plus its current manifest, component, API, and browser results; external green state is not inferred from Axis tests.

## Retirement and compatibility

No supported production consumer or production data requires compatibility for the named sample surfaces. Use a clean cutover with no overlap; this lifecycle fact does not lower the production quality of the replacement.

Retire `loan_application`, `sampleApplicationObjectKey`, `findSampleApplicationDefinition`, `provisionSampleApplication`, `ApplicationsPage`, `ApplicationRecordDialog`, `applicationRecordWindowDescriptor`, the `/applications` route, the `applications.*` product-copy namespace, standalone `Cors:AllowedOrigins`, hard-coded `SeedSpaClientAsync`/`SeedMcpClientAsync` descriptors, Axis-owned Applications component/browser tests, and product-specific examples in generic API/MCP tests. Preserve `axis_spa` and `axis_mcp` only as platform catalog values, preserve generic Business Object record operations, and rewrite generic tests with consumer-neutral examples.

In the same cutover, rewrite [submit-business-object-record.md](../use-cases/business-objects/submit-business-object-record.md) so its purpose, flows, AC-018, AT-008/AT-009, screen contract, status, decisions, and [evidence sidecar](../use-cases/business-objects/submit-business-object-record.evidence.md) describe only product-neutral Business Objects behavior and current Axis evidence. Move the Applications product journey and its component/browser evidence to the independently versioned reference solution. Reconcile [define-business-object.md](../use-cases/business-objects/define-business-object.md) and its evidence only if the consumer implementation proves its public derived-key contract insufficient; no unapproved object-key wire change is implied by this dossier.

The post-edit sweep must find no reference-solution identity, product route/copy/setup behavior, compatibility alias, feature flag, fallback, or duplicate product client in Axis source, tests, generated routes, OpenAPI descriptions, or docs.

## Verification plan

- Contract development: `python scripts/axis.py check use-case-docs`, `python scripts/axis.py check doc-link-targets`, and `python scripts/axis.py check doc-drift`.
- Auth/API development: focused Rule Binding query tests; focused policy tests for overlay path validation and propagation; `python scripts/axis.py dotnet test tests/Modules/Identity/Axis.Identity.Infrastructure.Tests/Axis.Identity.Infrastructure.Tests.csproj` for catalog validation, exact legacy adoption, unmarked-collision rejection, and reconciliation; `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj` for seeding, retired-client denial, CORS, and API behavior; regenerate with `python scripts/axis.py generate api-contracts` and prove `python scripts/axis.py check frontend-api-contracts`.
- Axis clean cutover: focused affected frontend tests through `python scripts/axis.py frontend test <paths>`, `python scripts/axis.py frontend ci`, and `python scripts/axis.py check frontend-quality`.
- Runtime acceptance: the reference-solution repository's approved finite manifest/component/API checks plus one blank-workspace browser journey; exact commands are required before implementation begins and must not be substituted by Axis-local mocks.
- Review boundary: clean immutable checkpoints for both repositories, cross-repository acceptance revision recorded, `python scripts/axis.py verify`, `$axis-review-readiness`, and independent review.

## Architecture decision

The conditional platform architecture decision is **no new Axis module**: product-owned orchestration calls public contracts, Rules owns the one generic binding-detail query, each Axis module retains mutation authority, and partial multi-operation progress is explicit rather than a distributed transaction. Event sourcing remains rejected. The reference-product auth portion is Blocked until its BFF Design Gate defines a production-grade product-side boundary; public-browser authentication is not acceptance evidence for that portion.

## Routing checkpoint

| Owner | Work unit | Work shape | Execution owner | Rationale |
|---|---|---|---|---|
| `$axis-use-case-spec` + `$axis-design-gate` | External repository selection, manifest/canonicalization contract, immutable revision exchange, and cross-repository acceptance command | High-risk product/integration decision | Primary | Repository creation, command authority, and the cross-repository completion boundary require user sign-off and shared integration ownership. |
| `$axis-api-contract` | Authenticated workspace-isolated Rule Binding detail query, OpenAPI generation, parity, and focused tests | Bounded implementation after sign-off | Implementation worker | Route, query, DTO reuse, tests, and generated artifacts form a disjoint exact contract with known verification. |
| `$axis-use-case-implementation` | Deployment-configured public PKCE clients, exact redirects, exact CORS origins, and auth tests | High-risk auth/config integration | Primary | The use-case orchestrator integrates Identity and API configuration because client registration and CORS share a trust boundary with the external product origin. |
| `$axis-frontend-feature` + `$axis-ui-system` | Remove the Axis-owned Applications product surface and preserve unrelated generic frontend foundations | Bounded clean-cutover implementation after product evidence exists | Implementation worker | The retired paths and focused frontend checks are disjoint, but deletion waits for the independent product journey to pass. |
| `$axis-doc-hygiene` | Reconcile Business Objects use-case/status/evidence and record immutable external revision evidence | Cross-owner durable-guidance integration | Primary | Status must match both repositories at one cutover checkpoint and cannot be completed by either source writer alone. |
| `$axis-review-readiness` | Audit the immutable Axis and product checkpoints and issue the readiness verdict | Final integration/readiness boundary | Primary | Cross-repository evidence and high-risk auth/wire changes require one integrated audit; readiness does not perform independent review. |
| `$axis-use-case-implementation` | Trigger scoped read-only review of the exact Ready checkpoints and receive the findings report | Independent review boundary | Independent reviewer | Independent judgment starts only after a Ready verdict; the reviewer reports without editing or integrating. |
| `$axis-use-case-implementation` | Classify and integrate review findings, then rerun only affected evidence | Post-review integration | Primary | Finding resolution mutates shared checkpoints and therefore remains with the orchestrator rather than the read-only reviewer. |

Routing is re-evaluated after user sign-off fixes the external repository and commands, after the product journey first passes, and whenever a contract change expands a listed unit.
