# Use case — Configure a Form step

> **Navigation**: [← Workflow Builder](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Configure a Form step with a specific form and assignee so that the right person receives the form during execution.

## Primary actor

- Organization Member

## Trigger

- Configure a form step with a specific form and assignee.

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Each step has a type that determines what it does when executed. Users configure each step type through the canvas side panel.

## Acceptance Criteria

*Happy path*
- [ ] Form picker shows a searchable list of all forms in the org.
- [ ] Assignee field accepts: a specific user (by name/email search), a role name (all members of that role are notified), or a context expression like `{{context.step_id.submitted_by}}`.
- [ ] Optional timeout field accepts a number of hours (1–720). When set, the step auto-fails if not submitted within that period.
- [ ] Step node on the canvas shows the selected form name and assignee as a summary.

*Validation & errors*
- [ ] Saving without selecting a form is blocked: "A form must be selected."
- [ ] Saving without setting an assignee is blocked: "An assignee is required."
- [ ] An invalid context expression in the assignee field (e.g., mismatched braces) shows: "Invalid expression syntax."
- [ ] If the selected form is deleted after the step is configured, the step node shows a broken indicator and publishing is blocked.

*Edge cases*
- [ ] If the assignee resolves to a deactivated user at execution time, the engine falls back to notifying all Admins and logs a warning.
- [ ] A timeout of 0 hours is invalid and blocked.

*Out of scope*
- Multiple assignees on a single Form step (assign to all and wait for the first response).

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
> **Gaps vs spec:** form picker UI, assignee expression evaluation, and timeout enforcement pending Frontend + workflow-engine.
>
> **Deferred (PR #146 follow-up):** Multiple assignees on one Form step (assign to all, first response wins).
>
> **Decisions:** step config (formId, assignee, timeout) stored as JSONB dict in `steps` column. `StepType` enum includes `Start` and `End` values.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
