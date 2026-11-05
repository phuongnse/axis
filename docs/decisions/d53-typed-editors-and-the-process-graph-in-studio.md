# D53. Typed editors and the process graph in Studio

Status: **Agreed**

## Context

Studio's generic form is built from a kind's JSON Schema, and it cannot show maps or `oneOf` parts.
So pages, process steps, connectors, test scenarios, seeds, text resources and mappings still need
JSON. An editor that drops a value it cannot show, or writes a shape the compiler refuses, costs the
author's trust. A process is also hard to follow as a list of steps alone.

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
- **React Flow draws the graph and dagre lays it out,** in a chunk of their own that loads only when
  a process shows its graph. React Flow brings keyboard support and custom nodes.

What each editor shows is in [Studio](../reference/studio.md) and the
[process designer](../reference/process-designer.md).

## Consequences

- Scenarios, mappings, seeds and texts can be written without JSON.
- The SPA gains two libraries, outside the main bundle.

## Alternatives

- **ELK for the layout.** It lays out better than dagre but weighs over 400 KB, while dagre handles
  directed step graphs of the size Axis processes have.
