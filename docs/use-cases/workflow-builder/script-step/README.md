# Use case — Configure a Script step

> **Navigation**: [← Workflow Builder](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Write a small script step so that I can transform data that isn't possible with standard steps.

## Primary actor

- Workspace Member

## Trigger

- Write a small script step.

## Main flow

1. Actor starts the — Configure a Script step flow from the relevant Axis screen or API.
2. System checks workspace access, validates the request, and applies the documented acceptance criteria.
3. Actor sees the resulting data, confirmation, or actionable error for the flow.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Each step has a type that determines what it does when executed. Users configure each step type through the canvas side panel.

## Acceptance Criteria

*Happy path*
- [ ] Script editor (Monaco) has syntax highlighting and basic type hints for the `context` and `output` objects.
- [ ] The `context` object provides read access to all current execution context variables.
- [ ] Writing to `output` (e.g., `output.full_name = context.first_name + " " + context.last_name`) merges those values into the execution context after the step completes.
- [ ] A "Run test" button executes the script with a sample context (user-editable JSON) and shows the resulting `output` in the panel.

*Validation & errors*
- [ ] Timeout: required, 1–60 seconds.
- [ ] Script exceeding the timeout is forcibly terminated; the step is marked Failed with the message "Script execution timed out."
- [ ] Script that throws an unhandled exception marks the step as Failed and stores the exception message and stack trace in the step's error details.
- [ ] Script that attempts forbidden operations (network calls, file access, `process.*`, `require()`/`import`) throws a sandbox violation error immediately.

*Edge cases*
- [ ] `context` is a read-only proxy; attempting to write to `context` directly (not `output`) has no effect and does not throw an error.
- [ ] A script that runs within the timeout but produces no `output` writes is valid; context is unchanged.

*Out of scope*
- Importing external npm packages.
- Python or other language scripts — JavaScript only.

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
> **Gaps vs spec:** JS sandbox execution, timeout enforcement, and "Run test" button pending workflow-engine + Frontend.
>
> **Gaps vs spec:**
> - N/A
>
> **Deferred follow-ups:**
> - N/A
>
> **Decisions:**
> - N/A

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

