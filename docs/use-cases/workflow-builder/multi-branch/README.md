# Use case — Add a multi-branch condition

> **Navigation**: [← Workflow Builder](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Add more than two branches from a Condition step so that I can handle multiple distinct cases.

## Primary actor

- Workspace Member

## Trigger

- Add more than two branches from a condition step.

## Main flow

1. Actor starts the — Add a multi-branch condition flow from the relevant Axis screen or API.
2. System checks workspace access, validates the request, and applies the documented acceptance criteria.
3. Actor sees the resulting data, confirmation, or actionable error for the flow.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Workflows can take different execution paths based on data values using Condition steps. This enables if/else and multi-branch switch logic.

## Acceptance Criteria

*Happy path*
- [ ] "+ Add branch" button in the Condition step config panel adds an additional named branch with its own expression.
- [ ] Each branch has a user-defined label (editable) and an expression.
- [ ] A "Default" branch (no expression) can be added; at most one default branch per step.
- [ ] Branches are evaluated in the order shown; the first matching branch wins.
- [ ] Branch order can be changed via drag-and-drop within the config panel.

*Validation & errors*
- [ ] Attempting to add a second Default branch is blocked: "Only one default branch is allowed."
- [ ] A branch without a label is blocked: "Branch label is required."

*Edge cases*
- [ ] If no branch matches and there is no Default branch, the step fails at execution time with: "No condition branch matched and no default branch is configured."
- [ ] A Condition step with 10 or more branches still renders correctly on the canvas, with the config panel scrollable.

*Out of scope*
- Regex-based branch matching.

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
> **Gaps vs spec:** branch drag-to-reorder UI and default-branch validation at publish pending Frontend + API.
>
> **Deferred follow-ups:**
> - N/A
>
> **Decisions:**
> - N/A

## Design Sources

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

