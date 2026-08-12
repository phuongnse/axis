---
name: axis-frontend-feature
description: Self-direct and implement Axis client experiences from ready product contracts. Use for user journeys, routes, screen hierarchy, read/edit modes, feature components, forms, server-state loading, generated API consumers, mutations, URL state, recovery, and user-visible loading, empty, error, or success behavior.
---

# Axis Frontend Feature

## Goal

Turn a ready product/foundation contract into a coherent, easy-to-use client journey with generated contracts, observable and recoverable states, accessible interaction, and focused evidence.

## Hard gates

Follow [reference.md](../reference.md).
- Non-trivial entry work **Requires** current `$axis-design-gate` evidence; reuse evidence supplied by an orchestrating use-case or foundation workflow.
- UI token, primitive, baseline, shared visual API, or provider work **Delegates** to `$axis-ui-system` and returns here with its decision.
- Unresolved verification command selection **Delegates** to `$axis-script-scope`; known owner commands stay local.
- Generated request types own wire shape; do not hand-write duplicate DTOs or submit fields absent from the generated request.
- Follow the Design Gate compatibility decision. A clean cutover removes the retired journey/components/state/tests instead of hiding or preserving them beside the replacement.
- Resource collection CRUD consumes [docs/foundations/data-display/collection-page.md](../../../docs/foundations/data-display/collection-page.md). A separate CRUD route, drawer, or competing collection requires an owning-contract exception before implementation.
- A cross-feature visual-system or surface-archetype change requires an `enforced` contract returned by `$axis-ui-system`; feature work does not define, fork, or locally approximate a shared direction.
- Every active feature surface uses a contract-compatible id from `frontend/src/lib/ui-foundation.ts`, is bound to a real implementation symbol in `frontend/src/lib/active-surface-registry.ts`, and renders through its declared owner. `frontend/ui-coverage-profile.json` defines current conformance requirements and invalidation; `frontend/ui-foundation.json` records lifecycle, review state, exact covered/gap/not-applicable partition, acceptance/evidence traces, and the checked enforced-contract registry; the real-symbol registry owns source identity. Filename or import-text scans do not establish conformance; rendered owner markers are evidence only when produced by the owner itself.

## Inputs

- Caller, owning use-case, applicable foundation contracts, and all in-scope product/foundation AC/AT rows.
- User outcome, journey, mode, state, hierarchy, content, and quality decisions from the client experience contract.
- Generated API contract and Design Gate/UI decisions when triggered.
- Existing route, feature, query, component, and test paths.

## Workflow

1. Confirm the caller owns product decisions, read the UI contract metadata and typed active-surface catalog, resolve the feature's registered surface contract, and carry current prerequisite evidence plus every in-scope product/foundation AC/AT row through implementation and return evidence.
2. Read [docs/playbooks/client-experience.md](../../../docs/playbooks/client-experience.md), [docs/playbooks/frontend.md](../../../docs/playbooks/frontend.md), [docs/playbooks/testing.md](../../../docs/playbooks/testing.md), and the owning contract.
3. Trace the complete existing journey: routes, neighboring screens, access group, entry/exit, query factories/keys, API wrapper, generated types, cache and URL state, translations, UI call sites, and tests with `rg`. Classify visible vocabulary as server-owned product meaning or client-owned interface copy; flag every frontend mapping or static guide that duplicates server-owned values.
4. Before JSX, reconcile the experience contract: user outcome; primary and recovery paths; read/edit transitions; applicable state model; information hierarchy; relationships; semantic-role mapping; content/help; responsive and accessibility behavior. Apply domain, collection, and enforced visual-system contracts from their owners. Decide ordinary product details within those contracts; delegate any new cross-feature visual language or archetype instead of designing it inside the feature.
5. Implement the complete in-scope journey narrowly: stable server state, RHF/Zod forms, explicit mutation cache behavior, shareable URL state, localized copy, visible progress, actionable errors, recovery, and distinct loading/empty/validation/disabled/success states. Render backend-owned labels, explanations, examples, constraints, and compatibility from generated reference metadata; never create a parallel feature-local knowledge map.
6. Use only enforced UI contracts, pass the contract-compatible typed surface id to the owner, and keep the real-symbol inventory current. Map product state to existing semantic roles; do not invent values, duplicate the owner's anatomy, or create component-local conventions. This workflow **Delegates** unresolved visual deviations to `$axis-ui-system` and wire-shape deviations to `$axis-api-contract`; keep consumer code to product data, state, content, and declared slots. When replacing a surface, remove obsolete controls, mappings, translations, and tests in the same slice unless the owning contract explicitly requires overlap.
7. Trace every in-scope AC/AT row to its lowest reliable boundary. Use Vitest/Testing Library for state and component matrices. Give each Playwright test one independently runnable user outcome; keep one thin cross-layer happy path and separate only browser-required lifecycle, recovery, read-only discovery, keyboard, layout/overflow, or console-health journeys. Never solve a broad or stale journey with fixed sleeps, serial state, or a local timeout override.
8. Run documented focused checks directly through their `python scripts/axis.py ...` wrappers, never raw npm, Playwright, or .NET commands. Use the verification handoff only when changed paths lack an owned command or alter workflow selection; report every omitted broad check.

## Output

Report feature paths, experience contract and autonomous decisions, open product decisions, AC/AT evidence, route/state decisions, UI/API handoffs, tests/browser evidence, and gaps.
