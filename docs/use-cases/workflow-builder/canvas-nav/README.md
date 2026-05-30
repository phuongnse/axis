# Use case — Navigate and zoom the canvas

> **Navigation**: [← Workflow Builder](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Pan and zoom the workflow canvas so that I can work comfortably with large workflows.

## Primary actor

- Organization Member

## Trigger

- User initiates: pan and zoom the workflow canvas

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
- [ ] Scroll wheel zooms in/out centered on the cursor position.
- [ ] Click-and-drag on empty canvas area pans the view.
- [ ] "Fit to view" button (toolbar) zooms and pans to show all steps on screen.
- [ ] Mini-map in the bottom-right corner shows the full workflow; clicking on the mini-map jumps to that area.

*Validation & errors*
- [ ] Zoom range: 25% to 200%. Attempting to zoom beyond these limits stops at the boundary.

*Edge cases*
- [ ] A workflow with only a Start and End node: "Fit to view" centers those two nodes with reasonable padding.
- [ ] Mini-map can be collapsed to avoid obscuring the canvas for users who don't need it.

*Out of scope*
- Touch/gesture controls for tablet use.

> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | API | N/A |
> | Frontend | ⏳ |
>
> No backend domain or infrastructure work; canvas navigation is a pure client-side concern.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |

