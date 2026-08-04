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

## Inputs

- Caller, owning use-case, applicable foundation contracts, and all in-scope product/foundation AC/AT rows.
- User outcome, journey, mode, state, hierarchy, content, and quality decisions from the client experience contract.
- Generated API contract and Design Gate/UI decisions when triggered.
- Existing route, feature, query, component, and test paths.

## Workflow

1. Confirm the caller owns product decisions, discover applicable foundation contracts, and carry current prerequisite evidence plus every in-scope product/foundation AC/AT row through implementation and return evidence.
2. Read [docs/playbooks/client-experience.md](../../../docs/playbooks/client-experience.md), [docs/playbooks/frontend.md](../../../docs/playbooks/frontend.md), [docs/playbooks/testing.md](../../../docs/playbooks/testing.md), and the owning contract.
3. Trace the complete existing journey: routes, neighboring screens, access group, entry/exit, query factories/keys, API wrapper, generated types, cache and URL state, translations, UI call sites, and tests with `rg`. Classify visible vocabulary as server-owned product meaning or client-owned interface copy; flag every frontend mapping or static guide that duplicates server-owned values.
4. Before JSX, reconcile the experience contract: user outcome; primary and recovery paths; read/edit transitions; applicable state model; information hierarchy; relationships; semantic component mapping; content/help; responsive and accessibility behavior. Apply domain and collection details from their owning contracts. Decide ordinary UX details autonomously from the owner and existing patterns; stop only for missing product behavior.
5. Implement the complete in-scope journey narrowly: stable server state, RHF/Zod forms, explicit mutation cache behavior, shareable URL state, localized copy, visible progress, actionable errors, recovery, and distinct loading/empty/validation/disabled/success states. Render backend-owned labels, explanations, examples, constraints, and compatibility from generated reference metadata; never create a parallel feature-local knowledge map.
6. Use existing UI contracts. This workflow **Delegates** unresolved visual deviations to `$axis-ui-system` and wire-shape deviations to `$axis-api-contract`; keep consumer classes layout-only and do not create feature-local primitives. When replacing a surface, remove obsolete controls, mappings, translations, and tests in the same slice unless the owning contract explicitly requires overlap.
7. Trace every in-scope AC/AT row to its lowest reliable boundary. Use Vitest/Testing Library for state and component matrices. Give each Playwright test one independently runnable user outcome; keep one thin cross-layer happy path and separate only browser-required lifecycle, recovery, read-only discovery, keyboard, layout/overflow, or console-health journeys. Never solve a broad or stale journey with fixed sleeps, serial state, or a local timeout override.
8. Run documented focused checks directly through their `python scripts/axis.py ...` wrappers, never raw npm, Playwright, or .NET commands. Use the verification handoff only when changed paths lack an owned command or alter workflow selection; report every omitted broad check.

## Output

Report feature paths, experience contract and autonomous decisions, open product decisions, AC/AT evidence, route/state decisions, UI/API handoffs, tests/browser evidence, and gaps.
