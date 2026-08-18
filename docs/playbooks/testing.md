# Testing Playbook

> **Navigation**: [docs/README.md](../README.md) · [docs/playbooks/agent-checklist.md](./agent-checklist.md) · [AGENTS.md](../../AGENTS.md)

Use the smallest test that proves the edit while developing. Use the shared
`verify-change` lifecycle; the Axis `review` profile supplies the immutable
review-boundary checks before `review-change` assigns an independent reviewer.

## Shared

- Do not skip, weaken, or mock away behavior under test.
- Test observable behavior and boundary contracts.
- Treat warnings, flakes, and cleanup failures as lifecycle signals. Identify the setup, render/execute, assertion, unmount, and cleanup boundary that produced the signal before choosing a fix.
- Prefer semantic test lifecycle fixes over generic suppressors, extra waits, or framework wrappers. Use those mechanisms only when they model the behavior under test, and record that reason in the implementation notes.
- When the target is a clean warning/failure signal, rerun the focused command and confirm the relevant output is clean, not merely passing.
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
- Place tests beside the feature or design-system component they prove.
- Use clear `*.test.tsx` / `*.test.ts` names matching the surface.
- Assert UI behavior, API interactions, validation, empty/error states, authorization, and loading/disabled states when in scope.
- Use Playwright for browser-level journeys and layout-sensitive flows.
- Prefer `userEvent` over implementation-level event calls.
- Mock network edges, not product behavior.

### Browser journeys

- Give each Playwright test one independently runnable user outcome. Do not share mutable state or require test order.
- Keep one thin cross-layer happy path per use case. Put lifecycle branches, recovery, read-only discovery, and responsive behavior in separate focused journeys only when a browser boundary is required.
- Prove component variants, detailed control behavior, semantic markup, and exact visual composition with component tests. Browser tests cover routing, real browser interaction, cross-layer integration, keyboard behavior, layout/overflow, and console health without repeating the component matrix.
- Use web-first assertions against observable URL, response, busy, content, focus, or visibility changes. Fixed sleeps and local test-timeout overrides are forbidden; a timeout means fix the wait or split the journey. A bounded assertion or external-system poll may use an explicit timeout when the behavior itself requires that budget.
- Keep network mocks at the API boundary and make them honor server-owned query, paging, and metadata semantics. A browser mock must not restore retired client behavior.
- During development, run the affected test title or file. At review, run the browser journeys triggered by the diff and its owning acceptance evidence. Run the full browser suite only in CI or when a cross-cutting change invalidates every browser surface.
