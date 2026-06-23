# Use case — Receive error notification when a workflow fails

> **Navigation**: [← Workflow Engine](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Be notified when a workflow execution fails so that I can investigate and take action.

## Primary actor

- Workspace Member

## Trigger

- User initiates: be notified when a workflow execution fails

## Main flow

1. Actor starts the — Receive error notification when a workflow fails flow from the relevant Axis screen or API.
2. System checks workspace access, validates the request, and applies the documented acceptance criteria.
3. Actor sees the resulting data, confirmation, or actionable error for the flow.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

When a step fails, the engine marks the execution as `FAILED`, records full error details, and notifies configured channels. The execution halts; users investigate and retry manually.

## Acceptance Criteria

*Happy path*
- [ ] Error notification is sent via all configured channels (email, in-app, webhook) within 60 seconds of the failure.
- [ ] Email notification includes: workflow name, execution ID (with a deep link), failed step name, error message summary, and timestamp.
- [ ] In-app notification appears in the bell icon and persists until dismissed.

*Validation & errors*
- [ ] If the email notification itself fails to deliver, the failure is logged but does not create a cascading error (no retry for notification delivery).
- [ ] If no notification channels are configured for the workflow, the failure notification is sent to all workspace Admins by default as a safety net.

*Edge cases*
- [ ] A workflow with multiple parallel branches: if one branch fails (AND join), a single failure notification is sent for the overall execution, not one per failed branch.
- [ ] If the same workflow fails repeatedly in a short period (e.g., schedule trigger firing every 5 minutes and always failing), notifications are rate-limited to 1 per 15 minutes per workflow per channel to avoid notification flooding.

*Out of scope*
- PagerDuty / OpsGenie / Slack integration for error notifications.

## Design Sources

| Screen | Source | Preview |
|--------|--------|---------|
| N/A | N/A | N/A |

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
> - no Application-layer notification dispatch handler
> - `ExecutionFailed` domain event raised but notification channels not wired
> - email/in-app/webhook dispatch and rate-limiting pending Application layer + a future cross-cutting notification service (outside WorkflowEngine Infrastructure, which is complete).
>
> **Deferred follow-ups:**
> - N/A
>
> **Decisions:**
> - N/A
