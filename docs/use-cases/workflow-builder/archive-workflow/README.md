# Use case — Archive a workflow

> **Navigation**: [← Workflow Builder](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Archive a workflow so that it is disabled but its history is preserved.

## Primary actor

- Workspace Member with `workflow:definition:write`

## Trigger

- User initiates: archive a workflow

## Main flow

1. Actor starts the — Archive a workflow flow from the relevant Axis screen or API.
2. System checks workspace access, validates the request, and applies the documented acceptance criteria.
3. Actor sees the resulting data, confirmation, or actionable error for the flow.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Users can create, view, edit, publish, archive, delete, and duplicate workflow definitions. A workflow definition is the blueprint the execution engine follows when triggered.

## Acceptance Criteria

*Happy path*
- [ ] Archiving moves the workflow to `Archived` status and deactivates all triggers (cron jobs unscheduled, webhook URL deactivated).
- [ ] Running executions at archive time are allowed to complete; no new executions can start.

*Validation & errors*
- [ ] Attempting to archive a `Draft` workflow returns HTTP 422: "Cannot archive a draft workflow."
- [ ] Attempting to trigger an archived workflow via API or webhook returns HTTP 422: "This workflow is archived and cannot be triggered."

*Edge cases*
- [ ] An archived workflow can be unarchived (restored to Active) by any admin.
- [ ] Execution history for an archived workflow is still fully accessible.

*Out of scope*
- Automatic archiving after N days of inactivity.

## Design Sources

| Screen | Source | Preview |
|--------|--------|---------|
| N/A | N/A | N/A |

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
> - trigger deactivation on archive pending workflow-engine integration
> - HTTP 422 on archived-workflow trigger pending.
>
> **Deferred follow-ups:**
> - N/A
>
> **Decisions:**
> - N/A
