# Use case — Undo and redo canvas actions

> **Navigation**: [← Workflow Builder](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Undo and redo changes on the canvas so that I can recover from mistakes easily.

## Primary actor

- Organization Member

## Trigger

- User initiates: undo and redo changes on the canvas

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
- [ ] Ctrl+Z / Cmd+Z undoes the last canvas action (add step, delete step, move step, add/delete connection).
- [ ] Ctrl+Shift+Z / Cmd+Shift+Z (or Ctrl+Y) redoes the last undone action.
- [ ] Undo/Redo buttons in the canvas toolbar mirror the keyboard shortcuts.

*Validation & errors*
- [ ] Undo is not available when at the beginning of history; the undo button is disabled.
- [ ] Redo is not available when at the end of history; the redo button is disabled.

*Edge cases*
- [ ] Undo history depth: at least 20 actions.
- [ ] Undo/redo applies to canvas layout actions only (node position, connections, adding/removing steps). Step configuration changes (saved via auto-save) are NOT undoable.
- [ ] Refreshing the page clears undo history (history is in-memory only).

*Out of scope*
- Server-side version history with named snapshots.

> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | API | N/A |
> | Frontend | ⏳ |
>
> Undo/redo is in-memory client state only; no server round-trip required.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
