# Use case — Receive error notification when a workflow fails

> **Navigation**: [← Workflow Engine](./README.md)

## Purpose

Be notified when a workflow execution fails so that I can investigate and take action.

## Primary actor

- Organization Member

## Trigger

- User initiates: be notified when a workflow execution fails

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

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
- [ ] If the email notification itself fails to deliver, the failure is logged but does not create a cascading error (no retry for notification delivery in MVP).
- [ ] If no notification channels are configured for the workflow, the failure notification is sent to all org Admins by default as a safety net.

*Edge cases*
- [ ] A workflow with multiple parallel branches: if one branch fails (AND join), a single failure notification is sent for the overall execution, not one per failed branch.
- [ ] If the same workflow fails repeatedly in a short period (e.g., schedule trigger firing every 5 minutes and always failing), notifications are rate-limited to 1 per 15 minutes per workflow per channel to avoid notification flooding.

*Out of scope*
- PagerDuty / OpsGenie / Slack integration for error notifications — not in MVP.

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

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| execution-detail | [source](./wireframes/execution-detail.excalidraw) | [preview](./wireframes/execution-detail.svg) |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
