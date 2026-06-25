---
name: axis-use-case-implementation
description: Implement Axis use-case slices from specs through tests, code, documentation status, and verification. Use when adding or fixing behavior for a documented use case in docs/use-cases, touching module Domain/Application/Infrastructure/API/Frontend layers, or closing implementation-status gaps. If the requested use case has no owning README or acceptance criteria, first use axis-use-case-spec.
---

# Axis Use Case Implementation

## Goal

Ship one reviewable Axis use-case slice without losing acceptance criteria, layer order, docs status, or verification honesty.

## Workflow

1. Locate the owning spec.
   - Read `docs/use-cases/README.md`, the domain `README.md`, and the use-case file.
   - If the use case is unclear, search with `rg -n "<feature words>" docs/use-cases src tests frontend`.
   - If no owning use-case README or AC block exists, stop implementation and use `$axis-use-case-spec` first.
   - Read `docs/WORKAROUNDS.md` for entries touching the same module or files.

2. Prove spec readiness before code.
   - Record `Spec Readiness Verdict: Ready` only when each required-to-close AC and AT expected result can cite the owning spec section, AC ID, or flow step.
   - Mark the verdict `Blocked` and use `$axis-use-case-spec` when any required AC lacks a cited actor, entry point, precondition, observable outcome, business side effect, failure/validation behavior, or testable expected result.
   - Do not infer product behavior from existing code, screenshots, or agent judgment and then make it a required test expectation.

3. Run `$axis-design-gate` for non-trivial work.
   - Stop for high-risk sign-off before code.
   - Carry the dossier decisions into the implementation.

4. Build the AC map.
   - If the use case you are implementing or closing lacks local AC IDs or an `## Acceptance Test Matrix`, update the spec first with `$axis-use-case-spec`.
   - Copy each in-scope `- [ ]` acceptance criterion into a row.
   - Use the matrix as the acceptance contract: every in-scope AC must be covered by at least one required AT row before any layer or use case is marked done.
   - Keep implementation details out of the use-case matrix: do not add `Evidence source`, test file paths, class names, or commands.
   - For each required AT row, record in the implementation/verification report which spec section, AC ID, or flow step defines the expected behavior. If any expected behavior cannot be cited from the spec, stop and use `$axis-use-case-spec`.
   - Include out-of-scope bullets as `N/A this PR`.
   - Give every in-scope row a proving automated test before coding. Use the AT ID in the test title, xUnit trait, or nearby metadata when practical.
   - Choose the lowest reliable runner: Playwright for browser-level journeys, Vitest for focused UI states/validation, and xUnit API/Application/Infrastructure for backend contracts, side effects, and business rules.
   - If a required runner is missing, treat installing the harness as a new-library Design Gate decision and get required sign-off before code.
   - Do not mark a layer complete when validation, edge, isolation, or rollback ACs remain open.

5. Work in layer order.
   - Domain, then Application, then Infrastructure, then API, then Frontend.
   - Before API work, search open lower-layer gaps in `docs/use-cases`.

6. Use TDD where behavior changes.
   - Add or update the proving test first when practical.
   - Use .NET test names shaped as `{Subject}_{Condition}_{ExpectedOutcome}`.
   - Do not add skips, weaken assertions, or mock away the behavior under test.

7. Implement narrowly.
   - Follow same-module patterns before inventing abstractions.
   - Use `Result` / `Result<T>` for business failures.
   - Keep endpoint logic thin: route binding, `mediator.Send`, and problem-details mapping.
   - Keep frontend server state in TanStack Query and forms in RHF + Zod.

8. Update status docs when behavior/status changes.
   - Update the use-case `Implementation status` callout.
   - Use `$axis-visual-artifact` when frontend screen shape, design-source links, previews, or diagrams changed.
   - Update the domain README and `docs/PROGRESS.md` only when their summarized status changes.
   - Name exact deferred AC bullets under `Deferred follow-ups`; use `N/A` when none.
   - Add or update `docs/WORKAROUNDS.md` only for intentional P0/P1 workarounds.

9. Verify honestly.
   - During development, run the smallest check that proves the touched surface.
   - Run every required AT row's runner or targeted test before claiming the use case/layer is complete.
   - Report acceptance evidence by AT ID (`AT-001 Playwright passed`, `AT-002 xUnit API passed`, etc.).
   - Use `$axis-ready-review` before asking for review; it owns `python scripts/axis.py verify` when triggered.
   - Do not rerun ready-review verification after every small edit.
   - Do not claim the full suite unless full `python scripts/axis.py dotnet test` ran.

## Output

When finishing, report:

```text
Implemented:
- ...

Tests / checks:
- command -> pass/fail/not run with reason

Acceptance evidence:
- AT-... -> pass/fail/not run with reason

Docs:
- updated / not triggered with reason

Gaps and deferred follow-ups:
- ...
```
