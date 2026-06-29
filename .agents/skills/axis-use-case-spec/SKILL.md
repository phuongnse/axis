---
name: axis-use-case-spec
description: Draft or complete Axis docs-first use-case specifications before implementation. Use when a requested feature or use case lacks a docs/use-cases README, acceptance criteria, flow, implementation status, design-source or diagram inventory, or has unclear product/design decisions that must be resolved before using axis-use-case-implementation.
---

# Axis Use Case Spec

## Goal

Create or tighten the owning use-case spec so implementation can follow spec -> design requirements -> tests -> code -> verification without invented behavior, blank AC rows, placeholder docs, or missing visual artifacts.

## Hard gates

Follow [reference.md](../reference.md).
- Do not route to `$axis-use-case-implementation` while blocking decisions remain open.
- Do not invent product behavior, IDs, endpoints, or tables.
- New domain without user scope approval: stop and ask.

## Inputs

- User feature request, domain candidate, and use-case slug when known.
- Existing related use-case docs, code, tests, and product decisions found through `rg`.
- Blocking decisions needed from the user before behavior becomes required.

## Workflow

1. Locate or create the owning spec.
   - Read [docs/use-cases/README.md](../../../docs/use-cases/README.md) and the domain `README.md`.
   - Search existing docs and code with `rg -n "<feature words>" docs/use-cases src tests frontend`.
   - If no use-case folder exists, create `docs/use-cases/{domain}/{slug}/README.md` from the template and add the domain README link.
   - If the domain itself does not exist, stop and ask for scope unless the user explicitly approved a new domain.

2. Establish product boundaries before writing behavior.
   - Capture Purpose, Primary actor, Trigger, Main flow, Alternate/error flows, Acceptance Criteria, Acceptance Test Matrix, Out Of Scope, Design System, Design Sources, implementation status, and Decisions.
   - Use the source priority from [AGENTS.md](../../../AGENTS.md): use-case ACs, then AGENTS, then focused owner docs, then same-module code, then agent judgment.
   - Ask the user for blocking decisions such as authorization model, data ownership, API exposure, integration effects, or UI journey.
   - Do not invent IDs, endpoints, table names, or copy that changes product meaning.
   - Use `## Out Of Scope` only for behavior the flow references or hands off to but does not own.
   - Do not list unrelated future features as out of scope.
   - Treat the spec as ready only when each in-scope required-to-close AC has a cited actor, entry point, precondition, observable outcome, business side effect, failure/validation behavior, and testable expected result.
   - Cite readiness evidence by spec section, AC ID, or flow step. If an expected behavior cannot be cited from the spec, record it under Open decisions instead of turning it into a test expectation.

3. Write acceptance criteria and acceptance tests for implementation.
   - Follow [reference.md](./reference.md) for AC IDs, Acceptance Test Matrix shape, runner choice, and slice boundaries.

4. Define design requirements and visual evidence.
   - Add `## Design System` with `Surface | Contract` rows for required UI primitives, states, accessibility, and visual contracts; use one `N/A` row only when no UI surface exists.
   - If no approved design-system implementation exists, state the missing requirement in `## Design System` and keep the affected frontend status incomplete.
   - For user-facing screens, use `$axis-visual-artifact` to create or update design-source links, optional previews, and the `## Design Sources` table.
   - Add `## Screen flow` when the journey has more than three screens, branched happy paths, or non-obvious error screens.
   - Add Mermaid diagrams for non-trivial workflow or sequence behavior; use use-case vocabulary and keep local diagrams in the owning README.
   - Use a single `N/A` row when no design source or local diagram applies.

5. Mark implementation status honestly.
   - Add the `> **Implementation status**` callout after visual sections using the template layer table.
   - Use only `Done`, `Partial`, `Not started`, or `N/A`.
   - Do not mark a layer or use case done if an in-scope AC lacks a required AT row or required automated evidence is missing.
   - Name exact deferred AC bullets under `Deferred follow-ups`, or write `N/A`.
   - Add `Verification` naming the required AT runners; passing evidence belongs in the implementation or ready-review report.
   - Update the domain README Open work only when the new spec changes prioritized work.

6. Route follow-up implementation.
   - Use `$axis-api-contract` for new or changed REST/OpenAPI/API type surfaces.
   - Use `$axis-frontend-feature` for SPA routes, feature folders, forms, data fetching, or UI behavior.
   - Use `$axis-use-case-implementation` only after the owning spec exists and blocking decisions are resolved.

7. Verify only what changed.
   - Use `python scripts/axis.py check use-case-docs` for use-case README shape.
   - Use `python scripts/axis.py check markdown-links` only when links or anchors changed.
   - Use `$axis-visual-artifact` when design-source links, previews, Mermaid, or visual docs changed.
   - Leave full ready-review verification to `$axis-ready-review`.

## Output

Report:

```text
Spec created/updated:
- ...

Decisions resolved:
- ...

Open decisions:
- ...

Visual artifacts:
- created/updated/N/A

Next skill:
- $axis-use-case-implementation / $axis-api-contract / $axis-frontend-feature / none yet

Checks:
- command -> pass/fail/not run with reason
```
