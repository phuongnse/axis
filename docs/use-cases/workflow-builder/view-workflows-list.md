# Use case — View workflows list

> **Navigation**: [← Workflow Builder](./README.md)

## Purpose

see all workflows so that I can find and manage them.

## Primary actor

- Organization Member with `workflow:definition:read`

## Trigger

- User initiates: see all workflows

## Main flow

1. _(Happy path — align with acceptance criteria below.)_

## Alternate / error flows

- See *Validation & errors* and *Edge cases* under Acceptance Criteria.

## Context

Users can create, view, edit, publish, archive, delete, and duplicate workflow definitions. A workflow definition is the blueprint the execution engine follows when triggered.

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
- [ ] List shows: name, status badge (Draft / Active / Archived), trigger type icon, step count, last modified date, and last execution date.
- [ ] Default sort: last modified descending.
- [ ] Tabs or filter for status: All, Active, Draft, Archived.
- [ ] Search by name (real-time, client-side).

*Validation & errors*
- [ ] Empty state for each status tab has a contextual message (e.g., "No active workflows yet. Publish a workflow to activate it.").

*Edge cases*
- [ ] A workflow with multiple trigger types shows the first trigger's icon and a "+N" badge.

*Out of scope*
- Workflow folders / tags — not in MVP.

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
> **Gaps vs spec:**
> - status-tab filter and last-execution-date column backend polish — see gaps below
> - execution date requires WorkflowEngine integration.

---

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| workflows | [source](./wireframes/workflows.excalidraw) | [preview](./wireframes/workflows.svg) |

[← Back to Workflow Builder](./README.md)

---

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
