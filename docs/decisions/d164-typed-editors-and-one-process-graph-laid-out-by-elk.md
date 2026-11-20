# D164. Typed editors and one process graph laid out by ELK

Status: **Agreed**

## Context

Studio's generic form is built from a kind's JSON Schema, and it cannot show maps or `oneOf` parts.
So pages, process steps, connectors, test scenarios, seeds, text resources and mappings still need
JSON. An editor that drops a value it cannot show, or writes a shape the compiler refuses, costs the
author's trust. A process is also hard to follow as a list of steps alone, and read from left to right
it needs a layout that routes every transition round the steps it passes.

## Decision

- **A typed editor replaces the generic form for its kind.** The JSON view stays, as a second view
  of the same draft.
- **A typed editor saves as the form does.** It edits the same draft and saves the same JSON Patch,
  with the same version rules, through the reference pickers, label texts and expression editor of
  [D52](d52-reference-pickers-label-texts-and-the-expression-editor.md).
- **A typed editor never drops a value it does not show.** Unknown keys are saved as they are, so an
  older editor never breaks a newer format.
- **A process is drawn as a graph of its steps and transitions.** It is drawn from the unsaved
  draft, so an edit redraws it at once. A layout library places every node, so the author never
  places one and the file holds no positions. The graph never invents a node, works with the
  keyboard, and marks the steps that a diagnostic names. The
  [process designer](d74-a-visual-process-designer.md) edits on this graph.
- **React Flow draws the graph and ELK lays it out from left to right,** with every transition routed
  round the steps. Both load in chunks of their own, only on a page that draws
  a process. One graph serves the process designer and an instance's page, with no attribution drawn.

What each editor shows is in [Studio editors by kind](../reference/studio-editors-by-kind.md), the
[process designer](../reference/process-designer.md) and the
[process graph](../reference/process-inspector-widget.md#process-graph).

## Consequences

- Scenarios, mappings, seeds and texts can be written without JSON.
- The SPA gains two libraries, outside the main bundle. `elkjs` is about 1.4 MB, so it stays out of
  the first load.
- A fix to the graph reaches both the designer and an instance's page.

## Alternatives

- **dagre.** It places nodes, but it cannot route a line round a step, so a transition can cross one.
- **ELK's wrapping of a long row into several rows.** It broke transitions on a long process, so the
  graph keeps one row.
