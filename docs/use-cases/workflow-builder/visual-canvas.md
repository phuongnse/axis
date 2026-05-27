# Use Case Group — Visual Workflow Canvas

## Wireframes

| Screen | Excalidraw | Preview |
|--------|------------|---------|
| workflow-editor | [source](../../epics/E04-workflow-builder/wireframes/workflow-editor.excalidraw) | [preview](../../epics/E04-workflow-builder/wireframes/workflow-editor.svg) |

[← Back to E04-workflow-builder](../../epics/E04-workflow-builder/README.md)

---

## Description

A node-based drag-and-drop canvas (powered by React Flow) where users design their workflow visually. Steps are nodes; transitions are directed edges.

---

## Use Cases

### Use case — Add a step to the canvas

**Purpose:** _(to be detailed during migration)_
**Primary actor:** _(to be detailed during migration)_
**Trigger:** _(to be detailed during migration)_

#### Main flow
1. _(to be detailed during migration)_

#### Alternate / error flows
- _(to be detailed during migration)_


**As an** Organization Member with `workflow:definition:write`, **I want to** add a step to the workflow canvas **so that** I can build my process visually.

**Acceptance Criteria:**

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

---

### Use case — Connect steps with transitions

**Purpose:** _(to be detailed during migration)_
**Primary actor:** _(to be detailed during migration)_
**Trigger:** _(to be detailed during migration)_

#### Main flow
1. _(to be detailed during migration)_

#### Alternate / error flows
- _(to be detailed during migration)_


**As an** Organization Member, **I want to** draw connections between steps **so that** the workflow knows the execution order.

**Acceptance Criteria:**

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
- Animated transitions showing flow direction — not in MVP (static arrows only).

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

---

### Use case — Configure a step via side panel

**Purpose:** _(to be detailed during migration)_
**Primary actor:** _(to be detailed during migration)_
**Trigger:** _(to be detailed during migration)_

#### Main flow
1. _(to be detailed during migration)_

#### Alternate / error flows
- _(to be detailed during migration)_


**As an** Organization Member, **I want to** click a step to open its configuration panel **so that** I can set it up without leaving the canvas.

**Acceptance Criteria:**

*Happy path*
- [ ] Clicking any step node opens a slide-over configuration panel on the right side of the canvas.
- [ ] The panel shows the step's type icon, an editable name field at the top, type-specific config fields below, and an optional notes/description field at the bottom.
- [ ] Configuration changes are auto-saved to the server after a 1-second debounce.
- [ ] Closing the panel (clicking the X or clicking elsewhere on the canvas) does not lose unsaved changes — they are saved before the panel closes.

*Validation & errors*
- [ ] Required config fields (e.g., form selection on a Form step) show an inline error indicator on the step node when missing, visible without opening the panel.
- [ ] Steps with validation errors block workflow publishing (see US-049).

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

---

### Use case — Navigate and zoom the canvas

**Purpose:** _(to be detailed during migration)_
**Primary actor:** _(to be detailed during migration)_
**Trigger:** _(to be detailed during migration)_

#### Main flow
1. _(to be detailed during migration)_

#### Alternate / error flows
- _(to be detailed during migration)_


**As an** Organization Member, **I want to** pan and zoom the workflow canvas **so that** I can work comfortably with large workflows.

**Acceptance Criteria:**

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
- Touch/gesture controls for tablet use — not in MVP.

> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | API | N/A |
> | Frontend | ⏳ |
>
> No backend domain or infrastructure work; canvas navigation is a pure client-side concern.

---

### Use case — Undo and redo canvas actions

**Purpose:** _(to be detailed during migration)_
**Primary actor:** _(to be detailed during migration)_
**Trigger:** _(to be detailed during migration)_

#### Main flow
1. _(to be detailed during migration)_

#### Alternate / error flows
- _(to be detailed during migration)_


**As an** Organization Member, **I want to** undo and redo changes on the canvas **so that** I can recover from mistakes easily.

**Acceptance Criteria:**

*Happy path*
- [ ] Ctrl+Z / Cmd+Z undoes the last canvas action (add step, delete step, move step, add/delete connection).
- [ ] Ctrl+Shift+Z / Cmd+Shift+Z (or Ctrl+Y) redoes the last undone action.
- [ ] Undo/Redo buttons in the canvas toolbar mirror the keyboard shortcuts.

*Validation & errors*
- [ ] Undo is not available when at the beginning of history; the undo button is disabled.
- [ ] Redo is not available when at the end of history; the redo button is disabled.

*Edge cases*
- [ ] Undo history depth: at least 20 actions.
- [ ] Undo/redo applies to canvas layout actions only (node position, connections, adding/removing steps). Step configuration changes (saved via auto-save) are NOT undoable in MVP.
- [ ] Refreshing the page clears undo history (history is in-memory only).

*Out of scope*
- Server-side version history with named snapshots — not in MVP.

> **Implementation status**
>
> | Layer | Status |
> |-------|--------|
> | API | N/A |
> | Frontend | ⏳ |
>
> Undo/redo is in-memory client state only; no server round-trip required.


## Diagrams

| Diagram | Source | Preview |
|---------|--------|---------|
| N/A | N/A | N/A |
