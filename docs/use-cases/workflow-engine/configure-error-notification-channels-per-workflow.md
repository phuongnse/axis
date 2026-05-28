# Use case — Configure error notification channels per workflow

> **Navigation**: [← Workflow Engine](./README.md)

## Purpose

configure who gets notified when my workflow fails so that the right people are alerted.

## Primary actor

- Organization Member with `workflow:definition:write`

## Trigger

- User initiates: configure who gets notified when my workflow fails

## Main flow

1. _(Happy path — align with acceptance criteria below.)_

## Alternate / error flows

- See *Validation & errors* and *Edge cases* under Acceptance Criteria.

## Context

When a step fails, the engine marks the execution as `FAILED`, records full error details, and notifies configured channels. The execution halts; users investigate and retry manually.

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
- [ ] Workflow settings tab has an "Error Notifications" section (separate from the trigger config).
- [ ] Available channels to add: specific users (search by name/email), roles (all members of the role), webhook URL.
- [ ] Multiple channels can be configured; all receive the notification on failure.
- [ ] Configuration is saved per workflow; changes take effect for the next failure.

*Validation & errors*
- [ ] Webhook URL: must be a valid HTTPS URL.
- [ ] At least one channel must remain configured (removing the last channel prompts: "At least one notification channel is recommended. Continue anyway?").

*Edge cases*
- [ ] If a configured user is deactivated, their channel is ignored at notification time (no error; a warning is logged).
- [ ] If a configured role has no members, the role channel is skipped (no error; a warning is logged).

*Out of scope*
- Different notification channels for different failure scenarios (e.g., "only notify on HTTP step failures") — not in MVP; all failures use the same channels.

> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Domain | ⚠️ |
> | Application | ⚠️ |
> | Infrastructure | ⏳ |
> | API | ⏳ |
> | Frontend | ⏳ |
>
> **Gaps vs spec:**
> - error notification channel config is not modeled in the domain (no channel list on `WorkflowExecution`)
> - no `UpdateErrorNotificationChannelsCommand` handler
> - notification channel configuration UI and per-workflow channel storage pending API + Frontend.


## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| execution-detail | [source](./wireframes/execution-detail.excalidraw) | [preview](./wireframes/execution-detail.svg) |

[← Back to Workflow Engine](./README.md)

---

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
