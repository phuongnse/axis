# AC and AT Authoring Reference

Use with [SKILL.md](./SKILL.md) when writing or refreshing use-case acceptance criteria and the Acceptance Test Matrix.

## Acceptance criteria

- Keep ACs focused on user-visible product outcomes, business side effects, validation/failure behavior, and ownership boundaries.
- Put accessibility, interaction quality, visual stability, and component expectations in Screen flow or Required UI quality unless they are themselves the product outcome being accepted.
- Group ACs under `Happy path`, `Validation & errors`, and `Edge cases`.
- When implementing, closing, or materially refreshing this use case, give in-scope AC bullets local IDs (`AC-001`, `AC-002`, ...). Do not bulk-retrofit unrelated use cases.
- Format ACs as plain contract bullets: `- **AC-001** Requirement text.`
- Keep progress in implementation status.
- Include enough validation, isolation, authorization, dependency-failure, rollback, and empty-state ACs for the layer that will be implemented.
- Cover the applicable enterprise-production outcomes from [docs/PLATFORM_STRATEGY.md § Enterprise Production Baseline](../../../docs/PLATFORM_STRATEGY.md#enterprise-production-baseline). Put a separate capability in Out Of Scope; do not label a required production property as a deferred follow-up.

## Acceptance Test Matrix

- Add an `## Acceptance Test Matrix` with local AT IDs (`AT-001`, `AT-002`, ...). Every in-scope AC must appear in at least one required AT row before the use case can be closed.
- Use this column shape: `ID | Boundary | Scenario | Covers AC | Verification | Required`.
- Keep the matrix high-level: do not add `Evidence source`, exact runner/tool names, test file paths, class names, or commands to use-case specs.
- Before making a row required, confirm its expected result can cite the spec section, AC ID, or flow step. That citation belongs in the readiness/verification report, not in the use-case matrix.
- Use `Boundary` values that name the proving boundary: `Browser journey`, `UI component`, `API boundary`, `Application boundary`, `Infrastructure boundary`, or a slash/composite when one row intentionally spans boundaries.
- Use `Verification` values that name evidence categories, not exact tools: `Browser automation`, `UI component test`, `API integration test`, `Application test`, `Infrastructure integration test`, etc.

## Screen flow and UI quality

- Use Screen flow for product screen contracts: entry points, visible states, recovery paths, and handoffs between screens.
- Use Required UI quality for accessibility and interaction expectations: labels, focus, keyboard reachability, visible state, layout stability, copy fit, and shared design-system expectations.
- Keep both sections implementation-agnostic: name the expected experience, not component internals, CSS classes, test files, or library-specific props.

## Boundary and runner choice

- Choose the lowest reliable boundary in the spec. Exact runners belong in implementation or review-readiness evidence: Playwright for browser-level journeys, Vitest for focused UI states/validation, and xUnit for backend contracts, side effects, and business rules.
- If the selected runner is not installed yet, record that adding the harness is a new-library Design Gate decision before implementation.

## Reusable evaluator contracts

- For a reusable evaluator or rules capability, specify definition, application/binding, and execution separately. The definition owns declared typed inputs, canonical logic, and declared typed outputs; application maps consumer sources into inputs; execution resolves one exact version and returns outputs plus safe diagnostics.
- Define the polarity of every Boolean output in product language. Require paired acceptance examples for satisfied and unsatisfied assertions, plus absent optional input and malformed required input when applicable; a one-sided match example cannot establish Boolean semantics.
- Keep consumer meaning and side effects outside the evaluator. Validation, calculation, classification, eligibility, routing, and transformation may consume the same contract without creating separate evaluator models.
- Distinguish logic-visible inputs from execution-envelope data used only for isolation, authorization, audit, or tracing. Do not give logic ambient access to platform context or describe source mappings as part of the reusable definition.

## Slicing

- Split oversized work into isolated slices and record the slice boundary in `Decisions` or `Deferred follow-ups`.
