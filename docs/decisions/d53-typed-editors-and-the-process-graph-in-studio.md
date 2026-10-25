# D53. Typed editors and the process graph in Studio

Status: **Agreed**

M11 gives Studio typed editors for pages, process steps and connectors, and
for the kinds that need JSON today. It also adds a read-only process graph. The
seed grid, the scenario editor, the connector editor, the page editor, the endpoint
mapping rows, the text table, the process graph, and
the process step list with its decision, end, task and operation panels are built.
See the
[Studio section](../reference/studio.md) for the detail. The owner
confirmed these points on issue #780 on 2026-10-20.

- **Typed editors:**
  - A typed editor replaces the generic form for its kind. The JSON view
    stays, as a second view of the same draft.
  - It edits the same draft and saves the same JSON Patch as the form, with
    the same `If-Match` version rules.
  - It never drops a value it does not show. Unknown keys are kept as they
    are.
  - It uses the reference pickers, label texts and expression editor of
    [D52](d52-reference-pickers-label-texts-and-the-expression-editor.md).
- **Page editor:**
  - A list of the page's widgets, each with its type and a summary.
  - A panel per widget type: `table`, `form`, `taskInbox` and
    `processInspector`. Each shows the properties the widget has today, and
    the form's `startProcess` actions.
  - M12 runs in parallel
    ([D46](d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md)).
    A widget or layout that M12 adds gets its panel in M12. Until then it
    shows in the JSON view.
- **Process step editor:**
  - The steps as a list in file order, with add, remove and reorder.
  - A panel per step type: `operation`, `decision`, `task` and `end`. An
    operation shows its `set`, `input`, `output` and `retry`. A decision shows
    its `branches` and `otherwise`. A task shows its assignee, form, outcomes
    and `dueIn`.
  - Mappings show as rows of target and expression.
  - Each transition, `next`, a branch, `otherwise` or an outcome, picks its
    target from the process's steps.
- **Connector editor:**
  - The base URL binding and the auth secret binding are set by name, in a name box. No
    value of a binding is shown or stored, as today.
  - The timeout and the idempotency header.
  - The operations as a list, each with its method, path, input and output
    fields, and lookup.
- **Other kinds:**
  - Test scenarios: the users with their roles, then the steps as a list,
    with a panel per step type.
  - Seed data: the records as a grid with a column per field of the seed's
    entity.
  - Text resources: a table of keys with a column per locale, with search.
    Adding a key adds it in every locale's `text` resource.
  - Endpoint mapping: rows of field and expression, as for process mappings.
    The rest of an endpoint keeps the generic form.
- **Process graph:**
  - A process shows a drawn graph of its steps and transitions above its
    editor. It is read-only. A layout library places every node, and the
    author never places one.
  - The graph is drawn from the editor's unsaved draft, so an edit in the
    step list redraws it at once. Diagnostic marks come from the diagnostics
    panel's last loaded list.
  - Selecting a node selects that step in the list. Selecting an edge opens
    its transition. Selecting a step in the list highlights its node.
  - A transition that names a step that does not exist draws no edge. The
    diagnostic marks the step that holds the transition. The graph never
    invents a node.
  - A step that a diagnostic names is marked on the graph.
  - The graph works with the keyboard, and each node has a text label with
    the step's name and type.
  - It uses React Flow (`@xyflow/react`, MIT, about 50 KB min+gzip with its
    dependencies) to draw, and dagre (`@dagrejs/dagre`, MIT, about 30 KB
    min+gzip) to lay out. M14's process designer builds on both.
  - Both packages came with the graph, in their own lazy chunk.
- **Designer:** [D74](d74-a-visual-process-designer.md) makes the graph the
  process designer in M14. It still stores no position.
- **Effect on earlier decisions:** D39 says there is no graph drawing library.
  [D39](d39-reference-index-and-where-a-resource-is-used.md) links here
  where M11 changes that.

_Why:_ an editor that drops a value it cannot show, or writes a shape the
compiler refuses, costs author trust. So a typed editor keeps unknown keys and
saves the same patch as the form. A newer format is never broken by an older
editor. The generic form cannot show maps or `oneOf` parts, so scenarios,
mappings, seeds and texts need typed editors to work without JSON. The graph is
read-only until M14, because editing on a graph needs the process designer and
its design. Authors edit in the list until then. React Flow gives keyboard
support and custom nodes, which M14 needs. Dagre lays out directed step graphs
of the size Axis processes have. ELK lays out better but weighs over 400 KB.
The cost is two new libraries in the SPA bundle. The page editor changes again
in M12, which D46 accepts.
