# Use case — Handle form step timeout

> **Navigation**: [← Form Builder](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Configure a timeout on a Form step so that the workflow doesn't wait indefinitely.

## Primary actor

- workflow designer

## Trigger

- User initiates: configure a timeout on a Form step

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

When a workflow reaches a Form step, the engine creates a Form Task and notifies the assignee. The assignee opens a unique link, fills the form, and submits it. The engine then validates and continues the workflow.

## Acceptance Criteria

*Happy path*
- [ ] Timeout is configured in hours (1–720) in the Form step config panel.
- [ ] When the timeout expires, a Wolverine scheduled job marks the Form Task as `Expired` and the step as `Failed`.
- [ ] The workflow failure flow is triggered (error notification sent, execution marked `Failed`).

*Validation & errors*
- [ ] Timeout value must be a positive integer between 1 and 720.
- [ ] A Form step without a timeout configured waits indefinitely (no timeout is a valid configuration).

*Edge cases*
- [ ] If the form is submitted within the timeout window, the scheduled expiry job is cancelled.
- [ ] Expiry jobs are idempotent: if the job fires more than once (at-least-once delivery), the second invocation detects the task is already expired and exits gracefully.
- [ ] If the workflow is cancelled while a Form Task is pending, the task is marked `Cancelled` and the expiry job is cancelled. The form link shows: "This workflow has been cancelled."

*Out of scope*
- Sending a reminder notification before timeout expires — not in MVP.

> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Domain | ✅ |
> | Application | ⚠️ |
> | Infrastructure | ✅ |
> | API | ⏳ |
> | Frontend | ⏳ |
>
> **Gaps vs spec:** 
>
> **Done:**
> - `ExpireFormSubmissionMessage` scheduled from `FormStepReachedHandler`
> - `ExpireFormSubmissionHandler` marks submission expired. Workflow execution → `Failed` + error notification on expiry pending workflow-engine coordination.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| form-submission | [source](../wireframes/form-submission.excalidraw) | [preview](../wireframes/form-submission.svg) |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
