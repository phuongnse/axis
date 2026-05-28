# Use Case Group — Workflow Definition Management

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| workflows | [source](./wireframes/workflows.excalidraw) | [preview](./wireframes/workflows.svg) |

[← Back to Workflow Builder](./README.md)

---

## Description

Users can create, view, edit, publish, archive, delete, and duplicate workflow definitions. A workflow definition is the blueprint the execution engine follows when triggered.

---

## Use Cases

### Use case — Create a workflow

**Purpose:** _(to be detailed during migration)_
**Primary actor:** _(to be detailed during migration)_
**Trigger:** _(to be detailed during migration)_

#### Main flow
1. _(to be detailed during migration)_

#### Alternate / error flows
- _(to be detailed during migration)_


**As an** Organization Member with `workflow:definition:write`, **I want to** create a new workflow **so that** I can start designing an automated process.

**Acceptance Criteria:**

*Happy path*
- [ ] Creation dialog collects: name (required), description (optional).
- [ ] New workflow is created in `Draft` status and opens in the visual canvas editor.
- [ ] A new workflow starts with a Start node and an End node already placed on the canvas.

*Validation & errors*
- [ ] Name: required, 2–200 characters, unique within the org (case-insensitive). Duplicate shows: "A workflow named '{name}' already exists."
- [ ] If the plan's workflow limit is reached, creation is blocked with an HTTP 402 upgrade prompt.

*Edge cases*
- [ ] Creating a workflow and immediately navigating away without adding any steps: the empty workflow is saved in Draft status and can be returned to later.

*Out of scope*
- Workflow templates / starter library — not in MVP.

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
> **Gaps vs spec:** canvas/list UI only (backend).
>
> **Done:** HTTP 402 on create when workflow plan limit reached (`CreateWorkflowHandler` + platform-foundation subscription plans).
>
> **Decisions:**
> - new workflow initialised with Start + End nodes by domain factory
> - all data stored in single `workflow_definitions` table.

---

### Use case — View workflows list

**Purpose:** _(to be detailed during migration)_
**Primary actor:** _(to be detailed during migration)_
**Trigger:** _(to be detailed during migration)_

#### Main flow
1. _(to be detailed during migration)_

#### Alternate / error flows
- _(to be detailed during migration)_


**As an** Organization Member with `workflow:definition:read`, **I want to** see all workflows **so that** I can find and manage them.

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

### Use case — Publish a workflow

**Purpose:** _(to be detailed during migration)_
**Primary actor:** _(to be detailed during migration)_
**Trigger:** _(to be detailed during migration)_

#### Main flow
1. _(to be detailed during migration)_

#### Alternate / error flows
- _(to be detailed during migration)_


**As an** Organization Member with `workflow:definition:write`, **I want to** publish a workflow **so that** it can be triggered and executed.

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

### Use case — Archive a workflow

**Purpose:** _(to be detailed during migration)_
**Primary actor:** _(to be detailed during migration)_
**Trigger:** _(to be detailed during migration)_

#### Main flow
1. _(to be detailed during migration)_

#### Alternate / error flows
- _(to be detailed during migration)_


**As an** Organization Member with `workflow:definition:write`, **I want to** archive a workflow **so that** it is disabled but its history is preserved.

**Acceptance Criteria:**

*Happy path*
- [ ] Archiving moves the workflow to `Archived` status and deactivates all triggers (cron jobs unscheduled, webhook URL deactivated).
- [ ] Running executions at archive time are allowed to complete; no new executions can start.

*Validation & errors*
- [ ] Attempting to archive a `Draft` workflow returns HTTP 422: "Cannot archive a draft workflow."
- [ ] Attempting to trigger an archived workflow via API or webhook returns HTTP 422: "This workflow is archived and cannot be triggered."

*Edge cases*
- [ ] An archived workflow can be unarchived (restored to Active) by any admin.
- [ ] Execution history for an archived workflow is still fully accessible.

*Out of scope*
- Automatic archiving after N days of inactivity — not in MVP.

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
> - trigger deactivation on archive pending workflow-engine integration
> - HTTP 422 on archived-workflow trigger backend polish — see gaps below.

---

### Use case — Duplicate a workflow

**Purpose:** _(to be detailed during migration)_
**Primary actor:** _(to be detailed during migration)_
**Trigger:** _(to be detailed during migration)_

#### Main flow
1. _(to be detailed during migration)_

#### Alternate / error flows
- _(to be detailed during migration)_


**As an** Organization Member with `workflow:definition:write`, **I want to** duplicate an existing workflow **so that** I can use it as a starting point for a similar process.

**Acceptance Criteria:**

*Happy path*
- [ ] Duplicate creates a full copy of the workflow (steps, transitions, trigger config, step configs) in `Draft` status.
- [ ] The copy is named "Copy of {original name}" by default; the user can change the name in the creation dialog before confirming.
- [ ] The duplicate opens in the canvas editor immediately after creation.

*Validation & errors*
- [ ] If "Copy of {original name}" already exists, a suffix is appended: "Copy of {name} (2)", "Copy of {name} (3)", etc.
- [ ] If the workflow limit is reached, duplication is blocked with an HTTP 402 upgrade prompt.

*Edge cases*
- [ ] Duplicating a workflow does not copy its execution history.
- [ ] Webhook URLs are NOT copied; the duplicate generates a new unique webhook URL when published.

*Out of scope*
- Cross-org workflow duplication (copy to another org) — handled by Import/Export in import-export.

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
> **Gaps vs spec:** webhook URL generation for duplicate pending workflow-engine.
>
> **Done:** HTTP 402 on duplicate when at workflow limit (`DuplicateWorkflowHandler`).
>
> **Decisions:**
> - Duplicate() deep-copies all steps with new IDs and remaps transitions atomically in domain logic
> - handler resolves name collisions via "(2)", "(3)"… suffix loop up to 50, then Guid suffix.

---

### Use case — Delete a Draft workflow

**Purpose:** _(to be detailed during migration)_
**Primary actor:** _(to be detailed during migration)_
**Trigger:** _(to be detailed during migration)_

#### Main flow
1. _(to be detailed during migration)_

#### Alternate / error flows
- _(to be detailed during migration)_


**As an** Organization Member with `workflow:definition:write`, **I want to** delete a Draft workflow **so that** I can permanently remove workflows I no longer need without having to publish them first.

**Acceptance Criteria:**

*Happy path*
- [ ] Deleting a Draft workflow removes it from the list permanently (soft-deleted; not recoverable via the UI).
- [ ] Delete returns HTTP 204 No Content on success.

*Validation & errors*
- [ ] Only `Draft` workflows can be deleted. Attempting to delete an `Active` or `Archived` workflow returns HTTP 422: "Only draft workflows can be deleted."
- [ ] Attempting to delete a workflow that does not exist returns HTTP 404.

*Out of scope*
- Hard delete / permanent purge — not in MVP.
- Bulk delete — not in MVP.

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


## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
