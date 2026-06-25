# Testing Playbook

> **Navigation**: [<- docs/README.md](../README.md) . [<- agent checklist](./agent-checklist.md) . [<- AGENTS.md](../../AGENTS.md)

Use the smallest test that proves the edit while developing. Use `$axis-ready-review` before review.

## Shared

Do not skip, weaken, or mock away behavior under test. Test observable behavior and boundary contracts.

## .NET testing

### Test naming

Use `{Subject}_{Condition}_{ExpectedOutcome}`.

### Test isolation

Tests must not depend on run order or shared mutable state.

### Database rules

Use Testcontainers for persistence/integration. Do not use EF in-memory providers for behavior that depends on relational/database semantics.

### Required test coverage for integration tests

Cover happy path plus in-scope validation, not-found/isolation, authorization, constraint, and dependency-failure paths.

### Required path coverage (all implementation types)

Map each touched handler/repo/job/consumer/endpoint/component to the relevant paths in [agent checklist](./agent-checklist.md#ac-coverage--avoid-happy-path-only).

### Additional .NET test patterns

Keep deterministic handler/repository tests separate from integration pipeline tests. Use focused fixtures over broad shared setup.

### Ready-PR gate

Before review, `$axis-ready-review` owns triggered checks and `python scripts/axis.py verify` when needed.

### Integration test maintenance

Stabilize setup determinism before retrying flaky suites. Confirm preconditions exist before the request/action under test.

### ApiTestFixture — module database isolation (ADR-011 + ADR-023)

API fixtures must create and isolate the module databases/schemas required by the scenario.

### Keep deterministic tests separate from async-pipeline tests

Use unit/application tests for deterministic decisions and integration tests for persistence/API behavior.

## Frontend testing

### Runner and structure

Use Vitest and Testing Library for component/feature behavior.

### File location

Place tests beside the feature or shared component they prove.

### File naming

Use clear `*.test.tsx` / `*.test.ts` names matching the surface.

### What to test

Assert UI behavior, API interactions, validation, empty/error states, authorization, and loading/disabled states when in scope.

### Browser E2E

Use Playwright for browser-level journeys and layout-sensitive flows.

### Interactions

Prefer `userEvent` over implementation-level event calls.

### Mocking

Mock network edges, not product behavior.

### Pre-push gate

Pre-push is quick sanity. Ready-review verification happens later.
