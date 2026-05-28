# Use case — View workflows list

> **Navigation**: [← Workflow Builder](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

See all workflows so that I can find and manage them.

## Primary actor

- Organization Member with `workflow:definition:read`

## Trigger

- User initiates: see all workflows

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Users can create, view, edit, publish, archive, delete, and duplicate workflow definitions. A workflow definition is the blueprint the execution engine follows when triggered.

## Acceptance Criteria

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

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| workflows | [source](./workflows.excalidraw) | [preview](./workflows.svg) |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
