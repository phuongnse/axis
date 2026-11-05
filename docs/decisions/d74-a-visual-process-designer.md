# D74. A visual process designer

Status: **Agreed**

## Context

An author who cannot write JSON must be able to build a process, and a graph is how people think of
one. [D53](d53-typed-editors-and-the-process-graph-in-studio.md) already draws a process as a graph
with a computed layout, and the page and form designers of
[D65](d65-a-visual-page-designer-on-a-canvas-drawn-by-the-sites-widgets.md) and
[D66](d66-a-visual-form-designer-for-form-resources.md) set how a Studio designer works.

## Decision

- **The process graph becomes the designer.** A process opens on a Design tab, next to its Form and
  JSON tabs, and the three tabs edit one draft on the graph of D53.
- **The layout is always computed.** The designer stores no position, so the process file holds no
  layout.
- **Steps and transitions are drawn.** The author drags a step type from a palette onto the canvas, or
  onto an edge to insert it there, and drags from one step to another to set a transition, choosing
  which one when the step has several.
- **The designer never invents a step or a transition.** A removal leaves the transitions that named
  the step dangling, and Validate reports them.
- **The rest works as in the other designers:** the existing step panels, the undo of D65, a keyboard
  way for every action, and the graph's diagnostic marks.

The screen is in the [process designer](../reference/process-designer.md).

## Consequences

- Process files stay free of positions, so Studio's output matches the committed samples, and the
  author has no layout to keep tidy but cannot arrange the graph by hand.
- Studio keeps one way of working across its designers.

## Alternatives

- **Store node positions.** Studio's files would differ from the committed samples and give the author
  a layout to tidy.
