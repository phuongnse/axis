# Use case — View execution history for a workflow

> **Navigation**: [← Workflow Engine](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

See the execution history for a specific workflow so that I can monitor its performance and identify patterns.

## Primary actor

- Organization Member with `execution:read`

## Trigger

- User initiates: see the execution history for a specific workflow

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Every workflow execution and each of its steps is recorded in full detail. Users can browse execution history, filter by status, and inspect the complete context and output of any past execution.

## Acceptance Criteria

*Happy path*
- [ ] Execution history tab on the workflow detail page shows a paginated table: execution ID, status badge, trigger type icon, started at, completed at, duration, triggered by (user or "System").
- [ ] Default sort: started at, descending (newest first).
- [ ] Filters: status (All / Completed / Failed / Cancelled / Running), date range, trigger type.
- [ ] Clicking a row navigates to the execution detail page.

*Validation & errors*
- [ ] If the history table fails to load, an error state with a Retry button is shown (not an empty table).
- [ ] Date range filters where "from" is after "to" show an inline validation error.

*Edge cases*
- [ ] A workflow with thousands of executions: pagination is server-side (25 per page); the total count is shown (e.g., "1,247 executions").
- [ ] Executions that are currently `RUNNING` appear at the top of the list regardless of sort order, with a live elapsed time counter.

*Out of scope*
- Execution analytics dashboard (charts, trends) — not in MVP.

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
> **Gaps vs spec:** `GET /api/executions` and per-workflow paged list ✅. Filter UI, date/trigger query params, and running-first sort pending API + Frontend.
>
> **Decisions:** `GetExecutionsByWorkflowHandler` and `GetAllExecutionsHandler` use `IExecutionRepository.GetPagedByWorkflowAsync`/`GetPagedAsync` — server-side pagination with `pageSize` clamped to 100. Status filter forwarded to repository. Date range filter and trigger type filter deferred to API layer query parameters.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| executions | [source](./executions.excalidraw) | [preview](./executions.svg) |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
