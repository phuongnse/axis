# Use case — Create a model

> **Navigation**: [← Data Modeling](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Create a new model so that I can start defining the data structure for my business objects.

## Primary actor

- Organization Member with `data_modeling:model:write`

## Trigger

- User initiates: create a new model

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Users can create custom data models within their organization. A model defines the structure of a type of business object. All model metadata is stored in the tenant schema; actual records use a JSONB-backed storage strategy.

## Acceptance Criteria

*Happy path*
- [ ] Creation dialog collects: name (required), description (optional), icon (optional, from a predefined icon set), and color (optional).
- [ ] The model is created immediately with auto-generated system fields: `id` (UUID), `created_at` (DateTime), `updated_at` (DateTime).
- [ ] After creation, the model opens in the field editor.

*Validation & errors*
- [ ] Name: required, 2–100 characters. Allows letters, numbers, spaces, and hyphens. Blocks special characters like `/ \ < > " ;`.
- [ ] Name must be unique within the org (case-insensitive). Duplicate name shows: "A model named '{name}' already exists."
- [ ] If the plan's model limit is reached, creation returns HTTP 402 with an upgrade prompt instead of a form error.

*Edge cases*
- [ ] Creating a model with a name that matches a soft-deleted model is allowed (they are different models).
- [ ] Model creation is atomic: if any part of the creation fails (e.g., inserting system fields), the entire model is rolled back and nothing is left in a partial state.

*Out of scope*
- Importing a model from another org or from a JSON file directly — covered in [workflow-builder import-export Import/Export](../../workflow-builder/).

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
> - model plan-limit check (HTTP 402) pending billing layer (platform-foundation subscription plans)
> - name format validation enforced in Application handler.
>
> **Decisions:** system fields (id, created_at, updated_at) injected by domain factory; atomicity guaranteed by UnitOfWork.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| data-model | [source](./data-model.excalidraw) | [preview](./data-model.svg) |
