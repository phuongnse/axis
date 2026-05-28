# Use case — Add a step to the canvas

> **Navigation**: [← Workflow Builder](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Add a step to the workflow canvas so that I can build my process visually.

## Primary actor

- Organization Member with `workflow:definition:write`

## Trigger

- User initiates: add a step to the workflow canvas

## Main flow

1. Actor satisfies the trigger.
2. System performs the happy-path steps in Acceptance Criteria.
3. Actor receives the expected outcome.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

A node-based drag-and-drop canvas (powered by React Flow) where users design their workflow visually. Steps are nodes; transitions are directed edges.

## Acceptance Criteria

*Happy path*
- [ ] A sidebar lists all step types with icons and one-line descriptions.
- [ ] Dragging from the sidebar and dropping onto the canvas places the step node at the drop position.
- [ ] Dropping a step immediately opens its configuration panel.
- [ ] A step can also be added by clicking a "+" button on any existing transition arrow.

*Validation & errors*
- [ ] Dropping a step outside the canvas bounds snaps it to the nearest valid canvas position.
- [ ] If the canvas has unsaved changes when the browser tab is closed, the browser shows a "Leave site? Changes you made may not be saved" warning.

*Edge cases*
- [ ] Dropping two steps at nearly the same position offsets the second one automatically to prevent overlap.
- [ ] Workflow definitions are auto-saved to the server after every change with a 1-second debounce, so the user never needs to manually save canvas layout changes.

*Out of scope*
- Copy-paste of steps — not in MVP.

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
> **Gaps vs spec:** canvas drag-drop UI and 1-second debounce auto-save pending Frontend.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| workflow-editor | [source](./workflow-editor.excalidraw) | [preview](./workflow-editor.svg) |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
