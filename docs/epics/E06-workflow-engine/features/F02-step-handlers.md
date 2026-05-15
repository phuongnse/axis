# F02 — Step Execution Handlers

[← Back to E06](../README.md)

---

## Description

Each step type has a dedicated handler that executes it in isolation. Handlers report success or failure back to the engine with their output data.

---

## Handler Specifications

### Form Step Handler

| Attribute | Detail |
|---|---|
| Behavior | Creates a Form Task, notifies assignee, suspends step in `WAITING` state |
| Input | Form ID, assignee expression, pre-population expressions, timeout (hours) |
| Output (on submit) | All submitted field values, merged into execution context |
| Failure conditions | Assignee not found; timeout expires |
| Side effects | Sends email + in-app notification to assignee |

### HTTP Request Handler

| Attribute | Detail |
|---|---|
| Behavior | Makes outbound HTTP call, parses response, stores result in context |
| Input | Method, URL, headers, body, auth config, output variable name, timeout |
| Output | `{ status_code, body (parsed JSON or raw text), headers }` |
| Failure conditions | DNS failure; connection timeout; non-2xx response (configurable) |
| Security | Auth credentials never logged; response body truncated at 1 MB |

### Condition Handler

| Attribute | Detail |
|---|---|
| Behavior | Evaluates expression, returns the matching branch ID |
| Input | Ordered list of branches (each with label and expression), execution context |
| Output | Selected branch ID |
| Failure conditions | Expression evaluation error; no branch matches and no default |
| Security | Expression evaluated in safe evaluator (no `eval()`, no network access) |

### Script Handler

| Attribute | Detail |
|---|---|
| Behavior | Executes sandboxed JavaScript, merges `output` into context |
| Input | Script body, timeout (1–60s) |
| Output | All properties written to the `output` object |
| Failure conditions | Unhandled exception; timeout exceeded; sandbox violation |
| Security | No network, no filesystem, no `process`, no `require`/`import` |

### Notification Handler

| Attribute | Detail |
|---|---|
| Behavior | Sends email or webhook notification; does not pause execution |
| Input | Channel, recipient(s), subject, body template |
| Output | Delivery status (`sent` / `failed`), timestamped |
| Failure conditions | Delivery failure (configurable: fail workflow or log warning) |
| Fire-and-forget | Workflow continues immediately; delivery is async |

---

## User Stories

### US-093 — Step execution is isolated and resilient

**As a** platform operator, **I want** each step handler to be isolated **so that** a failure in one step does not crash the engine or affect other executions.

**Acceptance Criteria:**

*Happy path*
- [ ] Each step handler runs as an independent Wolverine message handler with its own exception boundary.
- [ ] A step handler completing successfully reports `StepCompleted(executionId, stepId, output)` back to the engine via a Wolverine message.
- [ ] Step start time, end time, and duration are recorded for every step.

*Validation & errors*
- [ ] An unhandled exception in any step handler marks only that step as `Failed` — the engine and all other executions continue normally.
- [ ] All step handler exceptions are logged with structured context: `{ tenantId, executionId, stepId, stepType, errorType, errorMessage, stackTrace }`.
- [ ] A step handler that takes longer than 5 minutes (engine-level timeout, separate from step-level config) is forcibly killed and the step is marked Failed with: "Step execution exceeded the maximum allowed time."

*Edge cases*
- [ ] A step handler that loses its DB connection mid-execution retries the DB operation up to 3 times with exponential backoff before failing.
- [ ] Wolverine's at-least-once delivery guarantee: if a step handler message is re-delivered (e.g., after a crash), the handler checks if the step is already in a terminal state (`COMPLETED`, `FAILED`, `CANCELLED`) and exits immediately (idempotent).
- [ ] Two concurrent deliveries of the same step handler message (race condition): the second one detects the step is already `RUNNING` and exits; only one execution proceeds.

*Out of scope*
- Custom step types defined by users — not in MVP.

> **Implementation status** — Domain: ⚠️ | Application: ⚠️ | Infrastructure: ⏳ | API: ⏳ | Frontend: ⏳
> Gaps vs spec: `ExecutionStep` aggregate not yet implemented in domain (required for per-step status, start/end times, and output tracking); no Wolverine step handler message handlers; engine-level 5-minute timeout and idempotency check on re-delivery pending engine implementation layer.
