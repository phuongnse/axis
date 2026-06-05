# Use case — Configure a Condition step

> **Navigation**: [← Workflow Builder](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Add a Condition step so that my workflow can take different paths based on data values.

## Primary actor

- Organization Member

## Trigger

- Add a condition step.

## Main flow

1. Member selects a Condition step on the canvas and opens its configuration panel.
2. System lets the member define ordered branches with context variables, operators, labels, and an optional Default branch.
3. Member saves the step configuration; the canvas shows labeled outgoing branches in evaluation order.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Each step has a type that determines what it does when executed. Users configure each step type through the canvas side panel.

## Acceptance Criteria

*Happy path*
- [ ] Expression builder UI (no raw code) supports: field comparisons (`==`, `!=`, `<`, `>`, `<=`, `>=`, `contains`, `starts with`, `ends with`, `is empty`, `is not empty`) and logical operators (AND, OR, NOT).
- [ ] Left-hand side of comparison is a context variable picker (shows all variables available at this step's position in the workflow).
- [ ] Each branch has a label (editable) shown on the canvas edge.
- [ ] A "Default" branch (no condition) catches all unmatched cases; at most one default branch per step.

*Validation & errors*
- [ ] Publishing is blocked if a Condition step has fewer than 2 outgoing branches.
- [ ] Publishing is blocked if a Condition step has no Default branch and the conditions may not be exhaustive (non-exhaustive detection is best-effort; a warning is shown, not a hard block).
- [ ] An invalid expression (e.g., comparing a number field with `contains`) shows: "This operator is not valid for the '{field}' type."

*Edge cases*
- [ ] Branch order matters: branches are evaluated top-to-bottom; the first match wins. The canvas side panel shows branches in their evaluation order with drag-to-reorder.
- [ ] A Condition step with only a Default branch (no other conditions) is valid but the canvas shows a warning: "All inputs will follow the default branch."

*Out of scope*
- Raw expression editing (writing code directly) — the visual builder is the only interface.

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
> **Gaps vs spec:** expression builder UI and branch evaluation pending Frontend + workflow-engine; condition branches stored in step config JSONB.
>
> **Deferred:** Raw expression editing (code-only interface) — visual builder only.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |
