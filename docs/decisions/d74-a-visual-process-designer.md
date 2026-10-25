# D74. A visual process designer

Status: **Agreed**

M14 adds a visual process designer to Studio. An author builds a process by
drag and drop on its graph. The owner agreed this on 2026-10-25. It is
_(planned for M14)_. See [process designer](../reference/process-designer.md).
It builds on the process graph of
[D53](d53-typed-editors-and-the-process-graph-in-studio.md) and the designers
of [D65](d65-a-visual-page-designer-on-a-canvas-drawn-by-the-sites-widgets.md)
and [D66](d66-a-visual-form-designer-for-form-resources.md). It ends D53's
"read-only until M14" for the graph.

- **Design tab:** a process opens on a Design tab, next to its Form and JSON
  tabs. The three tabs edit one draft.
- **Canvas:** the graph of D53, drawn by React Flow and laid out by dagre. The
  designer stores no position. The layout is always computed, so the author
  never places a node, and the process file holds no layout to compare.
- **Palette:** it lists the step types, the new ones of
  [D69](d69-timer-wait-for-event-and-sub-process-steps-loops-and-create-record.md)
  included. The author drags a type onto the canvas to add a step, or onto an
  edge to insert one in that transition. A keyboard user picks a type and
  inserts it after the selected step.
- **Transitions:** the author drags from a step's handle to another step to
  set its `next`, an outcome, a branch or `otherwise`. When a step has several
  transitions, a small menu asks which one.
- **Remove:** removing a step or an edge leaves the transitions that named it
  dangling. They show as `AXC0070`. The designer never invents a step or a
  transition.
- **Property panel:** selecting a node opens the step's existing panel, with
  panels for the new step types and keys. An edit in the panel redraws the
  graph.
- **Undo:** the undo and redo of D65 cover every edit, with the same limits.
- **Keyboard:** every action has a keyboard way, through the panel's pickers
  and the palette, and a screen reader hears what changed.
- **Diagnostics:** marks work as on the graph of D53.
- **Carried over from M13:** pages and forms open on their Design tabs, a form
  section moves to another step by a drop on the step switch, and the form
  designer removes a section or a step. D65 and D66 already record these.

_Why:_ an author who cannot write JSON must be able to build a process, and a
graph is how people think of one. Computed layout keeps files free of
positions, which would make Studio's output differ from the committed samples
and give the author nothing to keep tidy. Reusing the graph, the panels and the
undo of the earlier designers keeps one way of working in Studio.
