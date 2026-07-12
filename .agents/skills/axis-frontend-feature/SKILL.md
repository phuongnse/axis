---
name: axis-frontend-feature
description: Implement Axis SPA feature behavior. Use for routes, feature components, forms, server-state loading, generated API consumers, mutations, URL state, and user-visible loading, empty, error, or success behavior.
---

# Axis Frontend Feature

## Goal

Implement a frontend slice from supplied product/foundation evidence with generated contracts, observable states, accessible interaction, and focused tests.

## Hard gates

Follow [reference.md](../reference.md).
- Non-trivial entry work **Requires** current `$axis-design-gate` evidence; reuse evidence supplied by an orchestrating use-case or foundation workflow.
- UI token, primitive, baseline, shared visual API, or provider work **Delegates** to `$axis-ui-system` and returns here with its decision.
- Generated request types own wire shape; do not hand-write duplicate DTOs or submit fields absent from the generated request.

## Inputs

- Caller, owning use-case/foundation, and in-scope AC/AT rows or foundation behavior.
- Generated API contract and Design Gate/UI decisions when triggered.
- Existing route, feature, query, component, and test paths.

## Workflow

1. Confirm the caller owns product/foundation decisions; carry current prerequisite evidence and every in-scope AC/AT row through implementation and return evidence.
2. Read [docs/playbooks/frontend.md](../../../docs/playbooks/frontend.md), [docs/playbooks/testing.md](../../../docs/playbooks/testing.md), and the owning contract.
3. Trace routes, access group, query factories/keys, API wrapper, generated types, cache updates, URL state, translations, UI call sites, and tests with `rg`.
4. Implement narrowly using the frontend playbook: stable server-state ownership, RHF/Zod forms, explicit mutation cache behavior, shareable URL state, localized copy, and required loading/empty/error/validation/disabled/success states.
5. Use existing UI contracts. This workflow **Delegates** unresolved visual deviations to `$axis-ui-system` and wire-shape deviations to `$axis-api-contract`; keep consumer classes layout-only and do not create feature-local primitives.
6. Trace every in-scope AC/AT row to observable behavior and API/cache proof with Vitest/Testing Library; use focused Playwright evidence for layout, navigation, or interaction risk.
7. Run the narrow check selected by `$axis-script-scope` when command choice is non-obvious, then return paths, evidence, and unresolved decisions to the caller.

## Output

Report feature paths, AC/AT evidence, route/state decisions, UI/API handoffs, tests/visual evidence, and gaps.
