# Use case — Reorder fields via drag-and-drop

> **Navigation**: [← Form Builder](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Drag form fields to reorder them so that the form flows naturally.

## Primary actor

- Team account Member

## Trigger

- User initiates: drag form fields to reorder them

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

Form fields define what data the form collects. Each field has a type, label, help text, and validation rules. Fields can be reordered and grouped into sections.

## Acceptance Criteria

*Happy path*
- [ ] Each field in the editor has a drag handle (six-dot icon) on its left side.
- [ ] Dragging a field up or down reorders it; the live preview updates in real time during the drag.
- [ ] The new order is auto-saved immediately on drop.

*Validation & errors*
- [ ] If the reorder API call fails, the field snaps back to its original position and an error toast is shown.

*Edge cases*
- [ ] Section dividers can be reordered along with fields, maintaining their grouping relationship.
- [ ] Reordering a field from one section into another section is supported.

*Out of scope*
- Multi-column form layouts — single-column only.

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
> **Gaps vs spec:** drag-handle UI and real-time preview reorder pending Frontend.
>
> **Decisions:** `ReorderFormFieldsHandler` catches `ArgumentException` from domain (IDs don't match all fields) and returns `ErrorCodes.BusinessRule`.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

