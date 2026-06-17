# Use case — View pending form tasks

> **Navigation**: [← Form Builder](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

See a list of all form tasks assigned to me so that I don't miss any pending actions.

## Primary actor

- Workspace Member

## Trigger

- User initiates: see a list of all form tasks assigned to me

## Main flow

1. Actor starts the — View pending form tasks flow from the relevant Axis screen or API.
2. System checks workspace access, validates the request, and applies the documented acceptance criteria.
3. Actor sees the resulting data, confirmation, or actionable error for the flow.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

When a workflow reaches a Form step, the engine creates a Form Task and notifies the assignee. The assignee opens a unique link, fills the form, and submits it. The engine then validates and continues the workflow.

## Acceptance Criteria

*Happy path*
- [ ] "My Tasks" page (accessible from the top navigation) lists all pending Form Tasks assigned to the current user.
- [ ] Each task shows: form name, workflow name, assigned at, timeout/due time (if set), and a direct link to the form.
- [ ] Default sort: oldest first (most urgent).
- [ ] A separate "Completed" tab shows submitted tasks.

*Validation & errors*
- [ ] If the tasks list fails to load, an error state with a "Retry" button is shown.

*Edge cases*
- [ ] Tasks assigned by role (where the user is a member of that role) also appear in "My Tasks."
- [ ] A task that was submitted by another role member (and thus no longer pending) disappears from the user's "My Tasks" within 60 seconds (polling or SignalR push).
- [ ] Expired tasks (timed out) appear in a separate "Expired" tab, not in Pending.

*Out of scope*
- Delegating a task to another user.
- Bulk task completion.

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
> - Frontend: "My Tasks" page lists pending Form Tasks assigned to the current user.
> - Frontend: each task shows form name, workflow name, assigned at, timeout/due time, and a direct link.
> - Frontend: completed and expired task tabs are not shipped yet.
> - WorkflowEngine/FormBuilder: tasks assigned by role appear for role members.
> - WorkflowEngine/FormBuilder: a task submitted by another role member disappears within 60 seconds via polling or SignalR push.
>
> **Done:** `GetMyFormTasksQuery` + authenticated list endpoints.
>
> **Deferred follow-ups:** none; unresolved work is tracked in Gaps vs spec.
>
> **Decisions:** N/A - no implementation-specific decision recorded for this slice.
>

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |
