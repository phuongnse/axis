# Process inspector widget

This page describes the process inspector widget, which lists the instances of one process, opens one
to show where it is and what it did, and runs the commands that fix failed work, all through the
[inspection API](process-inspection.md#process-inspection). The server checks every request and every
command, so the widget never decides who may inspect or act. The rest of the frontend is in
[frontend](frontend.md).

## Widget

A page whose widget is a `processInspector` names one process.

```json
{
  "type": "processInspector",
  "name": "inspector",
  "process": "PurchaseRequestApproval"
}
```

- **Compile.** The `process` must name a loaded process, otherwise it is `AXC0192` at
  `/widgets/{i}/process`. The widget names none of `entity`, `dataSource` and `form` (`AXC0060` at that
  property).
- **Metadata.** The widget has `"entity": null`, `"dataSource": null`, `"form": null`,
  `"tasksPath": null`, `"access": null` and `"actions": []`. It adds `process`, an `instancesPath`, the
  list endpoint, such as
  `"/api/apps/PurchasingApp/processes/PurchaseRequestApproval/instances"`, and a `deadLettersPath`, the
  dead letter list endpoint, such as
  `"/api/apps/PurchasingApp/processes/PurchaseRequestApproval/dead-letters"`, so the SPA never builds a
  path itself. The record history uses `process` to link to the page, see
  [history](form-widget.md#history).
- **Access.** The page endpoint answers `403` to a user without an `inspect` grant on the process, and
  site navigation leaves the page out.

## Instance list

The page lists the process's instances, newest first in the API's order, with their record, state,
step, release and times.

- **Toolbar.** The table has Search, Filter, Columns, a sort on every column and Refresh, see
  [tables](actions-and-controls.md#tables). Search looks in the record label and the step. Filter has every
  column: Record and Step as text, State as its five states, Release as a number, and Started and Ended as
  dates. Columns shows, hides and orders the columns, with Record always first. Refresh loads the list again.
- **Server.** The widget sends `search`, `filter`, `sort`, `page` and `pageSize` to the instances path as they
  are, and the server applies them, see [process inspection](process-inspection.md#instance-list). The
  column keys are the route's field names: `subjectLabel`, `state`, `step`, `releaseNumber`, `createdAt` and
  `endedAt`. Started and Ended filter by their UTC day and sort by the exact time. The browser does not turn
  a filter into `state` values.
- **Paging.** `search`, `filter`, `columns`, `sort`, `page` and `pageSize` live in the URL, as for a table.
  A new search, filter or sort goes back to page 1. A `state` in the address is not read.
- **Record.** The first column, headed Record, is the instance's record, named by `subjectLabel`, and
  is the link that opens the instance. When `subjectLabel` is `null` the link reads
  `shell.inspector.noSubject` for an instance without a subject, and `shell.inspector.open` when the
  user may not read the display field. There is no Open column and no record id.
- **Step.** The step column shows the step's name, because a list row carries no label key.
- **Release.** The Release column shows `releaseNumber`, the release's number such as 3, never its
  id. A `null` `releaseNumber`, for a release with no history row, shows the muted dash.
- **Ended.** An instance still running shows a muted "—" under Ended.
- **States as tags.** The State column shows each state as a `StatusTag` in its tone, see [which widget](shared-widgets.md#which-widget).

## Instance page

A row opens `/{site}/{page}/{instanceId}`, with the page name in lower case. `new` and an id in the
wrong form show the not-found page, and the instance is not requested.

- **Heading**. The heading is the process's title, through `processTitle`, which gives the process's name
  where its release gives no title. Its state is a `StatusTag` beside the title, and Process ("Quy trình") is the
  line under it, see [page header](shell.md#page-header). The commands sit on the heading's right, through
  `PageActions`: Open in Studio ("Mở trong Studio"), a secondary button, then Cancel instance and Retry, see
  [commands](#commands). Studio is another site, so Open in Studio is a named button, never a link in the data,
  see [opening things](actions-and-controls.md#opening-things). It shows only when the detail's `studio` is not
  `null`, which is for a person who may author the application.

- **Failure banner**. Only a failed instance has a banner. It is an `AxisAlert` with `role="alert"`
  under the heading, in one line that says the step where the instance stopped and why, worded from `problem`,
  see [step errors](process-inspection.md#step-errors-in-inspection). A step whose entry has no `problem` code shows
  its stored error instead. A waiting or running instance has none, as its state tag says what it is.

- **Facts**. Under the heading and above the tabs, a strip of facts through `TermList`'s `row`, in view
  whichever tab is open. Each fact sits at its own width, `spacing-xl` apart, wraps when the line is full, and each
  value stays on one line. The facts are, in order, Record, Started by, Started via, Started, Ended, Duration,
  Release and Instance ID. Record is a `RecordLink`, named as the instance list's Record column names it, and is
  left out for a process with no subject. Started by names a person through `PersonName`, or the trigger's,
  endpoint's or schedule's name, or the parent process's title, which opens the parent instance where the site has
  a page that inspects it. Started via says how: the action's label, such as Submit for approval, or Trigger,
  Endpoint, Schedule or Parent process ("Bắt đầu qua"). Started shows the date and time as data, in the system's
  format. Ended and Duration read Not set until the instance ends. Release is the release's number as plain text.
  Instance ID is the copyable id. A fact the instance always has always shows, Not set while empty. The facts come
  from the [instance detail](process-inspection.md#instance-detail). The page reads the navigation pages' metadata
  only when the instance has a parent or a child. A relative whose process no navigation page inspects shows
  without a link.

- **Tabs**. Three tabs, Diagram, History and Data, Diagram first, with no counts. The open tab is kept
  in the address as `tab`, through `useAddressTab`. Its value is `history` or `data`, and Diagram, the first tab,
  is left out. Diagram shows the process's graph with the steps the instance ran, see [process graph](#process-graph).

- **Data**. A `TermList` of two facts, each name above its value. Input is the parameters the instance
  started with, as a `ValueTable`, or "It started with no input.". Output is the step that ended the instance and
  the values it ended with, as `result` gives them, or "The instance has not ended yet." while it runs or waits.
  Output reads Not set for an instance that failed or was cancelled. A hidden value follows
  [values and masking](process-inspection.md#values-and-masking).

- **History** _(planned)_. One timeline of the instance's runs, newest first, through `AxisTable`'s group rows, in
  place of separate history, task, call and sub-process tables. A run of a step is a row. It opens to a row for each
  task, call and sub-process it started, in the same columns, and a call opens to a row for each attempt. A call's
  row holds its Resolve button, see [Resolve](#resolve). Its columns are:
  - **Step**, the step's label with its kind under it, and "(run 2)" after the kind where the step ran more than once.
  - **What happened**, one sentence from fixed texts through `useRunWords`, never free text, such as "createPurchaseOrder (ERP) succeeded on attempt 3."
  - **State**, a `StatusTag` in the tone that `stateText` gives. A kind, such as a call's, is plain text.
  - **Started**, through `EventTime`, and **Duration**.

  The toolbar's search, filter, sort and page are in the address under the `history.` prefix. Search goes through the
  words of a run and of the rows inside it. Filter has Step, State, Started and Duration, shown as chips, and a date
  filters on the local day of its time. Sort is by State, in the order Failed, Waiting, Running and Done, or by Started
  or Duration. Paging and Refresh complete the toolbar. The timeline holds its rows and does these itself. A click on
  a run's step opens that run in the side panel, as the step's run rule below says.

- **A step's run**. A click on a step, or on a run's step in History, chooses the step and opens
  `StepRunPanel` in the page's `SidePanel`, see [layout](shell.md#layout). The page opens no panel at first, and
  Close shuts the panel while the step stays chosen. The address keeps `step`, the step's name, `run`, the run's
  number, left out for the latest run, and `stepTab`, the panel's tab, left out for the first tab. The panel's title
  is the step's label, with the run's state as a `StatusTag` beside it. Its line is the step's kind alone, or its
  operation's when it has one, such as Calls a connector. An operation is named by what its run did: a call reads
  Calls a connector or Calls an extension package, a child instance Starts a sub-process, and what the run made
  reads Raises an event, Sends a notification, Creates a record or Changes the record. A step run more than once
  has a run chooser on top. Its tabs are the parts below that apply to the run, in their order, and Error only for a
  failed run. A run with one part has no tab bar. A step the instance has not reached opens the panel with its
  title and its line, and says "This step has not run." ("Bước này chưa chạy.") in place of the parts.

- **Overview**. The run's facts, each a term above its value in `TermList`, each on a row of its own
  unless two belong together:
  - Started beside Ended, then Duration.
  - A task's Assigned to, Due, with Overdue while it is open past its due time, and Completed by.
  - A call's Connector ("Bộ kết nối") or Extension package ("Gói mở rộng"), with its version after a space, beside
    Operation ("Thao tác"), then Attempts.
  - A sub-process's instance, by its process title, a link where the site inspects it, with its state. The detail
    names only the children whose process the user may inspect.
  - A wait's Waits for and the event's label, then Gives up at, beside Then goes to. A timer's Goes on at. A time set
    ahead is the system's date and time alone.
  - Came from, then a decision's Branch ("Nhánh") or a task's Outcome, beside Went to. With no such fact, Came from
    sits beside Went to. A decision's branch shows only where its author labelled it.
  - Trace ID, its first 8 characters, copying whole.

  A step named here is an inline link to its run, with "(run 2)" after it where it ran more than once. Connector and
  operation are plain text, see [opening things](actions-and-controls.md#opening-things). A fact the run always has
  reads Not set while empty, Ended and Went to included, see [fields](actions-and-controls.md#fields).

- **Input and Output**. Both are terms, each name above its value, and a value's table is `ValueTable`.
  A JSON body is a `BodyView`. Hidden values follow [values and masking](process-inspection.md#values-and-masking)
  and [call input kept](process-inspection.md#call-input-kept).
  - Input: a connector call's Request, its method, then `spacing-xs`, then its address on one line, then Query
    parameters, Headers and Body. A package call's input by its values. A decision's the values its conditions read.
  - Output: a connector call's answer from the attempt that settled it, with Status as plain text, then Headers and
    Body. A package call's returned values. The record's changed fields as a diff: the value before on a "−" line on
    the `danger` tint, and the value after on a "+" line on the `success` tint, read as "Changed from X to Y". An
    event it raised, as Event raised and Event fields, which only a `raiseEvent` step raises. The people it
    notified, as Notified, Channels and Subject. A record it created.

  A step that makes nothing has no Output tab.

- **Attempts**. One `compact` table, its columns #, Result (a `StatusTag`), HTTP and Duration, newest
  first. A row folds open through `details` to its Started, Trace ID, error and answer. The tab shows the attempts
  made, never the step's retry policy, which is the author's setting. A call with no attempts shows "No attempts
  yet." in place of the table. An empty cell shows `EmptyDash`.

- **Error**. The run's `problem` worded through `shell.stepProblem.<code>` in the reader's language,
  falling back to the stored error. No stack trace or exception message shows, see
  [step errors](process-inspection.md#step-errors-in-inspection).

- **Every current step.** _(planned for M38)_ A step is marked Current when the root path is at it, or when a
  running or waiting path is at it. A step that several paths are at is marked once, with the number of paths, as in
  "Current, 3 paths". Steps inside a block are listed under the block, indented, each with its branch's name. A
  repeat's steps show no branch.
- **Paths.** _(planned for M38)_ After the calls, a Paths part is a table of the instance's `paths`. Its columns are
  Branch or pass, Step, State, Revision, Started and Ended. A branch shows its name, and a pass shows "Pass 3". Step
  shows the step's label, as History does. Each column has Filter under the `paths.` prefix.
- **Variables.** _(planned for M38)_ After the Paths part, a Variables part lists each variable with its Name, Type and
  Value. A list shows its values as a list, a `null` value shows the muted dash, and a hidden value shows "hidden", as
  a call input does.
- **Parts left out.** _(planned for M38)_ A part is left out only when it does not apply: Paths when the pinned graph
  has no block, and Variables when the process declares no variables. Otherwise a part with no rows shows its empty text.
- **Path column.** _(planned for M38)_ History, tasks and calls have a Path column with the path of their `pathId`,
  shown as Paths shows it, and the muted dash for the root path. Filter on the column works on the text the cell shows,
  as a step's does.
- **Labels.** Step labels come from the texts of the instance's `releaseId`, loaded with
  `GET /api/sites/{path}/texts/{locale}?release={releaseId}`, as the inbox does. A step without a label
  shows its name.

## Process graph

A process is drawn as one graph, with the same layout, cards and controls wherever it shows.

- **One graph** _(planned)_. `ProcessGraphCanvas` draws a process on an instance's Diagram tab and in the process
  designer. React Flow draws the graph, and ELK lays it out.
- **Layout.** ELK's layered layout, `elkjs`, places the steps in the process's order from left to right. Every
  transition is routed in right angles with rounded corners, and no route crosses or runs through a step. A
  transition back to an earlier step goes round the other steps. No part of a route is shorter than 16 px. The graph
  is laid out with the heights its cards are drawn at.
- **Surface and zoom.** The graph lies on `fillSubtle`, with no pattern. It never zooms in past its own size.
- **What an instance took.** The steps an instance ran and the transitions it took are drawn in `successText`. The
  rest are muted, and their transitions are dashed and thinner.
- **A step's card**. A step is a card on `surface`, and every card is one width. Its type's icon sits in a tile on
  `fillSubtle`, and the type is the icon's tooltip. Its label is bold, at most two lines, then an ellipsis. The whole
  label is in a tooltip and in the card's accessible name. On an instance, the card shows the duration of the step's
  last run. A step run more than once shows its run count with that duration, such as "2 runs, last 1 day 17 hr".
  A duration under a second reads "< 1 sec". A run that has not ended has no duration, so its card shows only the run
  count, or nothing.
- **State icon.** On an instance, a step's state is an icon on the card's top-right corner, on a disc of `surface`
  that cuts into the border. Its words are in its tooltip, and colour is never the only sign:
  - done, a check in `successText`;
  - running or waiting for an event, a clock in `infoText`;
  - waiting for a person, an exclamation mark in `warningText`;
  - failed, a cross in `dangerText`;
  - cancelled, a minus in `textMuted`;
  - not reached, no icon.
- **Border**. Every card has a `borderSubtle` border, whatever its state. The step the instance is at has a thicker
  `primary` border, drawn inside the card so the card keeps its size. That step is chosen when the page opens, and
  the view opens on it. Choosing another step moves the `primary` border to it. In the designer, a step with a
  diagnostic keeps a `danger` border, see [diagnostics](process-designer.md#diagnostics). A viewer's steps show no
  handle dots.
- **Transition labels**. A transition's label sits beside its line, on `fillSubtle`, which cuts the line. The label is
  the author's `label` of the outcome or branch. Without one, a decision branch shows its name, and the fallback shows
  Otherwise. A wait for an event shows the event's label, or its name where it has none. A timeout shows "After {duration}" ("Sau {duration}"). A
  condition is not written in words, since a generated sentence explains a branch less well than the author's own
  words. The labels come from the [instance detail](process-inspection.md#instance-detail).
- **Controls** _(planned)_. `ProcessGraphControls` gives each control a tooltip in the reader's language:
  - Zoom in and Zoom out.
  - Show the whole process.
  - Go to the current step ("Tới bước hiện tại"), on an instance only. It also chooses the current step and opens
    its run.
  - Full screen, the browser's own, left with Esc or Exit full screen.
  - Find a step, a searchable list of the steps in the process's order, at the graph's top left and as wide as a card.
    Choosing a step moves the view to it and chooses it. On an instance, it also opens that step's run, see
    [the instance page](#instance-page).
- **Overview.** At the graph's corner, an overview draws each step in its state's tone and marks the part in view.
  Dragging or clicking in it moves the view.
- **Keys.** While the graph has the focus, + and − zoom, 0 shows the whole process, and F goes full screen. Each
  of those buttons names its key in `aria-keyshortcuts`. Go to the current step has no key.
- **Moves.** Every move of the view is animated over 300 ms. Under [reduced motion](frontend.md#reduced-motion), a
  move happens at once.
- **Download.** In the process designer only, the controls also hold Download, see
  [download](process-designer.md#download).
- **No React Flow controls.** React Flow's own controls and attribution are not drawn, as its MIT licence allows.

## Commands

The instance page runs the operator's commands on the instance and its calls, each with a reason.

- **Buttons.** The page has a Cancel instance and a Retry button for each of those commands that the
  instance detail's `commands` lists. With none listed, it has no button, and a command name the SPA
  does not know is left out.
- **On the heading**. Cancel instance and Retry are secondary `AxisButton`s on the heading's right, after
  Open in Studio, through `PageActions`.
- **Reason.** A button opens a dialog that asks for a reason. The reason is required, must not be
  blank and has at most 500 characters, and Send is blocked until it is valid.
- **Answer.** On success, the dialog closes and the page reads the instance detail again, so it shows
  the new state, tasks, calls and `commands`. On a `400`, `403`, `404` or `409`, the dialog shows the
  problem's title. A `401` shows the sign-in prompt, and any other failure shows a generic text.
- **Retry warning.** The Retry dialog first warns when a call on the instance's current step has
  `failed`, and that call is a package call whose `idempotent` is not `true`. A failed connector call
  gets no warning.

- **Cancel branch, Cancel pass and Cancel task.** _(planned for M38)_ A row of Paths whose `commands` lists `cancel`
  has a Cancel branch icon button for a branch, or a Cancel pass icon button for a pass. A task row whose `commands`
  lists `cancel` has a Cancel task icon button. Each is a stop icon with its name as a tooltip, as the dead letters'
  buttons are. Each button opens the shared reason dialog, titled with its own name, and posts the reason to
  `{instancesPath}/{id}/paths/{pathId}/cancel` or `{instancesPath}/{id}/tasks/{taskId}/cancel`. The reason rules
  and the answer are those of Cancel instance.

### Resolve

A call whose `resolvable` is `true` has a Resolve button, whether it is a connector or a package call. A
call that is not resolvable has none, because the server decides what may be resolved and by whom.

- **Dialog.** The button opens a dialog that asks for a reason, with the rules of the other commands,
  and an outcome, `succeeded` or `failed`. The outcome has no default, and Send is blocked until one is
  chosen.
- **Outputs.** For `succeeded`, the dialog shows one input for each declared output of the call,
  labelled with its name and type, with the required ones marked and checked before Send. A `boolean`
  output is a true or false choice, and every other type is text, so a date or date-time is typed as
  the API takes it. The values go out typed: an integer or decimal keeps every digit, a boolean is
  `true` or `false`, and an empty optional output is left out. A `failed` outcome sends no output.
- **Answer.** On success, the dialog closes and the page reads the instance again. On a `400`, the
  message of each `/reason`, `/outcome` and declared `/output/{name}` error shows under its field. The
  problem's title shows in the dialog, and the message of any other key follows it there. Any other
  refusal shows its title, and any other failure shows a generic text.

## Dead letters

Under the instance list, a Dead letters part lists the process's open dead letters from
`{deadLettersPath}?state=open`, newest first, with the kind, the attempts, the last error and the time
it was dead-lettered.

- **Toolbar.** The table has Filter, see [tables](actions-and-controls.md#tables). Filter has every
  column: Kind and Last error as text, Attempts as a number, and Created as a date. It has no search
  box, no sort, no Columns and no saved views.
- **Server.** The widget sends `filter` with `state=open` to the dead letters path, and the server
  applies it, see [process events](process-events.md#routes). The column keys are the route's field
  names: `kind`, `attempts`, `lastError` and `createdAt`. Created filters by its UTC day.
- **Rows.** The kind shows as the API sends it. With none open, the section shows an empty text.
- **Paging.** The filter and the page live in the URL as `deadLetters.filter` and `deadLetters.page`,
  because the URL's plain `filter` and `page` belong to the instance list. A new filter goes back to
  page 1. A `state` in the address is not read.
- **Buttons.** A row has a Redeliver and a Discard button for each of those commands that the item's
  `commands` lists, which it does for an open dead letter and a user with the `retry` grant. A user
  with only `inspect` sees no button, and a command name the SPA does not know is left out. Redeliver,
  with a send icon, and Discard, with a trash icon in `dangerText`, are icon buttons with their names as
  tooltips.
- **Reason.** A button opens the same reason dialog as Cancel and Retry, with the same rules for the
  reason, and posts it to `{deadLettersPath}/{id}/redeliver` or `…/discard`.
- **Answer.** On success, the dialog closes and the list is requested again, so a settled dead letter
  leaves it. On a `400`, `403`, `404` or `409`, the dialog shows the problem's title, such as the `409`
  for a trigger that is not in the active release. Any other failure shows a generic text.
- **Failure.** If the list cannot be loaded, a `403` included, an error shows inside the section and
  the instance list stays usable.

## Errors and texts

The widget shows a failure where it happens, and its words come from the platform texts.

- **Errors.** With nobody signed in, the API answers `401` and the widget shows the sign-in prompt like
  any other call. A `403` shows a message that the user may not inspect the process. Any other failure
  shows a generic load error. On the instance list, that error sits under the toolbar in place of the rows,
  so the person can clear the search or filter that caused it.
- **A missing instance**. A well-formed id that the inspection API answers `404` for keeps the page's
  heading. Under it, one line of body text says "This process instance does not exist, or you cannot open it."
  ("Lượt chạy quy trình này không tồn tại hoặc bạn không có quyền mở.", `shell.inspector.notFound`). On the same line,
  one link, "Go to {page}" ("Đi tới {page}", `shell.record.goToList`), as a missing record's line does, see
  [routing](frontend.md#routing). No save bar and no action shows.
- **Texts.** The platform texts `shell.inspector.*` hold the column headers, the section titles, the
  instance states, the call states (`shell.inspector.callStates.*`), the run states (`shell.inspector.runStates.*`), the attempt
  outcomes (`shell.inspector.attemptOutcomes.*`), the step types (`shell.inspector.stepTypes.*`), the "hidden" text, the "No subject" text, the Process and Sub-processes
  labels, the empty texts and the messages. `shell.inspector.deadLetters.*` hold the dead letter
  section's title, column headers, empty text and load error. `shell.inspector.commands.*` hold the
  command buttons, the dialog's labels, the reason errors, the retry warning and the resolve labels and
  errors.
- **New texts.** _(planned for M38)_ The Current mark, the Paths and Variables labels and the Path column are platform
  texts under `shell.inspector.*`. The three cancel buttons and their dialog titles are under `shell.inspector.commands.*`.
- **Page texts** _(planned)_. The tabs, the facts, the Data texts, the timeline's columns and sentences, and the step
  run panel's tabs and terms are platform texts under `shell.inspector.*`, as are the graph's control names, a card's run duration and the timeout's label.
