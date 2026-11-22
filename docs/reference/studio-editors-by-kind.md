# Studio editors by kind

This page describes the editor that each kind opens in Studio: a designer, a typed editor or the generic
form. The generic form and the rest of Studio are in [Studio](studio.md).

## Editors by kind

Each kind opens in the editor that fits it: a designer, a typed editor or the [generic form](studio.md#editor).

| Kind               | Editor                                                                                                               |
| ------------------ | -------------------------------------------------------------------------------------------------------------------- |
| `entity`           | The [entity designer](entity-designer.md)                                                                            |
| `page`             | The [page editor](#page-editor) and the [page designer](#page-designer)                                              |
| `form`             | The generic form with the [form steps](studio-form-editors.md#form-steps), and the [form designer](form-designer.md) |
| `process`          | The [process editor](#process-editor) and the [process designer](process-designer.md)                                |
| `site`             | The generic form, with the [sign-in](#a-sites-sign-in) picker                                                        |
| `endpoint`         | The generic form, with [rows](studio-form-editors.md#endpoint-rows) for its `values` and its `event`'s `fields`      |
| `connector`        | The [connector editor](#connector-editor)                                                                            |
| `identityProvider` | The [identity provider editor](#identity-provider-editor)                                                            |
| `scenario`         | The [scenario editor](#scenario-editor)                                                                              |
| `seed`             | The [seed editor](studio-form-editors.md#seed-editor)                                                                |
| `text`             | The [text editor](text-editor.md)                                                                                    |

Every other kind, `application`, `rule`, `sequence`, `role`, `policy`, `dataSource`, `event`,
`trigger`, `webhook` and `migration`, opens in the generic form. Every editor shows all of its kind's
content but a migration's `changes` list, which stays in the JSON view because each change's fields
depend on its `type`.

## Page editor

The page editor's Form tab edits a page's title, parameters and widgets, with a panel for each widget,
and the JSON view stays as the second view of the same draft. The [page designer](#page-designer)
edits the same draft on a canvas.

- **The page.** The Form tab shows three [sections](studio-resource-form.md#sections): General, with the
  page's name, its [title](studio-resource-form.md#labels) and its [description](studio.md#editor); the
  [parameters](#page-parameters); and Widgets, with the [widget tree](#widget-tree). Each page field and
  each widget name picker, and each field of every widget panel and of their actions, has a label and help,
  named by its path in the page. The page editor draws no label of its own.
- **Widget panels.** Each widget type has a panel that shows the widget's properties: `table`, `form`,
  `taskInbox`, `processInspector`, `kpi`, `chart`, `detail`, `columns`, `tabs` and `section`. A widget
  of a type with no panel shows its type and points to the JSON view.
  - A `table` panel has entity and data source pickers, then a `rowActions` list of navigate or delete
    actions and a `toolbarActions` list of navigate actions. A delete action has a label and an
    optional `visibleWhen` expression.
  - A `form` panel has entity and form pickers and its `actions`, each a `startProcess` action with a
    process picker and a label, or a navigate action.
  - A `taskInbox` panel has no properties, and a `processInspector` panel has a process picker.
  - A `detail` panel has the entity and form pickers and the actions of a form, with a record picker
    between them, see [detail, chart and KPI widgets](detail-chart-kpi-widgets.md). The record picker
    lists the page's required parameters of type `reference`: those whose `target` is the widget's
    entity, ignoring letter case, or every such parameter when the widget names a form. Picking another
    entity keeps the record, a record that matches no listed parameter shows as unknown, and the
    compiler reports it after the save.
  - The [KPI and chart panels](#kpi-and-chart-panels) and the
    [container panels](#container-panels) follow.
- **Exclusive bindings.** The compiler accepts exactly one of a table's entity and data source, and one
  of a form's or a detail's entity and form, so picking one clears the other.
- **Type change.** Choosing another type removes the known properties that the new type does not take,
  because the compiler rejects them. A switch to `processInspector`, `detail`, `kpi` or `chart` adds
  the empty keys the schema requires, such as `process` or `record`, and a new chart starts with the
  `bar` kind and one empty measure, which the schema requires.
- **Process pickers.** An action's process picker lists every process and does not check the choice,
  so a wrong one shows as a diagnostic after the save.
- **Unknown keys.** A key the editor does not show, on the page or on a widget, is saved unchanged,
  also when the widget moves.
- **Parts in words**. A widget's row, a canvas frame's name and a page parameter's row join their parts with a
  comma, as in "table, PurchaseRequestList", see [text](actions-and-controls.md#text).

### KPI and chart panels

A `kpi` panel has a data source picker, a measure picker and a [label](studio-resource-form.md#labels).
Add comparison adds a comparison with its own measure picker and label, and Add navigate action adds a
[navigate action](#navigate-actions). Each of the two adds is a grey button, and each Remove comparison and
Remove navigate action is a red button. A `chart` panel has a data source picker, a kind picker over `bar`,
`line` and `pie`, a category picker, a title, and a list of measures, each with a picker and a label. Add
measure is a light text button on the Measures title row, and each measure's Remove measure is in its "…"
menu, which is off when one measure is left.
Every field and group of both panels shows its label and help. A chart's title and a section's title
share one text, Title, with one help.

- **Data sources.** A KPI's data source picker lists only data sources with a total row, and a chart's
  only those with exactly one group field, which are the only choices the compiler accepts.
- **Columns.** The column pickers read the chosen data source from the names. The category lists its
  group field and the measure pickers its measures. A chart's measure pickers list only measures of
  type `integer` or `decimal`, because the names give a `count` the type `integer` and a `sum`, `min`
  or `max` the type of its field. Picking another data source keeps the names already set, and a name
  that the new data source does not project shows as unknown, which the compiler reports after the
  save. While the names have not loaded, the column pickers are plain boxes.
- **Parameters.** Both panels have a Page parameters list, with a picker for each parameter of the data
  source that lists the page parameters of the same type. Clearing a picker removes its key, a key the
  list does not show stays as it is, and clearing the last one leaves an empty `parameters` object. A
  binding to a missing or wrong-type page parameter shows as unknown. The compiler checks the target,
  the enum values and the required flag after the save, see
  [widget binding](data-sources.md#widget-binding). The editor does not check that a `pie` has exactly
  one measure.

### Navigate actions

A navigate action has a page picker, a [label](studio-resource-form.md#labels), an optional `record`
expression and parameter expressions. The page picker, label, record and parameters each show their
label and help. The page picker lists every page, the current one included, because a page may open
itself with other parameters.

- **Adding one.** The Adds of an action list are one group of light text buttons on the list's title
  row. A table's row actions offer Add navigate action and Add delete action, its toolbar actions offer
  only Add navigate action, and a form or a detail also offers Add action for `startProcess`. Each
  action is a row with no box, and its Remove action is in the row's "…" menu. A new action is named
  `<action type><n>`, such as `navigate1`, with the first free `n` in its list. A KPI's `navigate` has no
  name, because the schema refuses one.
- **Record.** An empty Record box removes the `record` key.
- **Parameters.** When the names list the target page, the action shows an expression box for each
  parameter the page declares, with the parameter's type and whether it is required. An empty box
  leaves the parameter out. A value the page does not declare stays visible with a note and Remove,
  because the compiler refuses it. Picking another page removes the values that the new page does not
  declare, ignoring letter case. While the names have not loaded, or when the page is not in them, the
  action keeps rows where the author types the parameter's name, and a wrong name shows as a
  diagnostic after the save.

### Page parameters

The Parameters section sits between General and Widgets. Its Add parameter is a light text button on the
right of the heading, and a page with no parameters says so in one line. Each parameter is a row with no
box. It has a name, a type picker over `text`, `integer`, `decimal`, `boolean`, `date`, `date-time`, `enum`
and `reference`, and a Required switch. The name, type and Required switch sit one to a row, each as wide as
the field it fills. Remove parameter is an entry of the row's "…" menu at its top right, the same menu as a
table's [row actions](actions-and-controls.md#tables). Add parameter appends a `text` parameter
with an empty name. A `reference` parameter also has a target entity picker, and an `enum` parameter its
values. A type change removes the `target` or the `values` that the new type does not take, and switching Required off removes the `required` key.
The names answer lists each page's parameters, which is how a navigate action knows them. A
`parameters` that is not a list points to the JSON view, and a parameter that is not an object shows that
line with no menu.

### Widget tree

The editor shows the page's widgets as nested rows. A container shows its columns, tabs or section
body as nested lists, and a row opens its widget's panel at its own pointer, such as
`/widgets/0/columns/1/widgets/0`.

- **Adding.** Every list has an Add widget picker with all ten types. A new widget is named
  `<type><n>`, such as `table1`, with the first `n` free on the page at any depth, ignoring letter case.
  A new `columns` starts with the columns `column1` and `column2` of width 6, a new `tabs` with `tab1`,
  and a new `section` empty.
- **Moving.** Every row has a Move to picker and a "…" menu at its end, with Move up, Move down and
  Delete. The menu's actions replace only the list that holds the widget, Move up is off on the list's first
  row and Move down on its last, and Delete is destructive. The row's button that selects the widget is
  outlined in the primary colour while the widget is selected, with `aria-current`, and grey otherwise.
  Move to lists every other widget list as a path, such as
  `Page › columns 1 › Column 2`, leaves out the widget's own list and the lists inside it, and replaces
  the page's `widgets` in one change. A type change or a move keeps the widget's name.
- **Fixed containers.** A container's type is fixed. The compiler reports the depth limit and an empty
  list after the save.

### Container panels

Each container type has a panel under its fixed type. The type shows in a read-only box labelled Widget
type, and every field of the panel shows its label and help. A key the panels do not show, on a column, a
tab or a section, stays in the saved file. A container whose parts are malformed, such as `columns`
that is not a list, points to the JSON view.

- **Columns.** A `columns` panel shows a width box from 1 to 12 for each column, and the compiler
  reports widths that do not add up to 12 after the save. Each column is a row with no box. Add column
  is a light text button on the Columns title row, and it adds a column that takes what is left of 12,
  or 1 when nothing is left, and keeps the other widths. Remove column is in the column's "…" menu and
  drops a column with its widgets.
- **Tabs.** A `tabs` panel shows each tab's [label](studio-resource-form.md#labels) as a row with no
  box. Add tab is a light text button on the Tabs title row, and Remove tab is in the tab's "…" menu.
- **Names.** An added column or tab is named with the first free `column<n>` or `tab<n>` among its
  siblings, ignoring letter case.
- **Section.** A `section` panel shows the title as a tab's label shows, and a Collapsed switch.
  Switching Collapsed off removes the `collapsed` key, because a section starts expanded.
- **The last one stays.** Remove column and Remove tab are off when one is left, because the schema
  needs at least one.

## Page designer

The page designer is the page editor's Design tab, where an author builds a page by drag and drop on a
canvas that the site's own widgets draw. It sits next to the Form and JSON tabs, a page opens on it,
and the three tabs edit one draft. The form designer and the process designer reuse its property
panel, its undo and its drag and drop.

- **Canvas.** The canvas draws the unsaved draft with the site's own widget components, with labels
  from the application's texts, and sends no request to `/api/apps/`. Tables, charts and the other data
  widgets show placeholder rows and values made from the field types, and a page parameter gets a
  placeholder value made from its type. The canvas cancels every click and form submit, so an action
  runs nothing and a navigate action or a link leaves Studio where it is. A page the route cannot draw
  shows a short notice. The canvas is mounted only while the Design tab is active.
- **Redraw.** The canvas posts the draft to [the draft page metadata route](draft-page-metadata.md)
  300 ms after the last edit and redraws from its answer. A widget redraws only from a new answer, so an
  edit redraws it once, after the post, and a drag over the same target redraws none. The canvas also
  posts the draft after each save. Save writes the texts of new label keys, which the route compiles
  with, and the draft stays the same. From an edit or a save until it draws the answer, the canvas has
  `aria-busy`, which the tests wait on before a drag.
- **Placeholders and marks.** A widget that does not compile draws as a placeholder with its type and a
  line for each of its diagnostics, with the code and the message. A widget is marked when a diagnostic
  of the page, of any severity, names its `path` or a path inside it, so a container is marked too when
  a widget inside it has one, even in a closed section or an inactive tab. A marked widget has a border
  in the `danger` colour, its text label, and `data-marked`. The marks follow the
  route's latest answer, and so the unsaved draft.
- **Marked names.** A marked widget's text label reads "{name}, has problems", where the name is its frame's
  name: the widget's type and the name it binds, so a marked table reads "table, PurchaseRequestList, has problems".
- **Selection.** A click on the canvas or a pick in the widget tree selects a widget in both. A click
  selects the innermost widget under the pointer, and a click on the empty area of a container selects
  the container. The selected widget has `aria-current` and an outline in the primary colour. Every
  frame is a Tab stop, and Enter or Space selects it. A frame's name is its widget's type and the name
  it binds, such as `table` and `PurchaseRequestList`, and reads as the marks above say when it is marked. The
  selection is kept across the Design and Form tabs while the resource stays open, and is not in the
  URL's `path`, which opens the Form tab.
- **Property panel.** The side pane holds the whole widget tree and the panel of the selected widget,
  the same as the Form tab. An edit redraws the canvas through the draft post.
- **Actions on the canvas.** Every action of a table, row and toolbar, of a form, a detail and a KPI
  draws as a button, also one that the site would hide, such as an action whose `visibleWhen` fails on
  placeholder data or a form action on an unsaved record. A click, Enter or Space on the button selects
  the action, by its JSON Pointer, such as `/widgets/0/rowActions/1`, or `/widgets/0/navigate` for a
  KPI, and not its widget. The button has `aria-current` and an outline in the primary colour, the
  widget tree keeps its widget selected, and the panel shows the widget's panel with the action's row
  marked by `aria-current`, outlined in the primary colour and scrolled into view. Adding a navigate, `startProcess` or `delete` action
  in the panel selects the new action, and removing an action selects its widget. The button runs
  nothing: it does not navigate, start a process, open a delete confirmation or call `/api/apps/`.
- **Actions that fold.** A KPI's navigate action is a link-style button on the label line, and the
  tile is not a link. A table's first `navigate` row action draws as the link of its identifying
  column, which selects the action as a button does. At narrow width a table's row actions fold into a
  menu, and three or more other row actions fold into the "…" menu at any width, see
  [frontend](frontend.md). The canvas cannot open that menu, because it cancels the click, so the
  author selects such an action from the tree and the panel.
- **Palette.** The side pane lists the ten widget types above the widget tree. Enter, Space or a click
  inserts a new widget after the selected widget, at the end of the selected container's first list,
  or at the end of the page's list on an empty page. The author can also drag a type onto the canvas.
  A drop on the upper half of a widget inserts before it and one on its lower half after it, and a drop
  on a widget list, the page's own or that of a column, a tab or a section, or between two widgets,
  appends to that list. The innermost target wins. A container that does not compile yet, such as a
  new empty section, draws as a placeholder with a drop area for each list its draft has.
- **Page limits.** A type that the page cannot take, under the limits of a page's
  [widget tree](resource-files.md#resource-file-shape), is off, cannot be dragged, and shows its
  reason: a second table, a second form, task inbox or process inspector, or a container past 4
  levels. A drop that a limit forbids inserts nothing, and the live region gives the same reason.
- **Data panel.** Between the palette and the widget tree, the side pane lists every entity and every data
  source, module ones too. Each list has a title and a help line, and a list with no names says so in one
  line. A drop lands where a palette drop lands, and opens a dialog with one button for each widget that
  can show the name: a table, a form or a detail for an entity, and a table, a chart with one group field
  or a KPI with a total row for a data source. A widget that a page limit forbids is off with the palette's
  reason. Enter, Space or a click on a name opens the same dialog, and the widget goes after the selected
  widget. A detail takes as its record the first required `reference` parameter that targets the entity, or
  none, because the designer adds no page parameter. A chart takes the group field as its category, the
  `bar` kind and the first numeric measure, and a KPI the first measure. A measure has no label, so a chart
  and a KPI start with empty text keys and draw as placeholders until the author writes them.
- **Moves.** The author can drag any widget of the canvas, and the innermost widget under the pointer
  drags, so a press on a KPI in a column moves the KPI. It drops on the same targets as a palette type.
  Alt+ArrowDown and Alt+ArrowUp move the selected widget to the next or previous allowed place,
  skipping refused ones, and focus follows it. The Move to picker of the widget tree stays. A move the
  format refuses, a widget inside itself or a container whose deepest list would land past level 4, is
  flagged while it is dragged: the drag overlay and the live region give the reason, and a drop there
  changes nothing. A drop on the widget itself changes nothing either. A move keeps all the widget's
  keys.
- **What the live region says.** After an insert, a drop or a move the new or moved widget is selected, and a live
  region reads one sentence of the agreed texts, with the type, the list and the position filled in, such as
  "Inserted table in Page, position 2." or "Moved kpi to Page › section 2, position 1." A move that cannot go
  further says "The widget cannot move further." The drag and drop library's own spoken messages are off, so the
  live region is the only voice.
- **Width switch.** It draws the canvas at desktop width, which fills the canvas area, or at phone
  width, where the columns stack to full width and tables use their narrow layout, even in a wide
  browser window. The choice goes back to desktop when the author returns to the Design tab. The live
  site keeps its own rule, the window's width.
- **Undo.** Undo and Redo on the [save bar](studio-resource-form.md#save-bar), and Ctrl+Z and Ctrl+Shift+Z, or Cmd on macOS, cover
  every edit of the draft in the three tabs of a page, a form and a process. The history is kept in
  memory, up to 100 steps, and a reload, also one after a reconnect, a conflict reload or opening
  another resource clears it, while a save keeps it. A step ends when the author edits another property
  or leaves the box, and a pick in a dropdown, a button action and a move are always a step of their
  own. The icons are off when there is nothing to undo or redo, and while a save
  runs. The keys also work when nothing has focus, but not when the focus is outside the editor, and
  Studio's undo replaces the browser's own inside the editor's boxes.
- **Undo for every kind.** Every kind's draft has undo and redo, on the
  [save bar](studio-resource-form.md#save-bar).
- **Custom widgets** _(planned for M22)_. The palette offers custom widgets, as
  [custom widgets](custom-widgets.md#designer-palette) describes.
- **Code.** Drag and drop uses `@dnd-kit/core`, `@dnd-kit/sortable` and `@dnd-kit/utilities`, in the
  lazy chunk `assets/DragAndDrop-*.js` that the designers share; the form outline's moves use
  `@dnd-kit/sortable`. Each designer's canvas, palette and, for a page, data panel are a lazy chunk of
  their own, `StudioPageDesign`, `StudioFormDesign` and `StudioProcessDesign`, which load when the
  Design tab opens. The build fails when the drag and drop chunk, the page chunk or the process design
  chunk is missing or is part of the main bundle.
- **Format.** The designer writes only the order of the widget tree and the properties the format has.
  It writes no pixels, and widths stay in twelfths.

## Process editor

The process editor's Form tab edits a process as a list of step cards, and the JSON view stays as the
second view of the same draft. The [process designer](process-designer.md) edits the same draft as a
graph, with the card of the selected step beside it, and describes the panels of the timer,
sub-process and wait for event steps and of a task's deadline and escalations.

- **The process.** The Form tab has the General, Subject and Steps [sections](studio-resource-form.md#sections),
  and each field has a [field text](studio-resource-form.md#fields).
- **Lists.** The lists of a process's parameters, a decision's branches, a task's outcomes and escalations, and an
  operation's rows have Add on their title row as a light text button. Each row's actions, and each step card's, are
  entries of its "…" menu, the table's [row actions](actions-and-controls.md#tables). A task's deadline and a wait
  for event's timeout have Add or Remove on their group's title row, also as a light text button. A read-only tab,
  such as while the JSON has an error, shows no Add, no Remove and no menu.
- **Subject.** A choice between Entity, which shows the entity picker, and Parameters, which shows rows
  of a name and a scalar type, `text`, `integer`, `decimal`, `boolean`, `date` or `date-time`, as the
  [Lists](#process-editor) bullet says. Switching replaces the draft in one change, so one undo brings it back: the
  other key is removed, and the new key, empty, takes its place in the file. See
  [processes without a subject](process-flow.md#processes-without-a-subject).
- **Steps.** The steps show as cards in file order, each with the step's name and type. Each card has a "…"
  menu with Move up, off on the first step, Move down, off on the last, and Delete, as the
  [Lists](#process-editor) bullet says. Add step appends a step of the chosen type, with an empty name. A type change
  works as the [step panel](process-designer.md#step-panel) says.
- **Decision and end.** A decision shows its branches, each with a `when` expression and a target, and
  its `otherwise`. A branch is added and removed as the [Lists](#process-editor) bullet says, and a new one is
  named `branch1`, `branch2` and so on. An end step shows only its name and type.
- **Transitions.** The start step, `next`, a branch's `next`, `otherwise` and an outcome's `next` pick
  their target from the step names of the unsaved draft. A target that is not a step stays visible and
  is tagged unknown. Removing a step does not rewrite the transitions that name it, so the compiler
  reports them.
- **Task.** A task shows its label and its form as pickers over the application's text keys and forms.
  The assignee has a User and Role switch: User shows the expression editor and Role a role picker, and
  a switch removes the other key, so the step never holds both. `dueIn` is a text box, and clearing it
  removes the key. The outcomes are rows of name, label and target step, added and removed as the
  [Lists](#process-editor) bullet says. A key the card does not show stays as it is. A diagnostic path inside a
  task's label, assignee, form, `dueIn` or outcomes opens the Form tab at that field.
- **Operation.** An `operation` step shows an operation choice, `updateRecord`, `callConnector`,
  `raiseEvent`, `createRecord`, `notify` or Package operation, above its transition. Changing the
  operation removes only the keys the new one forbids and adds no empty key, so the compiler reports
  what is still missing, and every other key, unknown keys included, stays.
  - **Rows.** Each list of rows, `set`, `input` and its nested `fields`, `output`, an event's `fields`, a
    notification's `values` and a page's parameters, has Add row on its title row, or the add choice of the
    unmapped keys where the keys are known. Its rows' menus and a read-only tab follow the
    [Lists](#process-editor) bullet.
  - `updateRecord` shows `set` as rows of a field of the process's entity and an expression.
  - `callConnector` shows the connector and its operation as pickers, its
    [`input`](process-designer.md#connector-input), `output` as rows of a typed connector output name
    and an entity field, and `retry`.
  - `raiseEvent` shows the event as a picker and `fields` as rows of a payload field of that event and
    an expression.
  - `createRecord` has the panel that [createRecord](process-designer.md#createrecord) describes. Its
    entity picker lists every entity, child entities included, because the names do not say which are
    child entities, and the compiler reports one.
  - `notify` has the panel that [notify](process-designer.md#notify) describes.
  - A package operation is typed in a text box as `<package>/<operation>`, because no route lists the
    installed operations. It shows `input`, `output` and `retry` as `callConnector` does, with typed
    names, but its `input` stays scalar rows.
  - `retry` is attempts and backoff, and clearing both removes `retry`.
- **Without names.** While the application's names are not loaded, every picker is a name box. A key
  that the step's operation forbids is not shown as rows: the card says to edit it in the JSON view,
  and a diagnostic path inside it opens the JSON view.

## Connector editor

The connector editor's Form tab shows the connector's fields in the four
[shared sections](studio-resource-form.md#sections) General, Service, Authentication and Operations,
each field with its label and [help](frontend.md#platform-field-help). Add operation sits on the
Operations heading, and the JSON view stays as the second view of the same draft.

- **No values.** A binding is a name, and its help says what it holds and that the file keeps only its name. No
  value of a binding is shown or stored: the editor reads the resource and schema routes and the
  binding names, and writes only the resource, and the values stay in tenant settings.
- **Bindings from the tenant.** A binding is chosen from the tenant's names of the kind it needs, see
  [controls that know what they hold](studio-resource-form.md#controls-that-know-what-they-hold).
- **Auth.** Choosing a type removes the keys the new type forbids, such as `token` for `header`, and
  adds the keys it requires, with an empty name, and clearing the type removes `auth`. `oauth2` shows a
  Token URL, a URL binding, a Client ID and a Client secret, both secret bindings, and Scope and
  Audience boxes that remove their key when empty, see [OAuth2 auth](integration-resources.md#oauth2-auth).
- **Operations.** A card per operation shows its name, method, path and `lookup`, which is picked from
  the connector's other operations. Its input and output fields are rows of name, type and required,
  one field to a line, and the schema's required keys carry the shared required mark. Add operation sits
  on the Operations heading. Each input, output and nested field list has its Add on its title row, as a
  light text button. Each operation's Delete operation is in its "…" menu at its top right, and each row's
  Delete is in its "…" menu, which also removes the fields nested in the row. A read-only tab, such as while the JSON has
  an error, shows no Add and no menu, as the Lists bullet of the [process editor](#process-editor) says.
  A `GET` with a body row keeps it and shows a hint, and the compiler reports the error.
- **Placement and nesting.** A scalar input has a Sent in choice of `body`, `path`, `query` or
  `header`, which shows `body` when the file has no `in` key and removes the key when `body` is chosen.
  `object` and `list` inputs show nested field rows instead, down to four levels with the input as
  level 1. Choosing one adds `fields` and removes `in`, and a scalar type removes `fields`. The compiler
  reports placement and path errors after the save, see
  [structured payloads](integration-resources.md#structured-payloads).
- **Read from.** An optional Read from box holds a JSON Pointer into the response, and an empty box
  removes `source`.
- **Unknown keys.** A key the editor does not show, in the file, in an operation or in `auth`, is saved
  unchanged.

## Identity provider editor

The identity provider editor's Form tab shows the provider's fields in the five
[shared sections](studio-resource-form.md#sections) General, Sign-in, Claims, Roles and Assurance, each
field with its label and [help](frontend.md#platform-field-help), see
[identity provider resource](identity-provider.md#identity-provider-resource). Sign-in and Claims show
only for `oidc` and `saml`. General holds the name, `type` and description. Add assurance level sits on
the Assurance heading, and the JSON view stays as the second view of the same draft.

- **Type.** Choosing `accounts`, `oidc` or `saml` removes the keys the new type forbids and adds the
  bindings it requires, with an empty name. An `accounts` provider shows only General, Roles and Assurance.
- **Bindings.** A binding is a name box, and its help says what it holds and that only the name is
  stored. No value is shown or stored. `oidc` shows the issuer, the client ID, and a choice of client
  credential: a client secret, or a client key for `private_key_jwt`, each option with its own help.
  `saml` shows the metadata. Name and Type carry the shared required mark.
- **Label.** The "Name on the sign-in button" field sits in Sign-in, after the bindings, and has its texts in every locale, see [label texts](studio.md#label-texts).
- **Claims.** The display name, email and locale claims are name boxes that show their defaults,
  `name`, `email` and `locale`, until the author types another name.
- **Roles.** The roles claim is a name box that shows `roles` by default. `values` are rows of a claim
  value and a role picked from the application's roles, one field to a line. Add claim value sits on the
  title row, and each row's Delete is in its "…" menu. With no rows, a claim value gives the role of the
  same name, and the editor says so.
- **Assurance.** `acr` is a list of text rows. Add assurance level sits on the Assurance heading, and each
  row's Delete is in its "…" menu. With none the key is removed. A read-only tab, such as while the JSON
  has an error, shows no Add and no menu, as the Lists bullet of the [process editor](#process-editor) says.
- **Unknown keys.** A key the editor does not show is saved unchanged.

### A site's sign-in

On a `site`, `signIn` is a list of the application's identity providers, picked by name, with add and
remove. Add provider sits on the heading, and each row's Remove is in its "…" menu. A read-only tab shows
neither. An empty list removes `signIn`, so the site allows every provider, and the form says so.

## Scenario editor

The scenario editor's Form tab has four sections: General, with the scenario's name, its [description](studio.md#editor)
and `now`; Users, with their roles; Stubs; and Steps, a list with a panel for each step type. Add user,
Add stub and Add step sit on their section's heading, and a read-only tab shows none of them. The JSON view
stays as the second view of the same draft. Scenarios themselves are in [test scenarios](test-scenarios.md).

- **Field names.** Every field, stub and step property shows a label and help text in the app's
  languages, so a person sees "Moves time by" and not the property name `by`.
- **Steps.** Each step has a type. Changing the type keeps the properties the new type takes and drops
  the others, and keys the editor does not show stay in the file. A step's Move up, Move down and Delete
  are in its "…" menu on the card's title row. Move up is off on the first step and Move down on the last.
  A read-only tab shows no menu.
- **Rows.** A key and value list, such as an expected notification's values or a stub's output, has Add on
  its title row, and each row's Delete is in its "…" menu. Users and stubs keep their Delete in a menu too.
  A read-only tab shows neither, as the Lists bullet of the [process editor](#process-editor) says.
- **Pickers.** Entities, processes, roles and connectors are picked by name. A `record` is picked from
  the aliases that earlier `createRecord` steps save, and a `step` and its `outcome` come from the
  processes that the record started.
- **Values.** The fields of the step's entity show as typed cells, and a value the entity does not name
  stays as a text row. A money value is a number box that stores a number. A child collection's rows
  cannot be added in the form, so the JSON view writes them. A step whose entity is not in the folder edits its `values` as a JSON object,
  and the text is saved only when it is one.
- **Notifications.** An `expectNotification` step picks its recipient kind, a user, a role or an email
  address, and a channel, `inApp`, `email` or none, which reads both channels. Its expected values are
  key and value rows, like a stub's output.
