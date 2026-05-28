# Use case — Handle form step timeout

> **Navigation**: [← Form Builder](./README.md)

## Purpose

configure a timeout on a Form step so that the workflow doesn't wait indefinitely.

## Primary actor

- workflow designer

## Trigger

- User initiates: configure a timeout on a Form step

## Main flow

1. _(Happy path — align with acceptance criteria below.)_

## Alternate / error flows

- See *Validation & errors* and *Edge cases* under Acceptance Criteria.

## Context

When a workflow reaches a Form step, the engine creates a Form Task and notifies the assignee. The assignee opens a unique link, fills the form, and submits it. The engine then validates and continues the workflow.

---

## Acceptance Criteria

**Purpose:** _(to be detailed during migration)_
**Primary actor:** _(to be detailed during migration)_
**Trigger:** _(to be detailed during migration)_

#### Main flow
1. _(to be detailed during migration)_

#### Alternate / error flows
- _(to be detailed during migration)_



**Acceptance Criteria:**

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


## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| form-submission | [source](./wireframes/form-submission.excalidraw) | [preview](./wireframes/form-submission.svg) |

[← Back to Form Builder](./README.md)

---

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
