# F04 — Execution History & Audit Log

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| executions | [source](../wireframes/executions.excalidraw) | [preview](../wireframes/executions.svg) |

[← Back to E06](../README.md)

---

## Description

Every workflow execution and each of its steps is recorded in full detail. Users can browse execution history, filter by status, and inspect the complete context and output of any past execution.

---

## User Stories

### US-097 — View execution history for a workflow

**As an** Organization Member with `execution:read`, **I want to** see the execution history for a specific workflow **so that** I can monitor its performance and identify patterns.

**Acceptance Criteria:**

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

---

### US-098 — View execution detail and step timeline

**As an** Organization Member with `execution:read`, **I want to** see the full detail of a specific execution **so that** I can understand exactly what happened at each step.

**Acceptance Criteria:**

*Happy path*
- [ ] Execution detail page shows: execution ID, status, total duration, input payload, trigger info, and created at.
- [ ] Step timeline shows all steps in execution order with: name, type icon, status, start time, end time, duration.
- [ ] Expanding a step shows: input (context snapshot before the step ran), output (data the step wrote to context), and error details (if failed).
- [ ] For Parallel Groups: the group is shown as a collapsible container with all parallel steps inside.

*Validation & errors*
- [ ] If a step's context snapshot is larger than 1 MB (e.g., large API response stored in context), it is shown truncated with a "Download full context" link.

*Edge cases*
- [ ] A step in `SKIPPED` status (branching condition not taken) shows with a neutral icon and "Skipped — branch not taken" as the reason.
- [ ] A step in `WAITING` status (Form step pending) shows "Waiting for: {assignee}" with a timestamp of when it was assigned.
- [ ] Context snapshots are immutable after being recorded — they reflect the exact state at that point in time, regardless of subsequent context changes.

*Out of scope*
- Replaying or simulating an execution from any point with a different context — not in MVP.

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
> **Gaps vs spec:** Step timeline UI, context snapshot display, and parallel group rendering pending Frontend.
>
> **Done:** `GET /api/executions/{id}` returns execution + steps.

---

### US-099 — View org-wide execution history

**As an** Organization Admin, **I want to** see all executions across all workflows **so that** I have a global overview of automation activity.

**Acceptance Criteria:**

*Happy path*
- [ ] A dedicated "Executions" page (main navigation) shows executions across all workflows.
- [ ] Additional filter vs per-workflow history: workflow name (searchable dropdown).
- [ ] Same columns and pagination as per-workflow history.
- [ ] "Export as CSV" button downloads a CSV with all visible records (respecting current filters).

*Validation & errors*
- [ ] CSV export of more than 10,000 rows is processed asynchronously; a download link is sent via in-app notification when ready.

*Edge cases*
- [ ] Admins see all org executions. Editors and Viewers see only executions they triggered or were assigned to (form tasks). The API enforces this scope.

*Out of scope*
- Platform-wide execution monitoring across all tenants (Platform Admin view) — not in MVP.

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
> **Gaps vs spec:** CSV export, role-scoped visibility enforcement, and async export notification pending API + Frontend.
>
> **Done:** `GET /api/executions` org-wide list (paginated).

---

## Data Retention

| Data | Retention |
|---|---|
| Execution records | 90 days (default); configurable per plan |
| Step output data | 90 days |
| Error details | 90 days |
| Audit log (who triggered, who submitted forms) | 1 year |

Executions older than the retention period are soft-deleted, then hard-deleted by a nightly background job. Users attempting to access an expired execution detail see: "This execution record has been deleted per the data retention policy."
