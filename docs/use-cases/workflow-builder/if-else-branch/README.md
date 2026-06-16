# Use case — Add an if/else branch

> **Navigation**: [← Workflow Builder](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Route my workflow down different paths based on a condition so that different scenarios are handled appropriately.

## Primary actor

- Team account Member

## Trigger

- Route my workflow down different paths based on a condition.

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Workflows can take different execution paths based on data values using Condition steps. This enables if/else and multi-branch switch logic.

## Acceptance Criteria

*Happy path*
- [ ] Adding a Condition step creates it with two default outgoing handles: "If true" and "If false."
- [ ] Each handle can be connected to different subsequent steps.
- [ ] The expression builder (see [Condition step](./README.md)) is used to define the condition.
- [ ] Canvas edges show the branch label ("If true" / "If false") next to the arrow.

*Validation & errors*
- [ ] A Condition step with only one outgoing connection (missing the other branch) blocks publishing with: "The Condition step '{name}' must have at least 2 outgoing branches."

*Edge cases*
- [ ] Both branches of an if/else can converge back to the same downstream step (diamond pattern). The downstream step executes once, whichever branch reaches it first.
- [ ] A Condition step's expression can reference output from any preceding step in the workflow, not only the immediately previous step.

*Out of scope*
- Loop-back branching (sending execution back to an earlier step) — cycles are blocked.

> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Domain | ✅ |
> | Application | ✅ |
> | Infrastructure | ✅ |
> | API | ✅ |
> | Frontend | ⏳ |
>
> **Gaps vs spec:** canvas branch label rendering pending Frontend; branch evaluation at execution time pending workflow-engine.
>
> **Decisions:** cycle detection implemented in domain (DFS reachability check in AddTransition). `Transition` is a value object (only `fromStepId`, `toStepId`, `condition` — no identity or ordering fields).

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

