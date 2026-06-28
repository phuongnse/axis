# AC and AT Authoring Reference

Use with [SKILL.md](./SKILL.md) when writing or refreshing use-case acceptance criteria and the Acceptance Test Matrix.

## Acceptance criteria

- Group ACs under `Happy path`, `Validation & errors`, and `Edge cases`.
- When implementing, closing, or materially refreshing this use case, give in-scope AC bullets local IDs (`AC-001`, `AC-002`, ...). Do not bulk-retrofit unrelated use cases.
- Format ACs as plain contract bullets: `- **AC-001** Requirement text.`
- Keep progress in implementation status.
- Include enough validation, isolation, authorization, dependency-failure, rollback, and empty-state ACs for the layer that will be implemented.

## Acceptance Test Matrix

- Add an `## Acceptance Test Matrix` with local AT IDs (`AT-001`, `AT-002`, ...). Every in-scope AC must appear in at least one required AT row before the use case can be closed.
- Keep the matrix high-level: do not add `Evidence source`, test file paths, class names, or commands to use-case specs.
- Before making a row required, confirm its expected result can cite the spec section, AC ID, or flow step. That citation belongs in the readiness/verification report, not in the use-case matrix.
- Use `Automated by` values that name runners/tools, not file paths: `Playwright`, `Vitest`, `xUnit API`, `xUnit Application`, `xUnit Infrastructure`, etc.

## Runner choice

- Choose the lowest reliable level: Playwright for browser-level journeys, Vitest for focused UI states/validation, and xUnit for backend contracts, side effects, and business rules.
- If the selected runner is not installed yet, record that adding the harness is a new-library Design Gate decision before implementation.

## Slicing

- Split oversized work into isolated slices and record the slice boundary in `Decisions` or `Deferred follow-ups`.
