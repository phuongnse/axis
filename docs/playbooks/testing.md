# Testing Playbook

> **Navigation**: [docs/README.md](../README.md) · [docs/playbooks/agent-checklist.md](./agent-checklist.md) · [AGENTS.md](../../AGENTS.md)

Use the smallest test that proves the edit while developing. Use `$axis-ready-review` before review.

## Shared

- Do not skip, weaken, or mock away behavior under test.
- Test observable behavior and boundary contracts.
- Map touched surfaces to the in-scope paths in [docs/playbooks/agent-checklist.md](./agent-checklist.md#acceptance-coverage).

## .NET Testing

Use `{Subject}_{Condition}_{ExpectedOutcome}` for test names.

Tests must not depend on run order or shared mutable state.

### Database rules

Use Testcontainers for persistence/integration. Do not use EF in-memory providers for behavior that depends on relational/database semantics.

### Coverage

Cover happy path plus in-scope validation, not-found/isolation, authorization, constraint, and dependency-failure paths.

Keep deterministic handler/repository tests separate from integration pipeline tests. Use focused fixtures over broad shared setup.

API fixtures must create and isolate the module databases/schemas required by the scenario.

## Frontend Testing

- Use Vitest and Testing Library for component/feature behavior.
- Place tests beside the feature or shared component they prove.
- Use clear `*.test.tsx` / `*.test.ts` names matching the surface.
- Assert UI behavior, API interactions, validation, empty/error states, authorization, and loading/disabled states when in scope.
- Use Playwright for browser-level journeys and layout-sensitive flows.
- Prefer `userEvent` over implementation-level event calls.
- Mock network edges, not product behavior.
