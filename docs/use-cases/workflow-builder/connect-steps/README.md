# Use case — Connect steps with transitions

> **Navigation**: [← Workflow Builder](../README.md) · [Use cases index](../README.md#use-cases)

## Purpose

Draw connections between steps so that the workflow knows the execution order.

## Primary actor

- Team account Member

## Trigger

- User initiates: draw connections between steps

## Main flow

1. Member drags from an output handle on one step to an input handle on another step.
2. System validates direction, cycle rules, and step-specific handle rules, then creates the directed transition.
3. Member sees the arrow on the canvas and can delete or adjust the connection.

## Alternate / error flows

- Validation failures and edge cases in Acceptance Criteria.

## Context

A node-based drag-and-drop canvas (powered by React Flow) where users design their workflow visually. Steps are nodes; transitions are directed edges.

## Acceptance Criteria

*Happy path*
- [ ] Hovering over a step node reveals output and input handles (small circles on the edges of the node).
- [ ] Dragging from an output handle to an input handle of another step creates a directed transition arrow.
- [ ] Connections can be deleted by clicking on the arrow and pressing Delete, or via a right-click context menu.

*Validation & errors*
- [ ] Dragging a connection from an output handle and dropping it on empty canvas (not on another node) cancels the connection.
- [ ] Creating a connection that would form a cycle (loop) is blocked; the connection snaps back and a toast shows: "Cycles are not allowed in workflows."
- [ ] A Condition step's output handles are labeled; connecting from an unlabeled handle is blocked.

*Edge cases*
- [ ] A step can have multiple outgoing transitions (branching) and multiple incoming transitions (merge).
- [ ] The Start node has no input handle. The End node has no output handle. Attempting to connect these in the wrong direction is blocked.

*Out of scope*
- Animated transitions showing flow direction — static arrows only.

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
> - canvas edge drawing and cycle-block toast pending Frontend
> - condition step label enforcement on connection pending Frontend.
>
> **Deferred follow-ups:** Animated transitions showing flow direction — static arrows only.
>
> **Decisions:** Transitions remain static arrows for the current builder scope; animated direction is decorative and deferred until the core canvas UX is usable.

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| N/A | N/A | N/A |
