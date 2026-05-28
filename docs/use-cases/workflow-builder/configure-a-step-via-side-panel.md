# Use case — Configure a step via side panel

> **Navigation**: [← Workflow Builder](./README.md)

## Purpose

Click a step to open its configuration panel so that I can set it up without leaving the canvas.

## Primary actor

- Organization Member

## Trigger

- User initiates: click a step to open its configuration panel

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
- [ ] Clicking any step node opens a slide-over configuration panel on the right side of the canvas.
- [ ] The panel shows the step's type icon, an editable name field at the top, type-specific config fields below, and an optional notes/description field at the bottom.
- [ ] Configuration changes are auto-saved to the server after a 1-second debounce.
- [ ] Closing the panel (clicking the X or clicking elsewhere on the canvas) does not lose unsaved changes — they are saved before the panel closes.

*Validation & errors*
- [ ] Required config fields (e.g., form selection on a Form step) show an inline error indicator on the step node when missing, visible without opening the panel.
- [ ] Steps with validation errors block workflow publishing (see [publish workflow](./README.md)).

*Edge cases*
- [ ] Switching between two steps while a panel is open closes the first and opens the second without losing changes.
- [ ] The canvas remains fully interactive (pan, zoom, add steps) while a configuration panel is open.

*Out of scope*
- Full-screen step config modal — the panel-based UI is the only config surface in MVP.

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
> - slide-over panel, inline error indicators, and auto-save pending Frontend
> - step config stored as JSONB dict in `steps` column.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| workflow-editor | [source](./wireframes/workflow-editor.excalidraw) | [preview](./wireframes/workflow-editor.svg) |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
