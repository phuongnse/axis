# Use case — Reorder fields

> **Navigation**: [← Data Modeling](./README.md)

## Purpose

Reorder fields in a model so that the display order matches our team's mental model.

## Primary actor

- Organization Member

## Trigger

- User initiates: reorder fields in a model

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Each field in a model has a type that determines what data it stores, how it's validated, and how it's rendered in forms and lists. The type system is the foundation of the data modeling module.

## Acceptance Criteria

*Happy path*
- [ ] Fields can be dragged up and down via a drag handle in the field editor.
- [ ] The new order is saved on drop (immediate API call, no separate Save button needed).
- [ ] The field order is reflected in: the auto-generated record list columns, the default form field order, and API responses.

*Validation & errors*
- [ ] If the reorder API call fails, the field snaps back to its original position and shows an error toast.

*Edge cases*
- [ ] System fields (`id`, `created_at`, `updated_at`) are always pinned to the end and cannot be reordered.
- [ ] Two users reordering fields simultaneously: last write wins; no conflict detection required for ordering.

*Out of scope*
- Hiding fields from the default list view per user — not in MVP.

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
> **Gaps vs spec:** drag-drop reorder UX and immediate-save endpoint pending Frontend layer; `displayOrder` persisted in JSONB field list.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| data-models | [source](./wireframes/data-models.excalidraw) | [preview](./wireframes/data-models.svg) |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
