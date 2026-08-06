# Provision A Reference Solution

> **Navigation**: [docs/use-cases/solutions/README.md](./README.md) · [docs/use-cases/README.md](../README.md) · [docs/PLATFORM_STRATEGY.md](../../PLATFORM_STRATEGY.md) · [docs/README.md](../../README.md) · [AGENTS.md](../../../AGENTS.md)

## Purpose

Provision an independently versioned reference solution into a blank Axis workspace through authenticated public contracts, then complete the existing Draft → Submitted record journey from a product-owned client without product identity, behavior, routes, copy, or setup logic in Axis source.

This is the Wave 0 consumer-boundary slice from [docs/PLATFORM_STRATEGY.md](../../PLATFORM_STRATEGY.md#delivery-sequence). It proves source ownership, deterministic provisioning, read-back, recovery, and clean product separation; it does not introduce persisted `SolutionVersion` or `SolutionInstallation` models.

## Primary actor

- Signed-in workspace user acting as the reference-solution release operator

## Supporting actor

- The same or another signed-in workspace user completing the provisioned record journey

## Preconditions

- The target is a blank authorized Workspace.
- The release manifest, matching generated public client, approved product origin, and required built-in Rule versions are available.

## Trigger

- A release operator needs to reproduce an exact reference-solution release in a blank Axis workspace.
- A product team needs to prove that its client and product source consume Axis without an Axis fork or product-specific platform code.

## Success guarantee

- The exact reference-solution release is reproducibly provisioned and read back through authenticated public contracts, and its product-owned client completes the existing record journey.

## Minimal guarantee

- Validation, compatibility, conflict, interruption, or read-back failure never claims success or creates unverifiable product state; exact matching work remains safely resumable.

## Main flow

1. Release operator opens the independently owned reference-solution client at its deployment-approved origin and signs in through its same-origin confidential BFF; the browser receives only an opaque product session cookie.
2. Client loads one source-controlled solution manifest containing stable solution identity/version, the exact Axis OpenAPI digest, required built-in Rule versions, Business Object definition and fields, complete Rule binding mappings/behavior, and product-owned localized content identity.
3. Client validates and canonicalizes the complete manifest locally before contacting mutation operations.
4. Client uses authenticated public reads to resolve the current workspace, required built-in Rules, existing Rule bindings and their full canonical content, Business Object definitions, and any prior matching provisioning state.
5. Client displays the exact target solution version, target workspace, planned creates/reuse, and any blocking conflict before the operator confirms provisioning.
6. Client creates only missing Rule bindings and the unpublished Business Object definition through current public operations, saves the exact field/binding contract, and publishes the immutable definition version.
7. Client reads every created or reused resource back through public operations and compares the canonical result with the manifest; provisioning succeeds only when the target state matches.
8. Repeating the same release against the same exact state performs no product mutation. An interrupted exact attempt resumes from verified matching resources rather than duplicating them.
9. Workspace user opens the product-owned Applications experience, creates a Draft, saves values, recovers from a Rule non-match, submits valid values, and reads the immutable submission evidence owned by [docs/use-cases/business-objects/submit-business-object-record.md](../business-objects/submit-business-object-record.md).
10. Acceptance evidence starts from a blank workspace and proves the manifest, provisioning client, product client, public API, and persisted read-back without direct database access or Axis-owned product behavior.

## Wave 0 manifest contract

The source schema is `manifestVersion: 1`. Solution identity is `(solutionKey, solutionVersion)`, where the key uses the lower-case semantic-key grammar and the version is an exact SemVer value. `axisOpenApiSha256` is the lower-case SHA-256 of the exact committed `openapi.json` used to generate and test the product client; cross-repository acceptance pairs that digest with immutable Axis and product revisions. Runtime compatibility additionally requires every preflight read to parse with the generated client and expose the required semantics before mutation; Wave 0 adds no separate version-negotiation endpoint.

Component semantic identities are exact ordinal tuples:

- Rule dependency: `(definitionKey, definitionVersion)`.
- Business Object: `objectKey`, which must equal the public server-derived key returned for the manifest's fixed canonical `name`; changing that derivation or receiving another key is incompatible, not an alias or rename.
- Field: `(objectKey, fieldKey)`; choice option: `(objectKey, fieldKey, optionKey)`.
- Rule Binding: `(definitionKey, definitionVersion, targetType, targetId, useCaseOrTrigger)`. Wave 0 permits exactly one reference-solution binding for that tuple; zero means create, one exact match means reuse, and two or more means an ambiguous conflict.

The canonical product projection contains only manifest-authored behavior: the identities above; Business Object name, expected published version `1`, fields, labels, types, selection modes, choices, and explicit order; and Rule Binding mappings, priority, enabled state, and failure behavior. Semantic keys and enum values are exact case-sensitive API values. Strings must already be trimmed and Unicode NFC. Maps sort by ordinal key; component arrays sort by semantic identity; ordered field, choice, and attachment arrays compare both their integer `order` and ordered semantic identities; literal-value order is preserved. Canonical JSON uses UTF-8, lexicographically sorted property names, and no insignificant whitespace.

Generated IDs, workspace/actor IDs, timestamps, source IDs, and concurrency revisions are excluded from content equality. The current binding revision is still captured from read-back and the published field attachment must reference that exact binding ID/revision. Business Object status and published version `1` are included. Any omitted required property, additional unknown property, non-normal form, duplicate semantic identity/order, unexpected generated relationship, or projection difference is a conflict rather than an implicit default.

## Alternate / error flows

- Manifest syntax, unsupported manifest version, invalid semantic key, duplicate component identity, unsupported field contract, or non-canonical content: reject before any API mutation.
- Axis contract incompatibility or missing exact built-in Rule version: report the unmet dependency and perform no mutation.
- Missing authentication, unavailable workspace, unregistered product client, redirect mismatch, or disallowed origin: reject without product-resource disclosure or mutation.
- Existing resource with the same semantic identity but different canonical content: report a blocking conflict; never overwrite, adopt, rename, or create a compatibility alias.
- Existing exact matching resource: reuse it and include it in read-back; do not create a duplicate.
- API failure after one or more successful operations: report the exact completed and pending plan entries. A rerun may resume only after public read-back proves every completed entry still matches.
- Read-back mismatch: report provisioning as failed and preserve the observable target state for safe diagnosis; never claim success from write responses alone.
- Product client load or record-journey failure: keep the provisioned platform state inspectable and provide a retry path without provisioning again.
- Wave 0 runs one active provisioning attempt per `(workspace, solutionKey, solutionVersion)`. Concurrent attempts are unsupported; if preflight or post-mutation read-back observes duplicate Rule Binding identity tuples or otherwise ambiguous state, fail closed and require operator diagnosis rather than selecting or deleting a resource automatically.

## Acceptance Criteria

*Happy path*

- **AC-001** The reference solution lives outside Axis source as an independently versioned product with its own manifest, provisioning behavior, client routes, localized product content, and acceptance journeys.
- **AC-002** The manifest conforms exactly to the Wave 0 identity, OpenAPI digest, component, ordering, normalization, canonical projection, and generated-field exclusion contract without runtime database IDs.
- **AC-003** Manifest validation and canonicalization are deterministic; the same valid source produces byte-identical canonical JSON, the same ordered provisioning plan, and the same content comparison.
- **AC-004** Preflight discovers dependencies and target state through authenticated public reads and shows the target workspace, creates, exact reuse, and conflicts before confirmation.
- **AC-005** Provisioning uses only documented REST/OpenAPI operations, including the generic authenticated Rule Binding detail read required by this contract, and creates the exact Rule bindings, unpublished Business Object definition, published field contract, and immutable definition version required by the manifest.
- **AC-006** Success requires public read-back of every created or reused resource and exact canonical comparison with the manifest.
- **AC-007** With one active attempt for the workspace/release, reapplying an exact release is a no-op and an interrupted exact attempt resumes without duplicates only from publicly verified matching state; duplicate or ambiguous semantic identities fail closed and concurrent same-release attempts are unsupported in Wave 0.
- **AC-008** A product-owned client authenticates at a deployment-configured first-party origin and completes the existing Draft → Submitted journey, including recoverable Rule non-match and immutable evidence.
- **AC-009** A blank-workspace acceptance journey provisions and runs the reference solution without direct database access, undocumented seed behavior, manual resource editing, or an Axis fork.

*Validation & errors*

- **AC-010** Invalid, unsupported, ambiguous, duplicate, or non-canonical manifest content fails before the first mutation with actionable component-local diagnostics.
- **AC-011** Missing or incompatible Axis contracts, built-in Rules, field capabilities, or public operations fail preflight without partial provisioning.
- **AC-012** A semantic-identity collision with different content fails closed and never overwrites, adopts, renames, aliases, or duplicates the existing resource.
- **AC-013** An API failure after partial progress returns exact completed/pending state; later resume requires fresh public read-back and refuses any conflicting change.
- **AC-014** Read-back mismatch is a failed provisioning result, and logs/errors expose no token, authorization code, secret, or another workspace's resource data.
- **AC-015** Product authentication uses the required same-origin confidential BFF as an Axis OAuth/OIDC client with Authorization Code + PKCE, mandatory PAR, exact deployment-configured sign-in/sign-out redirects, server-side refresh/revocation, a distributed opaque session, and CSRF validation. Browser code receives no client secret, access/refresh token, or post-callback authorization code; product identity and secrets remain deployment-owned outside Axis source. Any signed-in target-Workspace user may provision in Wave 0, while unauthenticated and cross-Workspace access is rejected.

*Edge cases and boundaries*

- **AC-016** Wave 0 adds no Solutions aggregate, database, migration, install endpoint, batch transaction, rollback promise, package signing, overlay, or upgrade behavior; those remain later owning use cases.
- **AC-017** Axis retains structural ownership of generic Business Objects, Rules, Identity, API, MCP, app-shell, data-display, and managed-window capabilities while retiring every `loan_application`/Applications sample route, product translation, product component, product setup function, and product-specific test from Axis in one clean cutover.
- **AC-018** No compatibility route, alias, duplicated product client, feature flag, fallback provisioning path, or preserved product copy remains after the cutover.
- **AC-019** The reference product may depend on committed OpenAPI semantics and public authentication behavior but may not reference Axis module internals, platform frontend feature code, module databases, or test-only setup APIs.
- **AC-020** REST/OpenAPI remains the canonical product contract; Wave 0 adds only the generic authenticated Rule Binding detail read needed for full canonical comparison. MCP may expose parity for authenticated agents but is neither the provisioning runtime nor substitute acceptance evidence for the product client.
- **AC-021** Focused component evidence proves that unrelated authenticated shell navigation, generic collection/data-display composition, and generic managed-window behavior still work after the Applications product surface is removed.

## Acceptance Test Matrix

| ID | Boundary | Scenario | Covers AC | Verification | Required |
|---|---|---|---|---|---|
| AT-001 | Application boundary | Manifest parsing applies the exact identity tuples, OpenAPI digest, normalization, canonical projection, generated-field exclusions, duplicate rules, and ordering to produce byte-identical canonical JSON/plans or component-local rejection before mutation | AC-002, AC-003, AC-010 | Application test | Yes |
| AT-002 | API/Application boundaries | Blank, exact-existing, conflicting, incompatible, and missing-dependency preflight states are derived only from authenticated public reads | AC-004, AC-011, AC-012, AC-019 | Application test + API integration test | Yes |
| AT-003 | API boundary | Confirmed provisioning creates the exact Rule binding and published Business Object contract through documented operations and verifies canonical read-back | AC-005, AC-006, AC-009, AC-020 | API integration test | Yes |
| AT-004 | API/Application boundaries | One active exact rerun is a no-op; injected interruption resumes matching progress, while duplicate binding tuples, changed state, and simulated concurrent ambiguity fail closed without being selected as success | AC-007, AC-013, AC-014 | Application test + API integration test | Yes |
| AT-005 | API boundary | The confidential BFF completes PKCE/PAR, keeps tickets and access/refresh tokens server-side, refreshes once under concurrent multi-instance calls, enforces CSRF and the route/method allowlist, preserves exact redirect/logout boundaries, and rejects unauthenticated/cross-workspace access without leaking credentials | AC-014, AC-015 | API integration test + Browser automation | Yes |
| AT-006 | UI component | Provisioning experience presents version/workspace identity, plan, confirmation, progress, conflicts, read-back success, partial failure, and retry without hiding mutations | AC-004, AC-006, AC-013 | UI component test | Yes |
| AT-007 | Browser journey | From a blank workspace, operator provisions once and a user completes the product-owned Draft, non-match recovery, Submit, and evidence journey through the independently owned client | AC-001, AC-008, AC-009 | Browser automation | Yes |
| AT-008 | Application boundary | Repository-boundary sweep proves Axis has no reference-product identity, route, copy, provisioning behavior, duplicate client, fallback, or product-specific tests while generic contracts remain | AC-017, AC-018, AC-019 | Architecture test | Yes |
| AT-009 | API boundary | The committed OpenAPI includes an authenticated, workspace-isolated Rule Binding detail read returning full binding content, while no product-specific query or Solutions install/batch/upgrade wire contract is introduced | AC-005, AC-016, AC-020 | API integration test | Yes |
| AT-010 | UI component | Unrelated authenticated shell navigation, a consumer-neutral collection/data-display fixture, and generic managed-window focus/open/close behavior pass after Applications source and registrations are removed | AC-017, AC-021 | UI component test | Yes |

## Out Of Scope

- Persisted Solutions definitions, package versions, installations, signatures, trusted publishers, overlays, upgrades, rollback, drift, promotion, or product data migrations.
- Generic workflow definitions, assignments, approvals, automation, connectors, documents, reports, notifications, entitlements, or marketplace behavior.
- Supporting arbitrary third-party clients, dynamic client registration, browser-exposed client secrets, wildcard redirect URIs, or wildcard CORS.
- Concurrent same-release provisioning, distributed locking, automatic duplicate cleanup, or atomic installation; observed ambiguity fails closed in Wave 0.
- Generalizing the product-owned Applications experience into metadata-driven Axis forms, views, routes, or navigation; Experiences owns that later wave.
- Preserving the Axis-owned sample route or a compatibility overlap after the independent product journey passes.

## Screen flow

| Surface | Required contract |
|---|---|
| Product sign-in | Start the same-origin BFF login, preserve a validated local return intent, and complete the OIDC callback server-side without exposing authorization artifacts or server-side credentials. |
| Provisioning preflight | Lead with solution identity/version and target workspace, then show ordered create/reuse/conflict entries and one explicit confirmation action; invalid or incompatible source has no mutation action. |
| Provisioning progress | Show the current plan entry and completed/pending outcomes; disabling dismissal during a mutation must not remove recovery information after failure. |
| Provisioning result | Success shows canonical read-back and the next product action. Partial failure or mismatch shows completed/pending/conflicting entries and a safe retry after fresh preflight. |
| Product Applications experience | Preserve the outcome and recovery contract owned by [docs/use-cases/business-objects/submit-business-object-record.md](../business-objects/submit-business-object-record.md) while all product copy, route ownership, and composition remain in the reference product. |

Required UI quality: every plan entry and outcome is programmatically labelled; confirmation receives focus only after a valid preflight; progress is announced without stealing focus repeatedly; errors identify the component and recovery action; keyboard and screen-reader users can inspect the full plan; compact and desktop layouts do not overflow; product copy is localized by the reference product; tokens, secrets, authorization codes, stack traces, and cross-workspace identifiers never render.

> **Implementation status**
>
> | Layer | Status |
> |---|---|
> | Reference product source | Done |
> | Axis Domain | N/A |
> | Axis Application | Done |
> | Axis Infrastructure | N/A |
> | API and authentication | Done |
> | Reference product client | Done |
> | Axis frontend retirement | Done |
>
> **Gaps vs spec:** none.
>
> **Deferred follow-ups:** Persisted, signed, installable and upgradeable Solutions packages begin after this consumer boundary passes; [docs/PLATFORM_STRATEGY.md](../../PLATFORM_STRATEGY.md#delivery-sequence) owns their sequence.
>
> **Verification:** [provision-reference-solution.evidence.md](./provision-reference-solution.evidence.md) binds every required AT to the reachable reference-product checkpoint `2b32616923ff9bb33b50013700b92ace1c5be15e` or its Axis-owned verification source. That checkpoint pins the current Axis OpenAPI digest and passed dependency audit, manifest/BFF checks, unit tests, the blank-workspace authenticated browser journey, and independent review. The published Wave 0 Axis range passes review-readiness; no missing boundary is treated as accepted.
>
> **Decisions:** Wave 0 uses existing module operations and a product-owned manifest/provisioning client instead of inventing a Solutions runtime. The manifest owns exact semantic identity and canonical comparison; runtime-generated fields never become product identity. A blank Workspace has platform built-ins but no reference-solution definitions, bindings, or records. The independent product replaces the retired Axis-owned sample through a clean cutover. Product authentication uses a same-origin confidential BFF with server-side credentials, bounded forwarding, and no browser credential path; [docs/TECH_STACK.md](../../TECH_STACK.md) owns its approved implementation stack. Any signed-in target-Workspace user may provision in Wave 0. Partial multi-operation provisioning is explicit and resumable from public read-back rather than presented as an atomic install.
