# F05 — Manual Retry

[← Back to E06](../README.md)

---

## Description

When a workflow execution fails at a step, users can manually retry from the failed step. Previously successful steps are not re-run; their outputs are carried forward from the original execution.

---

## User Stories

### US-100 — Retry a failed execution

**As an** Organization Member with `execution:retry`, **I want to** retry a failed execution from the point of failure **so that** I don't have to re-run steps that already succeeded.

**Acceptance Criteria:**

*Happy path*
- [ ] "Retry" button appears on the execution detail page when status is `FAILED`.
- [ ] Clicking Retry creates a new Execution record (status: `PENDING`) linked to the original via a `retry_of_execution_id` field.
- [ ] The retry loads the context snapshot from just before the failed step, skips all previously completed steps, and re-runs from the failed step onward.
- [ ] If the retry succeeds, it is marked `COMPLETED`. If it fails again, it is marked `FAILED` and can be retried again.

*Validation & errors*
- [ ] Retrying an execution with status other than `FAILED` is blocked: "'Retry' is only available for failed executions."
- [ ] Retrying an execution whose workflow definition has been archived since the original run shows a warning: "The workflow has been archived. The retry will use the last active version." The retry proceeds with the archived definition.
- [ ] A user without `execution:retry` permission does not see the Retry button and gets HTTP 403 from the API.

*Edge cases*
- [ ] If the failed step's configuration was changed in the workflow builder since the original execution, the retry uses the updated step config (not the original). A warning is shown: "The workflow definition has changed since this execution. The retry may behave differently."
- [ ] Retrying a workflow where the failed step referenced a form that has since been deleted: the retry fails immediately at that step with "Referenced form no longer exists."
- [ ] Multiple concurrent retries of the same execution are prevented: the Retry button is disabled while a retry is already in progress.

*Out of scope*
- Automatic retry (without user action) — not in MVP.

---

### US-101 — View retry history

**As an** Organization Member with `execution:read`, **I want to** see the retry history of a failed execution **so that** I can track how many times it has been retried.

**Acceptance Criteria:**

*Happy path*
- [ ] Execution detail page shows a "Retry history" section listing all retries in chronological order.
- [ ] Each entry shows: attempt number, status, started at, completed at, triggered by.
- [ ] Each entry is a link to that retry's own execution detail page.
- [ ] The original execution and all its retries are interlinked (each shows its parent and children).

*Validation & errors*
- [ ] If the retry history fails to load, it shows an error state in the section (not a full page error).

*Edge cases*
- [ ] A retry that was itself retried creates a chain: Original → Retry 1 → Retry 2 → ... All are shown in the retry history of the original execution.
- [ ] There is no maximum retry count imposed by the platform; the user can retry as many times as needed.

*Out of scope*
- Comparing two retry attempts side-by-side — not in MVP.

---

### US-102 — Retry with modified input context

**As an** Organization Member with `execution:retry`, **I want to** modify the execution context before retrying **so that** I can fix data errors that caused the original failure.

**Acceptance Criteria:**

*Happy path*
- [ ] "Retry with modified context" option (secondary action next to Retry) opens a JSON editor pre-populated with the execution context at the point of failure.
- [ ] The user edits the JSON and clicks "Start retry."
- [ ] The retry uses the modified context starting from the failed step.
- [ ] The execution history records that the retry used a modified context (a "Context modified by user" flag in the execution detail).

*Validation & errors*
- [ ] The context editor validates that the JSON is valid before the retry can be started; invalid JSON shows: "Context must be valid JSON."
- [ ] Removing a context key that is required by the failed step is allowed — the retry may fail again with a different error, which is the user's responsibility.

*Edge cases*
- [ ] The modified context is not restricted to keys relevant to the failed step; the user can modify any key. The full modified context is used for all remaining steps.
- [ ] A very large context (> 1 MB) may be slow to render in the JSON editor; the editor handles up to 5 MB.

*Out of scope*
- Structured field-by-field editing of context (showing fields by step/variable name) — not in MVP; raw JSON editor only.
