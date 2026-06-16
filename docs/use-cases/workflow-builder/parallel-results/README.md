# Use case — Access results from parallel branches

> **Navigation**: [← Workflow Builder](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Use the output of all parallel steps in subsequent steps so that I can combine results.

## Primary actor

- Workspace Member

## Trigger

- Use the output of all parallel steps in subsequent steps.

## Main flow

1. Actor starts the — Access results from parallel branches flow from the relevant Axis screen or API.
2. System checks workspace access, validates the request, and applies the documented acceptance criteria.
3. Actor sees the resulting data, confirmation, or actionable error for the flow.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Multiple steps can run concurrently inside a Parallel Group. The workflow fans out, runs them in parallel, and waits for all (or any) to complete before continuing.

## Acceptance Criteria

*Happy path*
- [ ] After the Parallel Group completes, execution context contains outputs from all completed branches, namespaced by step ID: `{{context.step_a.field}}`, `{{context.step_b.field}}`.
- [ ] Subsequent steps (after the group) can reference these values in expressions and config fields.

*Validation & errors*
- [ ] Referencing a parallel branch's output in a step that is itself inside the same Parallel Group (sibling branch) is blocked at design time: the context variable picker does not offer sibling branch outputs.

*Edge cases*
- [ ] With OR join, branches that did not complete (were cancelled) have `null` values in the context under their step IDs.
- [ ] If two parallel branches write to the same output variable name (via Script steps), the value from whichever branch completes last wins. A design-time warning is shown when this is detected.

*Out of scope*
- Merging/reducing outputs from parallel branches with built-in aggregation functions — use a Script step after the group for custom aggregation.

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
> **Gaps vs spec:**
> - context namespacing by step ID and sibling-output blocking pending workflow-engine
> - design-time duplicate output warning pending.
>
> **Deferred follow-ups:**
> - N/A
>
> **Decisions:**
> - N/A

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

