# Use case — View org-wide execution history

> **Navigation**: [← Workflow Engine](./README.md)

## Purpose

see all executions across all workflows so that I have a global overview of automation activity.

## Primary actor

- Organization Admin

## Trigger

- User initiates: see all executions across all workflows

## Main flow

1. _(Happy path — align with acceptance criteria below.)_

## Alternate / error flows

- See *Validation & errors* and *Edge cases* under Acceptance Criteria.

## Context

Every workflow execution and each of its steps is recorded in full detail. Users can browse execution history, filter by status, and inspect the complete context and output of any past execution.

---

## Acceptance Criteria

**Purpose:** _(to be detailed during migration)_
**Primary actor:** _(to be detailed during migration)_
**Trigger:** _(to be detailed during migration)_

#### Main flow
1. _(to be detailed during migration)_

#### Alternate / error flows
- _(to be detailed during migration)_



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


## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| executions | [source](./wireframes/executions.excalidraw) | [preview](./wireframes/executions.svg) |

[← Back to Workflow Engine](./README.md)

---

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
