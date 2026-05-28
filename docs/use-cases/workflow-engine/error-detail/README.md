# Use case — View detailed error information

> **Navigation**: [← Workflow Engine](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

See the full error details of a failed step so that I can understand what went wrong.

## Primary actor

- Organization Member with `execution:read`

## Trigger

- User initiates: see the full error details of a failed step

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
> - `GetExecutionQuery` (returning step-level error details) not yet implemented
> - error detail UI (stack trace, redacted fields) pending Frontend + API.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| execution-detail | [source](../wireframes/execution-detail.excalidraw) | [preview](../wireframes/execution-detail.svg) |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
