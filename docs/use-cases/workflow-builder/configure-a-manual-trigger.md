# Use case — Configure a Manual trigger

> **Navigation**: [← Workflow Builder](./README.md)

## Purpose

Configure a Manual trigger so that authorized users can start the workflow on demand.

## Primary actor

- Organization Member

## Trigger

- Configure a manual trigger.

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

A workflow must have at least one trigger before it can be published. Triggers define how and when a workflow execution starts.

## Acceptance Criteria

*Happy path*
- [ ] Adding a Manual trigger opens a config panel for defining optional named input variables (name + type + required flag).
- [ ] When triggering via UI, a dialog prompts for the defined input variables before starting.
- [ ] API: `POST /workflows/{id}/executions` with `{ "input": { "var_name": value } }` starts the execution.

*Validation & errors*
- [ ] Triggering via UI without filling required input variables shows inline errors before proceeding.
- [ ] API call with missing required inputs returns HTTP 422 with structured field errors.
- [ ] Users without `workflow:trigger:manual` permission do not see the Run button and get HTTP 403 from the API.

*Edge cases*
- [ ] A workflow with a Manual trigger and no input variables shows a simple "Run" confirmation dialog, not an input form.
- [ ] Triggering the same workflow many times in quick succession creates independent executions (no deduplication for manual triggers).

*Out of scope*
- Triggering with pre-filled input from a page button (Page Builder) — not in MVP for this domain; covered in page-builder.

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
> **Gaps vs spec:** input variable prompt dialog and `POST /workflows/{id}/executions` endpoint pending API + workflow-engine.
>
> **Decisions:**
> - trigger config (input variable definitions) stored as JSONB in `triggers` column
> - domain guards against duplicate trigger type per workflow (AddTrigger returns Conflict on second call for same type). `TriggerConfig` is a value object (no `id`, owned by `WorkflowDefinition`).

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| workflow-editor | [source](./wireframes/workflow-editor.excalidraw) | [preview](./wireframes/workflow-editor.svg) |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
