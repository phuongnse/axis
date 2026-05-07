# F01 — Execution Management

[← Back to E06](../README.md)

---

## Description

The engine manages the full lifecycle of a workflow execution — from creation through completion, failure, or cancellation. Each execution is a runtime instance of a workflow definition.

---

## User Stories

### US-090 — Start a workflow execution

**As a** user or system, **I want to** start a workflow execution **so that** the defined process begins running.

**Acceptance Criteria:**

*Happy path*
- [ ] All trigger types (Manual, Schedule, Webhook, Event) create an Execution record with status `PENDING` before any step runs.
- [ ] The execution ID is returned to the caller immediately (for Manual and Webhook triggers); execution proceeds asynchronously.
- [ ] The engine loads the workflow definition at the moment of trigger (not at publish time) to pick up the latest published version.
- [ ] Within 5 seconds of trigger, the first step begins executing and the execution status transitions to `RUNNING`.

*Validation & errors*
- [ ] Attempting to trigger an Archived or Draft workflow returns HTTP 422: "This workflow cannot be triggered. Status: {status}."
- [ ] If the workflow has no configured trigger matching the incoming trigger type, the request is rejected with HTTP 422.
- [ ] If the required input variables for a Manual trigger are missing, the trigger is rejected with HTTP 422 and structured field errors.

*Edge cases*
- [ ] If the engine crashes between creating the Execution record (PENDING) and starting the first step, a recovery job detects stale PENDING executions (older than 60 seconds) and retries or marks them as Failed.
- [ ] A workflow triggered multiple times in rapid succession creates independent executions; there is no implicit deduplication except for Schedule triggers (see max_concurrent_runs).

*Out of scope*
- Triggering a specific version of a workflow (other than the current active version) — not in MVP.

---

### US-091 — Track execution status in real time

**As an** Organization Member, **I want to** see the live status of a running execution **so that** I know where it is in the process.

**Acceptance Criteria:**

*Happy path*
- [ ] Execution detail page shows: status badge, total elapsed time (live counter for running executions), input payload, trigger type, and triggered by.
- [ ] Step timeline shows all steps with: name, type icon, status (Pending / Running / Completed / Failed / Skipped / Waiting), start time, and duration.
- [ ] Status updates are pushed via SignalR; the page refreshes step statuses without a full reload.
- [ ] Completed steps can be expanded to show their output data.

*Validation & errors*
- [ ] If the SignalR connection drops, the page falls back to polling (every 5 seconds) and shows a "Reconnecting…" indicator.
- [ ] If the execution does not exist or belongs to a different tenant, the page returns a 404 error page.

*Edge cases*
- [ ] For a Parallel Group, the timeline shows the group container and all parallel steps beneath it, each with their own status.
- [ ] A Form step in WAITING status shows the assignee name and a "Waiting for: {assignee}" label.
- [ ] Very long executions (running for hours): the elapsed time counter and timeline remain accurate; no frontend timeout occurs.

*Out of scope*
- Real-time execution graph overlay on the workflow canvas — not in MVP (timeline list only).

---

### US-092 — Cancel a running execution

**As an** Organization Member with `execution:cancel`, **I want to** cancel a running execution **so that** I can stop a process that is no longer needed.

**Acceptance Criteria:**

*Happy path*
- [ ] "Cancel" button appears on the execution detail page when status is `RUNNING` or `WAITING`.
- [ ] Clicking Cancel shows a confirmation dialog: "Are you sure you want to cancel this execution? This cannot be undone."
- [ ] After confirmation, the execution transitions to `CANCELLED` within 10 seconds.
- [ ] Pending Wolverine jobs for cancelled executions are abandoned before they run.
- [ ] Active Form Tasks for the cancelled execution are marked `CANCELLED`; their form links show: "This workflow has been cancelled."

*Validation & errors*
- [ ] Attempting to cancel a `COMPLETED`, `FAILED`, or already `CANCELLED` execution returns HTTP 422: "Cannot cancel an execution with status: {status}."
- [ ] A non-authorized user who calls the cancel API gets HTTP 403.

*Edge cases*
- [ ] Cancelling an execution during a step that is currently executing (e.g., an HTTP Request step in-flight): the step is allowed to finish its current operation, then the engine marks it as Cancelled and stops. Steps do not die mid-operation.
- [ ] Completed steps in a cancelled execution retain their outputs in the execution history.
- [ ] A concurrent cancel request (two users clicking Cancel at the same time) is handled idempotently: only one cancellation takes effect.

*Out of scope*
- Pausing an execution and resuming it — not in MVP (cancel only).
