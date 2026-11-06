# Process inspector widget

This page describes the process inspector widget, which lists the instances of one process, opens one
to show where it is and what it did, and runs the commands that fix failed work, all through the
[inspection API](process-inspection.md#process-inspection). The server checks every request and every
command, so the widget never decides who may inspect or act. The rest of the frontend is in
[frontend](frontend.md).

## Widget

A page whose widget is a `processInspector` names one process.

```json
{ "type": "processInspector", "name": "inspector", "process": "PurchaseRequestApproval" }
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

- **State filter.** A clearable select above the list filters by state, and offers the five states
  with platform texts. Changing the state goes back to page 1. A `state` in the URL that is not one of
  the five is dropped.
- **Paging.** `state`, `page` and `pageSize` live in the URL, as for a table.
- **Record.** The first column, headed Record, is the instance's record, named by `subjectLabel`, and
  is the link that opens the instance. When `subjectLabel` is `null` the link reads
  `shell.inspector.noSubject` for an instance without a subject, and `shell.inspector.open` when the
  user may not read the display field. There is no Open column and no record id.
- **Step.** The step column shows the step's name, because a list row carries no label key.
- **Release.** The Release column shows `releaseNumber`, the release's number such as 3, never its
  id. A `null` `releaseNumber`, for a release with no history row, shows the muted dash.
- **Ended.** An instance still running shows a muted "—" under Ended.

## Instance page

A row opens `/{site}/{page}/{instanceId}`, with the page name in lower case. `new` and an id in the
wrong form show the not-found page, and the instance is not requested.

- **Steps.** The page reads the instance detail and shows the steps as a list in graph order, each with
  its type and its transitions, with no diagram. Steps the instance has run are marked as taken, and
  the step it is at is marked as current, whatever the instance state.
- **History, tasks and calls.** Below the steps come the history rows with the step, the times, the
  decision, the error and the trace ID, then the tasks, then the calls with their attempts. The
  trace ID of a history row and of an attempt is copyable text, empty when there is none, see
  [Attempts](process-inspection.md#attempts). A call whose
  `inputHidden` is `true` shows "hidden" in place of its input. A shown input is a list of its
  top-level properties, where a number keeps its source digits.
- **Subject, timer and parent.** An instance without a subject shows "No subject" in place of the
  subject id. An instance that is `waiting` at a `timer` step shows a Due row with the `dueAt` of that
  step's latest history row, and the row is left out in any other case. An instance that a sub-process
  step started shows a Parent row with the parent's process name.
- **Sub-processes.** After the calls, a Sub-processes section lists the instances that this instance
  started, from the detail's `subProcesses`. Each row shows the child's process, the step of this
  instance that started it, its state, and its start and end times. With none, the section shows an
  empty text, as the tasks and the calls do. The detail names only the parent and the children whose
  process the user may inspect.
- **Links to relatives.** A child's Open link and the Parent row open the instance on the first
  navigation page whose process inspector names its process, with the same lookup as the record
  history's links. A relative whose process no navigation page inspects shows without a link. The page
  loads the pages' metadata only when the instance has a parent or a child.
- **Labels.** Step labels come from the texts of the instance's `releaseId`, loaded with
  `GET /api/sites/{path}/texts/{locale}?release={releaseId}`, as the inbox does. A step without a label
  shows its name.

## Commands

The instance page runs the operator's commands on the instance and its calls, each with a reason.

- **Buttons.** The page has a Cancel instance and a Retry button for each of those commands that the
  instance detail's `commands` lists. With none listed, it has no button, and a command name the SPA
  does not know is left out.
- **Reason.** A button opens a dialog that asks for a reason. The reason is required, must not be
  blank and has at most 500 characters, and Send is blocked until it is valid.
- **Answer.** On success, the dialog closes and the page reads the instance detail again, so it shows
  the new state, tasks, calls and `commands`. On a `400`, `403`, `404` or `409`, the dialog shows the
  problem's title. A `401` shows the sign-in prompt, and any other failure shows a generic text.
- **Retry warning.** The Retry dialog first warns when a call on the instance's current step has
  `failed`, and that call is a package call whose `idempotent` is not `true`. A failed connector call
  gets no warning.

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
  message of each `/output/{name}` error shows under that output's field, and the problem's title shows
  in the dialog. Any other refusal shows its title, and any other failure shows a generic text.

## Dead letters

Under the instance list, a Dead letters section lists the process's open dead letters from
`{deadLettersPath}?state=open`, newest first, with the kind, the attempts, the last error and the time
it was dead-lettered.

- **Rows.** The kind shows as the API sends it. With none open, the section shows an empty text.
- **Paging.** The list has its own page number, which the URL does not hold, because the URL's `page`
  and `pageSize` belong to the instance list.
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
  any other call. A `403` shows a message that the user may not inspect the process. A `404` on the
  instance page shows a message that the instance does not exist, inside the page. Any other failure
  shows a generic load error.
- **Texts.** The platform texts `shell.inspector.*` hold the column headers, the section titles, the
  instance states, the "hidden" text, the "No subject" text, the Due, Parent, Process and Sub-processes
  labels, the empty texts and the messages. `shell.inspector.deadLetters.*` hold the dead letter
  section's title, column headers, empty text and load error. `shell.inspector.commands.*` hold the
  command buttons, the dialog's labels, the reason errors, the retry warning and the resolve labels and
  errors.
