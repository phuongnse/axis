# F03 — Error Handling & Notification

[← Back to E06](../README.md)

---

## Description

When a step fails, the engine marks the execution as `FAILED`, records full error details, and notifies configured channels. The execution halts; users investigate and retry manually.

---

## User Stories

### US-094 — Receive error notification when a workflow fails

**As an** Organization Member, **I want to** be notified when a workflow execution fails **so that** I can investigate and take action.

**Acceptance Criteria:**

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

> **Implementation status** — Domain: ✅ | Application: ⚠️ | Infrastructure: ✅ | API: ⏳ | Frontend: ⏳
> Gaps vs spec: no Application-layer notification dispatch handler; `ExecutionFailed` domain event raised but notification channels not wired; email/in-app/webhook dispatch and rate-limiting pending Application layer + a future cross-cutting notification service (outside WorkflowEngine Infrastructure, which is complete).

---

### US-095 — View detailed error information

**As an** Organization Member with `execution:read`, **I want to** see the full error details of a failed step **so that** I can understand what went wrong.

**Acceptance Criteria:**

*Happy path*
- [ ] Failed step in the execution timeline is highlighted in red with an error icon.
- [ ] Clicking the failed step shows: error type, error message, and the timestamp of failure.
- [ ] "Technical details" collapsible section shows: full stack trace (if available), the step's input context at the time of failure (redacted for sensitive fields like auth tokens).

*Validation & errors*
- [ ] If error details are not available (e.g., infrastructure-level failure with no captured exception), the error section shows: "An unexpected error occurred. No additional details are available."

*Edge cases*
- [ ] HTTP Request step failure: shows the request URL (with auth headers omitted), response status code, and response body (truncated at 2 KB for display).
- [ ] Script step failure: shows the script that ran (truncated at 200 lines), the thrown exception type, message, and line number.
- [ ] Condition step failure: shows the expression that was evaluated and why it failed (e.g., "Cannot compare null to string").
- [ ] Sensitive values (auth tokens, API keys) are never shown in error details — they are replaced with `[REDACTED]`.

*Out of scope*
- Sharing a link to a specific error detail view with another user — not in MVP.

> **Implementation status** — Domain: ✅ | Application: ⚠️ | Infrastructure: ✅ | API: ⏳ | Frontend: ⏳
> Gaps vs spec: `GetExecutionQuery` (returning step-level error details) not yet implemented; error detail UI (stack trace, redacted fields) pending Frontend + API.

---

### US-096 — Configure error notification channels per workflow

**As an** Organization Member with `workflow:definition:write`, **I want to** configure who gets notified when my workflow fails **so that** the right people are alerted.

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

> **Implementation status** — Domain: ⚠️ | Application: ⚠️ | Infrastructure: ⏳ | API: ⏳ | Frontend: ⏳
> Gaps vs spec: error notification channel config is not modeled in the domain (no channel list on `WorkflowExecution`); no `UpdateErrorNotificationChannelsCommand` handler; notification channel configuration UI and per-workflow channel storage pending API + Frontend.
