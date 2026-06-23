# Use case — Duplicate a workflow

> **Navigation**: [← Workflow Builder](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Duplicate an existing workflow so that I can use it as a starting point for a similar process.

## Primary actor

- Workspace Member with `workflow:definition:write`

## Trigger

- User initiates: duplicate an existing workflow

## Main flow

1. Actor starts the — Duplicate a workflow flow from the relevant Axis screen or API.
2. System checks workspace access, validates the request, and applies the documented acceptance criteria.
3. Actor sees the resulting data, confirmation, or actionable error for the flow.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Users can create, view, edit, publish, archive, delete, and duplicate workflow definitions. A workflow definition is the blueprint the execution engine follows when triggered.

## Acceptance Criteria

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
- Cross-workspace workflow duplication (copy to another workspace) — handled by Import/Export in import-export.

## Design Sources

| Screen | Source | Preview |
|--------|--------|---------|
| N/A | N/A | N/A |

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
>
> **Deferred follow-ups:**
> - N/A
