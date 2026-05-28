# Use case — Receive form assignment notification

> **Navigation**: [← Form Builder](./README.md)

## Purpose

Be notified when a form is waiting for my input so that I know I have an action to take.

## Primary actor

- assignee

## Trigger

- User initiates: be notified when a form is waiting for my input

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
- [ ] Assignee receives an email within 60 seconds of the Form Task being created, containing: workflow name, form name, due time (if timeout configured), and a direct link to the form.
- [ ] Assignee also receives an in-app notification (bell icon in the nav) if they are a registered platform user.
- [ ] Email and in-app notifications link to the same unique form task URL.

*Validation & errors*
- [ ] If the email service fails to deliver the notification, the Form Task is still created and the engine continues waiting. The failure is logged in the step's execution detail. The admin can resend the notification manually.
- [ ] If the assignee resolves to a role with no members, the engine marks the step as Failed immediately: "No users found with role '{role_name}'."

*Edge cases*
- [ ] If the assignee is a role, all members of that role receive the notification. The form is completed by the first user to submit it; subsequent attempts by other role members are rejected as duplicate.
- [ ] If the assignee is a deactivated user, the step fails immediately (see [Form step](./README.md) deactivated-assignee edge case).

*Out of scope*
- Push notifications (mobile) — not in MVP.
- Escalation notifications if the form is not submitted after X hours — not in MVP (timeout causes failure, not escalation).

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
> - No notification dispatch on assign
> - email/in-app notification pending workflow-engine + notification infrastructure
> - role member resolution pending Identity integration.
>
> **Done:** `FormSubmission` aggregate + `FormStepReachedHandler` creates pending submission with access token.
>
> **Decisions:**
> - `FormSubmission` is a single aggregate combining task assignment (executionId, assigneeUserId, accessToken, expiresAt) and response data (submittedData, submittedAt). A separate FormTask entity would add no domain logic — the relationship is always 1:1 and both live within the same lifecycle (Pending → Submitted/Expired/Cancelled). Status enum is `FormSubmissionStatus`
> - `Submitted` used instead of `Completed` to name the action clearly. `AccessToken` is a `Guid` (unique URL key, not JWT)
> - expiry enforced via `ExpiresAt` + `Expire()` domain method
> - `Expire()` is non-idempotent by design — idempotency handled at the caller level.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| form-submission | [source](./wireframes/form-submission.excalidraw) | [preview](./wireframes/form-submission.svg) |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
