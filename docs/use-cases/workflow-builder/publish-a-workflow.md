# Use case — Publish a workflow

> **Navigation**: [← Workflow Builder](./README.md)

## Purpose

publish a workflow so that it can be triggered and executed.

## Primary actor

- Organization Member with `workflow:definition:write`

## Trigger

- User initiates: publish a workflow

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
- [ ] Clicking "Publish" validates the workflow, moves it to `Active` status, and activates all configured triggers (e.g., registers cron job, generates webhook URL).
- [ ] A published workflow shows an "Active" badge and a "Run" button (if it has a Manual trigger).

*Validation & errors*
- [ ] Publishing fails if: the workflow has no steps beyond Start/End, has no trigger configured, or has any "broken" step (referencing a deleted form or model). A validation panel lists all issues.
- [ ] Publishing fails if any step has no outgoing transition (except End nodes).

*Edge cases*
- [ ] An already-published (Active) workflow can be edited — edits create a new Draft version. The current active version continues running until the new version is published.
- [ ] Publishing a new version archives the previous version's definition snapshot for execution history traceability.

*Out of scope*
- Approval workflow for publishing (e.g., requiring a second admin to approve) — not in MVP.

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
> - cron job registration and webhook URL generation pending WorkflowEngine integration (workflow-engine)
> - broken-step validation pending data-modeling/form-builder integration
> - draft versioning on re-edit pending API design.

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
