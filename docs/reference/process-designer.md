# Process designer

The process designer is the Design tab of a `process` resource in Studio, where an author builds a
process as a graph of its steps and transitions. It sits next to the Form and JSON tabs, a process opens
on it, and the three tabs edit one draft. It reuses the property panel, the undo and the drag and drop
of the [page designer](studio-editors-by-kind.md#page-designer).

## Canvas

The canvas is the process graph. The designer stores no position, and every edit lays the graph out again, so
the process file holds no layout. The canvas and the step panel read the same unsaved draft, so an edit in the
panel, the Form tab or the JSON view redraws the graph at once.

- **One graph.** The designer draws and edits through `ProcessGraphCanvas`, with the layout, the step cards, the
  transition labels, the controls, the zoom limit and a diagnostic's `danger` border of the
  [process graph](process-inspector-widget.md#process-graph). The selected step is the chosen card. Find a step
  selects the step it finds. A step shows no run duration and no state icon, which belong to an instance.
- **Selecting.** Clicking a node selects its step, and the step's panel shows beside the graph. Clicking
  an edge, or pressing Enter on it, selects that edge and the step it leaves, because that step's panel
  holds the picker of the transition. The selected step's node has `aria-current`, and the selected
  edge is drawn in the primary colour.
- **Not in the address.** The selected step and edge are not written to the URL, because a path in the
  URL opens the Form tab. A path that is `/steps/{i}` or inside it opens the Form tab and also selects
  step `{i}`, so the Design tab shows it on return.
- **Nested steps in the address.** _(planned for M38)_ A path under `/steps/{i}/branches/{j}/steps/{k}` or
  `/steps/{i}/steps/{k}` opens the Form tab and selects that nested step, as `/steps/{i}` selects a top-level step.
- **Names.** Each card is named by the step's name and type, as its visible type is an icon.
- **No invented parts.** A transition to a step that does not exist draws no edge, and the graph never
  invents a node.

## Download

An author saves the process's graph as an image, for the documents an audit or a quality system asks for.

- **Where.** Download is an icon button at the end of the graph's own controls, under Full screen, and it has no key.
  It opens a menu of SVG and PNG. Only the process designer's graph has it: an instance's Diagram and a module
  contract's lifecycle draw the same graph without it. It needs only the right to open the process in Studio.
- **What it saves.** Each choice saves the whole graph as one image at its full size, whatever part is in view and
  however far it is zoomed. The image is laid out from left to right, with every transition routed as the designer draws
  it, and it holds every step and transition with their labels.
- **Surface.** The image is always drawn in the light theme on `surface`, so it prints the same in dark mode.
- **What it leaves out.** It has no controls, no Find a step, no overview, no grid, no selection, no diagnostic mark and
  no customization layer's mark, because it documents the process and not the editing session.
- **File.** The file is named after the process in kebab case, such as `purchase-approval.svg` or
  `purchase-approval.png`. A module process is named by the text after the last `/` of its name, and a process with no
  name is `process`. The PNG is drawn from the SVG at twice its size, or at less where a browser's canvas would not
  hold it.
- **Failure.** When the image cannot be made, a message at the graph's top right says so and nothing is saved.
- **Loading.** The image library loads only when a download starts.

## Frames

_(planned for M38)_

A block is drawn as a frame around the steps it holds, so the author sees what runs inside it. The frame's
header shows the block's name and type, and a repeat also shows its kind, such as For each item. The rules of
the blocks are in [process blocks](process-blocks.md).

- **Parallel.** A `parallel` frame holds one lane for each branch, side by side. Each lane's head shows the
  branch's name and its `when`, when it has one.
- **Repeat.** A `repeat` frame holds one area, headed Steps to repeat, for the steps of its body.
- **Edges.** An edge runs from a lane's head, or the area's head, to the `start` step of that list. No edge is
  drawn while `start` is empty.
- **Nesting.** Frames nest. A block inside a lane or an area is drawn as a smaller frame inside it.
- **Layout.** The graph's layout places the steps of each lane or area inside its frame, and no position is stored, as
  [the canvas](#canvas) says.
- **Leaving a block.** A block's handle sits on the frame's right edge and sets its `next`, as the other steps'
  handles do. No transition leaves a block, as [structure](process-blocks.md#structure) says.

## Loading and redraw

The Design tab is the lazy chunk `assets/StudioProcessDesign-*.js`, which loads, with React Flow, when the tab first opens. The build fails when that chunk is missing or is part of
the main bundle, as the [bundle budget](../development.md#scripts) describes. On a 40-step process the canvas
shows a panel edit within 2 s, and
[`studio-process-redraw.spec.ts`](../../tests/e2e/specs/studio-process-redraw.spec.ts) fails at 2 s or
more.

- **Layout chunk**. `elkjs` loads in a chunk of its own, only on a page that draws a process, see
  [design tokens and previews](frontend.md#design-tokens-and-previews).

## Palette

The palette sits at the top of the panel beside the canvas and lists every step type and built-in
operation that `process.schema.json` defines: `decision`, `task`, the operations `updateRecord`,
`callConnector`, `raiseEvent`, `createRecord` and `notify`, one Package operation entry, `subProcess`,
`timer`, `waitEvent` and `end`. A Vitest test reads the schema and fails when a step type or a built-in
operation is missing from the palette.

- **A new step.** A new step goes at the end of `steps`, with a unique name such as `updateRecord1`: the
  operation's name, or the type's, with the first free number, ignoring letter case. It has its required
  keys empty and no transitions, becomes the selected step, and its diagnostics mark it until the author
  fills it in. A new `createRecord` step starts with an empty `entity` and an empty `set`, a new `notify`
  step with an empty `to`, an empty `channels` and a `message` with an empty subject and body key, and a
  new `waitEvent` step with an empty `event` and `next`.
- **Package operation.** The entry adds an operation step with an empty `operation`, which the author
  types as `<package>/<operation>` in the panel, as the operation picker does.
- **Dropping on the canvas** adds an unconnected step.
- **Dropping on an edge** inserts the step into that transition. The drop target is the middle of the
  transition's route, a small area that takes no clicks, so clicking an edge still selects it. The transition names the new
  step, and the new step's `next` names the old target. A type with no `next`, such as `decision`, gets
  the old target as its `otherwise`. A `task` gets one outcome with an empty name and label whose `next`
  is the old target, and the diagnostics mark the empty name and label until the author fills them in.
  An `end` step cannot be dropped on an edge, and nothing changes.
- **By keyboard.** A palette button pressed with Enter or Space adds its step by the same rule: into the
  selected edge, else after the selected step's `next`, else unconnected. A `decision`, `task` or `end`
  step has no `next`, so a step added while one is selected is unconnected. Enter on an edge selects it,
  so the keyboard reaches every transition.
- **Blocks and variables.** _(planned for M38)_ The palette also lists `parallel`, `repeat` and `setVariables`.
  A new `parallel` step starts with one branch, `branch1`, which has an empty `start` and `steps`. A new `repeat`
  step starts with an empty `forEach`, `start`, `steps` and `next`. A new `setVariables` step starts with an
  empty `set` and `next`.
- **Into a frame.** _(planned for M38)_ Dropping on a lane, or on a repeat's area, adds an unconnected step to
  that list. The step becomes the list's `start` when the list is empty. Dropping on an edge inside a frame inserts
  the step into that list's transition.
- **By keyboard, into a frame.** _(planned for M38)_ Enter on a lane or an area selects that list, and a palette
  button pressed then adds its step there, in place of the rule for a selected edge or step.
- **Names across lists.** _(planned for M38)_ A new step goes at the end of its own list. Its first free name is
  free across every list of the process.
- **One Undo.** Each add is one change of `steps`, so one Undo removes it.

## Transitions

Every step except an `end` step has a handle on its card's right edge, and dragging from it and dropping
anywhere on another step sets a transition. A step with one transition, such as an operation, sets its
`next` at once. A step with several opens a small menu at the drop point: a decision lists its branches,
`otherwise` and New branch, and a task lists its outcomes and New outcome. The handle has no keyboard
way, and the panel's pickers set the same transitions by keyboard.

- **Same step.** Dropping on the step the drag started from does nothing. The panel's pickers can still
  set a transition to the same step.
- **Closing the menu.** Escape, or a click outside the menu, closes it and changes nothing.
- **New branch and New outcome.** New branch adds a branch with the first free name `branch1`, `branch2`
  and so on, an empty `when` and the target. New outcome adds an outcome with the first free name
  `outcome1`, `outcome2` and so on, an empty label key and the target. The diagnostics mark both until
  the author fills them in.
- **Inside its block.** _(planned for M38)_ A drop on a step of another list changes nothing, and the live region
  says "A transition cannot leave its block."
- **A wait for several events.** _(planned for M38)_ A `waitEvent` with `events` opens the menu with its events, and
  a choice sets that event's `next`.
- **One Undo.** Each drag, or each menu choice, is one edit and so one Undo.

## Remove

Removing a step removes it from `steps`, and removing an edge clears that transition: its target becomes
empty, and its branch or outcome stays. A transition left naming a missing step is `AXC0070`, and the
graph draws no edge for it. The designer never invents a step or a transition, and one Undo reverses each
removal.

- **A step** is removed by Delete in its card's "…" menu, or by Delete or Backspace in the canvas.
  Removing the selected step clears the selection.
- **An edge.** With an edge selected, the panel shows a Remove line button above the step's panel,
  and Delete or Backspace in the canvas clears that transition instead of removing the step.
- **A block.** _(planned for M38)_ Removing a block removes the steps in its branches or body too, in one edit, so
  one Undo brings them all back.
- **The selection, not the focus.** Delete acts on the selection, not on the focused element, and a key
  pressed outside the canvas, such as in the panel's text fields, removes nothing.

## Step panel

Selecting a node opens the step's card of the Form tab beside the canvas, with a "…" menu that holds Move up,
Move down and Delete. A read-only tab shows no menu. Each move is one change of `steps`, so one Undo reverses it,
and an edit in the panel redraws the graph. With no step selected, the panel says "Select a step on the canvas."
The panel is drawn in [`SidePanel`](shell.md#layout), titled Step, with no close button, and it gives way while the
Problems or Ask AI panel is open and comes back when that panel closes.

The type picker in the panel and the Add step choice list every type in
`$defs.step.properties.type.enum` of the process schema. Changing a step's type removes the keys that the
schema's conditional for the new type forbids and adds its required keys, empty, while keys that the
schema allows stay. The conditionals that also test the operation are left to the operation panel. A
type with no panel shows its keys as "Edit in JSON" markers. Every step panel except a task's has an optional Label picker, which writes `label`. A task's panel has the
required one, and each decision branch has an optional Branch label picker.

- **Within its list.** _(planned for M38)_ Move up and Move down move a step within its own list: `steps`, a lane's
  `steps` or a repeat's body. A step never moves to another list, and every picker of a panel lists only the steps
  of its own list.
- **A block's type.** _(planned for M38)_ Changing a block's type removes its branches or body, and so the steps in
  them, by the rule above.

The step types and operations below have a panel of their own, beside the operation panels that
[Studio editors by kind](studio-editors-by-kind.md#process-editor) describes for the Form tab.

### createRecord

The operation picker offers `createRecord`. Its panel has an entity picker and one `set` row for each
field of the picked entity, as the `updateRecord` panel has for the subject entity.

### Notify

The operation picker offers `notify`. Its recipients are rows, with Add recipient on the recipients' title row and Remove recipient in each row's "…" menu, and a read-only tab shows neither. Each row stacks
a Recipient type choice of User, Role or Email, with a help line under each option, then the one field of that
type: a user expression, a role picker or an email expression. Each field has its label and help, one field to
a line, and the required fields are marked. Its `inApp` and `email` channels are two checkboxes, and
its subject and body go through the label editor, so the author writes the texts in every locale.
`values` are rows of a typed placeholder name and an expression. An Open a page checkbox adds `open` with
a page picker and the page's parameters as rows, keyed by the parameters of the picked page.

- **Kind** _(planned for M35)_. The panel has a Kind choice with Information, Warning and Needs action. Information removes `kind`, so the file keeps no default key. Switching a step from `notify` to another operation removes `kind` too.

Switching a step from `notify` to another operation removes `to`, `channels`, `message` and `open`, and
adds no empty key. The editor takes the pointer of the object that holds these keys and never shows
`next`, so the escalations of a task use it too.

### Connector input

The `input` of a `callConnector` step is rows. Each row stacks a Name, a Kind choice of Value, Object or List
with a help line under each option, and an Expression for a value. Each field has its label and help, one
field to a line, and the required fields are marked. The names route lists no input types, so the panel
reads a row's kind from the draft: text is a Value, an object with `from` a List, and any other object an
Object. Add row and each row's Remove sit as [the operation's rows](studio-editors-by-kind.md#process-editor) have
them, and a row's Remove removes what is nested in it.

- **Object.** An `object` shows nested rows, each with the same Name, Kind and Expression, over the subject.
- **List.** A `list` shows a From picker of the subject's child collections, then nested field rows with
  an expression over a row of that collection. The expression editor offers that child entity's fields
  and `id`, not the subject's.
- **Nesting.** Nested rows nest further. The switch writes `{ fields }`, `{ from, fields }` or an empty
  expression and keeps the nested `fields`, and the compiler reports what is missing.
- **Package operations.** A package operation's `input` stays scalar rows, because package fields stay
  scalar.

### Task deadline and escalations

A task's panel has a Deadline section and an Escalations list, after Due in. Add deadline writes `deadline` with an empty `after` and `next`, and the author types the duration and
picks the next step from the step names. Remove deadline removes the key.

Add escalation appends an escalation with the first free name `escalation1`, `escalation2` and so on, and
an empty `after`, and Remove escalation removes it. Each escalation has a Reassign switch, which adds
`reassign` with the task's assignee editor, and a Notify switch, which adds `notify` with the
[notify](#notify) editor. Turning a switch off removes its key, and removing the last escalation removes
`escalations`. A new escalation has neither key yet, so the compiler marks it until one switch is on.

### Several assignees

_(planned for M38)_

The assignee of a task has a kind choice of User, Role or Several people. A task for several people is one of the
[steps](processes.md#steps) of a process, with its `users` and its `completion`.

- **Several people.** The choice writes `users`, with a From picker of the subject's child collections and of its
  `references` fields, and a User expression over each item, as the List row of a [connector input](#connector-input)
  does.
- **Completion.** The choice also writes `completion` as `{ "rule": "all", "otherwise": "" }`. A Rule choice offers
  All, Any, Majority and A number, and A number shows a Count box. An Otherwise outcome picker lists the task's
  outcomes.
- **Switching back.** Choosing User or Role removes `users` and `completion` in one edit, so one Undo brings them back.

### Timer

- **The choice.** _(planned for M35)_ A timer's panel has a For a time or Until a time choice, a duration box for
  `wait`, an expression editor for `until`, and a Next step picker. Switching removes the other key and puts the
  chosen key, empty, in its place, and one Undo brings the old key back. A timer with neither key or both opens in
  For a time.

### Sub-process

A sub-process's panel has a process picker, then the child's inputs, then a Next step picker. A child
process with a subject entity shows a `subject` expression editor, checked against the parent
process. Clearing it removes the key, because `subject` may be left out when the child's entity is the
parent's. A child without a subject shows `MappingRows`, with one row per child parameter, each mapped to
an expression.

Picking a child of the other kind removes the key that no longer applies in the same edit, so one Undo
restores it. Picking a child without a subject writes every child parameter, keeping a value that the
draft already holds for that name. A child that is not in the names list changes only `process`.

### Wait for event

A wait for event panel has an event picker, an optional `when` expression editor, an optional timeout
and a Next step picker. The event picker lists only the events whose entity is the process's subject entity. The `when` editor
suggests the payload fields of the picked event, with rules and functions, and not the subject record's
fields, and clearing it removes the key. Add timeout adds a `timeout` with an After duration and a Next
step on timeout picker, both empty until filled. Remove timeout takes the whole `timeout` out, and one
Undo brings it back.

- **Several events.** _(planned for M38)_ Add event moves `event`, `when` and `next` into the first entry of `events`,
  and adds a second, empty entry, in one edit. Each entry has the same event picker, `when` and Next step picker as a
  single wait. Removing an entry leaves the rest, and when one entry is left the step turns back into `event`, `when`
  and `next`. One Undo reverses each switch. The canvas draws one edge for each entry, named by its event.

### Parallel

_(planned for M38)_

A `parallel` panel lists the branches, then a completion and a Next step. Each rule of
[parallel step](process-blocks.md#parallel-step) has a choice here.

- **Branches.** Each branch is a row with a Name, a When expression and a Start picker of the branch's steps. Add
  branch appends one with the first free name, `branch1`, `branch2` and so on, and an empty `start` and `steps`.
  Remove branch removes the branch and its steps, and one Undo brings them back.
- **Completion.** A Completion choice offers All, Any, A number and A condition. A number shows a Count box, and A
  condition shows an expression editor for `condition`. All removes `completion`, which is the default rule.
- **Next.** A Next step picker lists the steps of the same list as the block, and sets its `next`. It is the
  keyboard way to set a block's transition, as the Transitions section says.

### Repeat

_(planned for M38)_

A `repeat` panel has a Kind choice, with one field under it for that kind. The kinds are those of
[repeat step](process-blocks.md#repeat-step).

- **Kind.** For each item takes a picker of the subject's child collections, its `references` fields and the loaded
  data sources, and a data source is written as `{ "dataSource" }`. A number of times, While and Until take an
  expression. Switching kind removes the keys that the new kind refuses, in one edit, so one Undo restores them.
- **Order.** In order, or At once with a Passes at a time box for `atOnce`. At once is offered for For each item and
  A number of times only.
- **Limits.** Largest number of passes, which is required for While and Until, and Longest time, a duration that is
  offered for While and Until only.
- **Waits and stops.** Wait between passes, a duration, and Stop when, an expression checked after each pass.
- **Pass name.** A Pass name box for `as`, which defaults to `pass`.
- **Gather.** A list variable picker and a Value expression, which write `gather`.
- **Start and Next.** Start picks a step of the body, and Next picks a step of the block's own list.
- **Skipping.** An `end` step inside the body shows a Skip item checkbox, which writes `skip`.

### Set variables

_(planned for M38)_

A `setVariables` panel has one row for each process variable, under Field values, as the updateRecord panel has one
row for each field. A row shows the variable's name and type, and an expression for its value, which must fit that type. A Next
step picker follows the rows.

### Error paths

_(planned for M38)_

Every step but `end` has an Error paths list, which writes `onError`, see [error paths](processes.md#error-paths).

- **Add error path.** Appends `{ "error": "", "next": "" }`, which the diagnostics mark until both are filled in.
- **Each row.** A Kind choice of Call fails, Record write fails, Expression fails, Limit reached and Any failure, and
  a Next step picker of the same list.
- **Remove.** Remove error path removes its row, and removing the last row removes `onError`.

## Undo

The page designer's undo and redo cover every edit of a process on all three tabs, with 100 steps,
cleared by a reload, see [undo](studio-editors-by-kind.md#page-designer). They do not apply to a module process.

## Keyboard and screen readers

Every add, move and remove has a keyboard way, and a hidden live region says what changed.

- **Tab order.** Edges and then nodes are reached with Tab, because the edges come first in the page,
  and nodes in step order. Enter selects a node's step and opens its panel, and Enter or Space on an edge
  selects that edge. Delete or Backspace removes the selection.
- **Edge names.** Each edge has a name made of its two steps and its branch or outcome.
- **What the live region says.** "Removed step remind.", "Removed the line financeCheck to markApproved, otherwise.",
  "Added step remind after resolve.", or "Added step remind." for an unconnected step, "An end step cannot go on a
  line.", and "submit now goes to done.". A choice from the transition menu also names its branch or outcome. A move
  names the step now before the moved step, such as "Moved step financeCheck after submit.", and a step moved to the
  first place in `steps` says "Moved step financeCheck to the start.". That place is the top of the list, not the
  process's `start` step.
- **Parts of a name join with a comma**. A node's name and type, an edge's steps and
  its branch or outcome, and a transition menu's choice join as peers, as in "financeCheck to
  markApproved, otherwise" and "managerApproval, approve now goes to done", see
  [text](actions-and-controls.md#text).
- **Frames.** _(planned for M38)_ With frames, Tab goes depth first, in file order: a block, then each lane and its
  steps in turn, then the next step. A frame's name is like "reviews, parallel". A lane's name is like "legal, branch
  of reviews", and a repeat's area is like "checkLines, steps to repeat".
- **Live region for frames.** _(planned for M38)_ The live region says, for example, "Step legalReview added in branch
  legal of reviews", "Step checkLine added to the steps to repeat of checkLines", "A transition cannot leave its
  block." and "Removed step reviews and its 4 steps".

## Diagnostics

A step of the open process is marked when a diagnostic of the open resource names its path, `/steps/{i}`
or a path inside it. A marked node has an error-coloured border, its text label, and
`data-marked`. The marks come from the Problems panel's last loaded list, so they change on a load, a
save or a request, not on every edit.

- **The text label.** A marked node's text label reads "{step}, has problems", with the step's name.

- **Nested steps.** _(planned for M38)_ A diagnostic at a path inside a nested step marks that nested node, and not
  the frame around it. A diagnostic at a block's own path marks the frame, and not the steps inside it.

## Module processes

A module process, such as `checklists/ChecklistReview`, opens in this designer with the application's
[customization layer](customization-layers.md#in-studio) applied, through the module editor that module
entities use. It shows only the Design view, with no Form tab, JSON tab, Undo, Delete or Used by.

- **Marks.** A step that the layer added or changed is marked Added or Changed on its graph node, in the
  node's accessible name and on its panel, which also has Reset to module. A change inside a branch,
  outcome or escalation marks the step that holds it as Changed. A step that the layer removed is a muted
  row under the graph, marked Removed, with Reset to module. Reset to module is off while the draft has
  unsaved edits.
- **Save.** Save writes the layer file, and then a Customized tag and Reset all show in the header.
