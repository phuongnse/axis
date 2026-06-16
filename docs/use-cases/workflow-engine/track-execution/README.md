# Use case — Track execution status in real time

> **Navigation**: [← Workflow Engine](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

See the live status of a running execution so that I know where it is in the process.

## Primary actor

- Tenant Member

## Trigger

- User initiates: see the live status of a running execution

## Main flow

1. Actor starts the — Track execution status in real time flow from the relevant Axis screen or API.
2. System checks tenant access, validates the request, and applies the documented acceptance criteria.
3. Actor sees the resulting data, confirmation, or actionable error for the flow.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

The engine manages the full lifecycle of a workflow execution — from creation through completion, failure, or cancellation. Each execution is a runtime instance of a workflow definition.

## Acceptance Criteria

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
- Real-time execution graph overlay on the workflow canvas — timeline list only.

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
> **Gaps vs spec:** SignalR push updates and execution detail page pending Frontend.
>
> **Done:** `GET /api/executions/{id}` returns step timeline via `GetExecutionHandler`.
>
> **Decisions:** `GetExecutionHandler` delegates to `IExecutionRepository.GetWithStepsAsync`, which loads execution + steps in two queries (no EF navigation property — `ExecutionStep` is a separate aggregate).
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

