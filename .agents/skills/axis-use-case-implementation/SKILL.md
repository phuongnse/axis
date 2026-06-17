---
name: axis-use-case-implementation
description: Implement Axis use-case slices from specs through tests, code, documentation status, and verification. Use when adding or fixing behavior for a use case in docs/use-cases, touching module Domain/Application/Infrastructure/API/Frontend layers, or closing implementation-status gaps.
---

# Axis Use Case Implementation

## Goal

Ship one reviewable Axis use-case slice without losing acceptance criteria, layer order, docs status, or verification honesty.

## Workflow

1. Locate the owning spec.
   - Read `docs/use-cases/README.md`, the domain `README.md`, and the use-case file.
   - If the use case is unclear, search with `rg -n "<feature words>" docs/use-cases src tests frontend`.
   - Read `docs/WORKAROUNDS.md` for entries touching the same module or files.

2. Run `$axis-design-gate` for non-trivial work.
   - Stop for high-risk sign-off before code.
   - Carry the dossier decisions into the implementation.

3. Build the AC map.
   - Copy each in-scope `- [ ]` acceptance criterion into a row.
   - Include out-of-scope bullets as `N/A this PR`.
   - Give every in-scope row a proving file or test name before coding.
   - Do not mark a layer complete when validation, edge, isolation, or rollback ACs remain open.

4. Work in layer order.
   - Domain, then Application, then Infrastructure, then API, then Frontend.
   - Before API work, search open lower-layer gaps in `docs/use-cases`.
   - Cross-module work must use Kafka events, RabbitMQ commands/jobs/saga steps, or gRPC contracts. Do not add in-process calls across modules.

5. Use TDD where behavior changes.
   - Add or update the proving test first when practical.
   - Use .NET test names shaped as `{Subject}_{Condition}_{ExpectedOutcome}`.
   - Do not add skips, weaken assertions, or mock away the behavior under test.

6. Implement narrowly.
   - Follow same-module patterns before inventing abstractions.
   - Use `Result` / `Result<T>` for business failures.
   - Keep endpoint logic thin: route binding, `mediator.Send`, and problem-details mapping.
   - Keep frontend server state in TanStack Query and forms in RHF + Zod.

7. Update status docs when behavior/status changes.
   - Update the use-case `Implementation status` callout.
   - Update the domain README and `docs/PROGRESS.md` only when their summarized status changes.
   - Name exact deferred AC bullets under `Deferred follow-ups`; use `N/A` when none.
   - Add or update `docs/WORKAROUNDS.md` only for intentional P0/P1 workarounds.

8. Verify honestly.
   - During development, run the narrow checks for the touched surface.
   - Before review, run `python scripts/axis.py verify` when triggered.
   - Use `$axis-ready-review` before asking for review.
   - Do not claim the full suite unless full `dotnet test Axis.sln --nologo` ran.

## Output

When finishing, report:

```text
Implemented:
- ...

Tests / checks:
- command -> pass/fail/not run with reason

Docs:
- updated / not triggered with reason

Gaps and deferred follow-ups:
- ...
```
