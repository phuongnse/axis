---
name: axis-use-case-implementation
description: Implement Axis use-case slices from specs through tests, code, documentation status, and verification. Use when adding or changing behavior for a documented use case in docs/use-cases, touching module Domain/Application/Infrastructure/API/Frontend layers, EF migrations in a use-case slice, or closing implementation-status gaps. If the requested use case has no owning use-case file or acceptance criteria, first read axis-use-case-spec.
---

# Axis Use Case Implementation

## Goal

Ship one reviewable Axis use-case slice without losing acceptance criteria, layer order, docs status, or verification honesty.

## Hard gates

Follow [reference.md](../reference.md).
- No code when spec readiness is **Blocked** — read `$axis-use-case-spec` first.
- High-risk: stop for `$axis-design-gate` sign-off before code.
- Do not mark **Done** while in-scope AC/AT rows lack proving tests or pass evidence.
- Do not push a non-trivial implementation to a published or PR branch without `$axis-pull-request`.

## Inputs

- Owning use-case file with Purpose, flows, ACs, Acceptance Test Matrix, Out Of Scope, optional diagrams, and implementation status.
- Design Gate dossier and sign-off when the slice is non-trivial or high-risk.
- In-scope AC/AT rows and the lowest reliable verification boundary for each required behavior.

## Workflow

1. Locate the owning spec.
   - Read [docs/use-cases/README.md](../../../docs/use-cases/README.md), the domain `README.md`, and the use-case file.
   - If the use case is unclear, search with `rg -n "<feature words>" docs/use-cases src tests frontend`.
   - If no owning use-case file or AC block exists, stop and read `.agents/skills/axis-use-case-spec/SKILL.md` (`$axis-use-case-spec`) first.

2. Prove spec readiness before code.
   - Record `Spec Readiness Verdict: Ready` only when each required-to-close AC and AT expected result can cite the owning spec section, AC ID, or flow step.
   - Mark the verdict `Blocked` and use `$axis-use-case-spec` when any required AC lacks a cited actor, entry point, precondition, observable outcome, business side effect, failure/validation behavior, or testable expected result.
   - Do not infer product behavior from existing code, screenshots, or agent judgment and then make it a required test expectation.

3. Run `.agents/skills/axis-design-gate/SKILL.md` (`$axis-design-gate`) for non-trivial work.
   - Stop for high-risk sign-off before code.
   - For new modules, module boundaries, foundational DDD/CQRS rules, or event-sourcing changes, stop and run `.agents/skills/axis-module-architecture/SKILL.md` (`$axis-module-architecture`); carry a **Ready** verdict before code.
   - When the slice adopts tactical DDD/CQRS/persistence/event patterns, run `.agents/skills/axis-module-patterns/SKILL.md` (`$axis-module-patterns`) and carry a **Ready** verdict before code.
   - Carry the dossier decisions into the implementation.

4. Build the AC map.
   - If the use case you are implementing or closing lacks local AC IDs or an `## Acceptance Test Matrix`, update the spec first with `$axis-use-case-spec`.
   - Map each in-scope `AC-...` bullet into one or more required AT rows.
   - Use the matrix as the acceptance contract: every in-scope AC must be covered by at least one required AT row before any layer or use case is marked done.
   - Keep implementation details out of the use-case matrix: do not add `Evidence source`, test file paths, class names, or commands.
   - For each required AT row, record in the implementation/verification report which spec section, AC ID, or flow step defines the expected behavior. If any expected behavior cannot be cited from the spec, stop and use `$axis-use-case-spec`.
   - Treat out-of-scope bullets as handoff boundaries, not deferred implementation rows.
   - If an out-of-scope item is unrelated to the flow, tighten the spec with `$axis-use-case-spec`.
   - Give every in-scope row a proving automated test before coding. Use the AT ID in the test title, xUnit trait, or nearby metadata when practical.
   - Choose the exact runner for each matrix verification category: Playwright for browser automation, Vitest for UI component tests, and xUnit API/Application/Infrastructure for backend integration, side effects, and business rules.
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
   - Use `.agents/skills/axis-frontend-feature/SKILL.md` (`$axis-frontend-feature`) for frontend route, server-state loading, prefetch, mutation-cache, and URL-state workflow; keep forms in RHF + Zod.

8. Update status docs when behavior/status changes.
   - Update the use-case `Implementation status` callout; status values are `Done`, `Partial`, `Not started`, and `N/A`.
   - Reconcile the layer table against the AC/AT matrix boundaries, changed paths, and the sibling `{slug}.evidence.md` sidecar. A layer touched by the slice or required by an in-scope AT row cannot be `N/A`; mark it `Done` only when its required evidence passed, `Partial` when implemented with gaps, and `Not started` when the spec requires it but no implementation exists.
   - Use `$axis-visual-artifact` when Mermaid diagrams or committed visual artifacts changed.
   - Update the domain README or [docs/use-cases/README.md](../../../docs/use-cases/README.md) only when their summarized status changes.
   - Name exact deferred AC bullets under `Deferred follow-ups`; use `N/A` when none.
   - Update `Verification` with the required AT evidence categories; put exact evidence paths and runner commands in `{slug}.evidence.md`, not the spec.
   - Do not introduce intentional shortcuts.

9. Verify honestly.
   - During development, run the smallest check that proves the touched surface.
   - Run every required AT row's runner or targeted test before claiming the use case/layer is complete.
   - Update `{slug}.evidence.md` before claiming complete status; each required AT needs committed evidence paths and exact Axis wrapper commands. Group comma-separated AT IDs in one evidence row only when those proof cells are identical.
   - Report acceptance evidence by AT ID (`AT-001 Playwright passed`, `AT-002 xUnit API passed`, etc.).
   - Use `.agents/skills/axis-ready-review/SKILL.md` (`$axis-ready-review`) before asking for review; it owns `python scripts/axis.py ready-review` when triggered.
   - Before pushing committed implementation to a branch with an upstream, open PR, or PR intent, read `$axis-pull-request`; treat it as a branch/diff update.
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
