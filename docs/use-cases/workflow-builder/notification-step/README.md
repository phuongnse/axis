# Use case — Configure a Notification step

> **Navigation**: [← Workflow Builder](./README.md) · [Use cases index](./README.md#use-cases)

## Purpose

Add a Notification step so that stakeholders are informed when a workflow reaches a certain point.

## Primary actor

- Organization Member

## Trigger

- Add a notification step.

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
- [ ] Channel options: Email or Webhook.
- [ ] Email config: recipient(s) (comma-separated emails or context expressions), subject, and body (rich text with `{{expression}}` placeholders).
- [ ] Webhook config: URL (supports expressions), HTTP method (POST only), and an optional JSON payload template.
- [ ] The step does not pause execution; the workflow continues immediately after the notification is dispatched (fire-and-forget).

*Validation & errors*
- [ ] Email recipient: at least one recipient required. Invalid email format shown inline.
- [ ] Webhook URL: required, must be a valid HTTPS URL. HTTP (non-SSL) URLs are blocked for security.
- [ ] Subject: required for email channel, max 200 characters.

*Edge cases*
- [ ] A failed email delivery (SMTP error, invalid address) logs a warning in the execution step detail but does NOT fail the workflow by default. This behavior is configurable per step: a "Fail workflow on notification error" toggle can be enabled.
- [ ] A webhook notification that times out (> 10s) or returns non-2xx follows the same configurable behavior.
- [ ] Expression placeholders that resolve to `null` or undefined are rendered as an empty string in the notification body.

*Deferred capabilities*
- SMS, Slack, or Teams notification channels.
- Notification templates shared across workflows.

> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | Domain | ✅ |
> | Application | ✅ |
> | Infrastructure | ✅ |
> | API | ⚠️ |
> | Frontend | ⏳ |
>
> **Gaps vs spec:** email/webhook dispatch pending workflow-engine; configurable fail-on-error toggle not yet implemented in API layer.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
