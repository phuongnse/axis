# Task inbox widget

This page describes the task inbox widget, which lists a person's open tasks, and the task page, where
the person decides one, both through the [task API](task-api.md). The rest of the frontend is in
[frontend](frontend.md).

## Task inbox widget

A page whose widget is a `taskInbox` lists the signed-in user's open tasks in the application.

```json
{
  "id": "e1a2b3c4-d5e6-4f70-8a9b-0c1d2e3f4a5b",
  "kind": "page",
  "name": "MyTasks",
  "formatVersion": 1,
  "title": { "textKey": "pages.myTasks.title" },
  "widgets": [{ "type": "taskInbox", "name": "inbox" }]
}
```

- **Compile.** A `taskInbox` names no `entity`, `dataSource` or `form`, otherwise it is `AXC0060` at
  that property.
- **Metadata.** The widget has `"entity": null`, `"dataSource": null`, `"form": null` and a
  `tasksPath`, such as `"/api/apps/PurchasingApp/tasks"`. `tasksPath` is `null` on every other widget
  and never left out.
- **Order.** Rows keep the task API's order until the person sorts by Subject or Due: the earliest due
  date first, and tasks with no due date last. The SPA never sorts them itself.
- **Columns.** The columns are the task's label, from the step's label text key, the subject's label
  and the due date, formatted as a `date-time` cell. An empty subject label or due date shows the muted
  em dash, see [no value is a dash](actions-and-controls.md#text).
- **Due time** _(planned for M39)_. A due date with a `dueTimeZone` shows in that time zone, with the zone
  named, and with the `workingTimeLeft` beside it. A due date with no `dueTimeZone` shows as any
  `date-time` cell does.
- **Labels from each release.** The inbox loads the texts of each release its rows name through
  `releaseId`, once per release, so tasks from several releases each show their own release's labels.
  The table stays loading until those texts have arrived, and a failed load shows the inbox's load
  error.
- **Tags.** An overdue task shows a red "Overdue" tag beside its due date. A task with `claimedBy` set
  shows a "Claimed by you" tag beside its label. The task list returns a claimed task only to its
  claimer, so the signed-in user is always the claimer, and the inbox does not fetch the user.
- **At risk** _(planned for M39)_. A task with `atRisk` that is not overdue shows an "At risk" tag in the
  warning tone beside its due date. It is a `StatusTag`, see [shared widgets](shared-widgets.md). "Overdue"
  stays the tag of a past due time.
- **View.** The table has Search, Filter on Subject and Due, Columns, a sort on Subject and Due, Refresh
  and paging, see [tables](actions-and-controls.md#tables). Search, filter, sort, columns, page and page
  size live in the URL, and the widget sends `search`, `filter`, `sort`, `page` and `pageSize` to the
  task API, see [list my tasks](task-api.md#list-my-tasks). The Task column stays first, with no sort or
  filter, because its label is resolved in the browser. A new view starts at page 1, and a failed load
  shows under the toolbar.
- **Links.** Each row opens the task page at `/{site}/{inboxpage}/{taskId}`, with the inbox page name
  in lower case.
- **Record links** _(planned)_. The Record column, which shows the subject's label, links to the subject's record page.
  The link is built from the row's `subject.entity` and `subject.id`, see [list my tasks](task-api.md#list-my-tasks).
  It is plain text when the person cannot open the record.
- **Signed out.** With nobody signed in, the task API answers `401`, and the widget shows the sign-in
  prompt like any other call.
- **Texts.** The platform texts `shell.tasks.*` hold the column headers, the empty text, the load error
  and the two tags.
- **Live reloads.** _(planned for M43)_ The inbox reloads its list when a `tasks` event arrives, see
  [live pages](frontend.md#live-pages).

### Completed tasks

_(planned for M43)_

A choice between Open and Completed is kept in the address. The Completed view sends `state=completed` to the
task API, see [list my tasks](task-api.md#list-my-tasks). Its table shows a Completed column and an Outcome
column. Its rows have no outcome buttons, and each row opens the task page, which shows the decided task.
The platform texts `shell.tasks.*` hold the choice and the two column headers.

### Deciding several tasks

_(planned for M43)_

In the Open view, each row has a checkbox. A selection shows an "N selected" bar, as the
[bulk actions rule](actions-and-controls.md#tables) says. The bar has one button for each outcome that the list
items of every selected task name in `outcomes`. A click calls `complete-many` with that outcome and the selected
ids, see [deciding several tasks](task-api.md#deciding-several-tasks). The page then shows a result for each task,
with its subject and its outcome or its reason. It clears the selection and reloads the list. The platform texts
`shell.tasks.*` hold the bar, its buttons and the results.

## Task page

The address `/{site}/{inboxpage}/{taskId}` shows one task, under the page of its task inbox, with the
form of its step over the subject record and a button for each outcome.

- **Labels.** The page loads the texts of the task's `releaseId` with
  `GET /api/sites/{path}/texts/{locale}?release={releaseId}`, for the current and the fallback locale.
  The step, form and outcome labels use those texts first, then the site texts and the platform texts.
  If they fail to load, the page shows its load error.
- **What it shows.** The page shows the step's label, the subject's label, the due date, with the same
  "Overdue" tag as the inbox when the task is overdue, and the form's sections over the subject record.
  The layout and the field types come from the task read's `form`, and the values from the subject
  record, read through the record API. The task page has no history panel.
- **Due time and At risk** _(planned for M39)_. The due date shows in its `dueTimeZone` with the working
  time left, and an "At risk" tag shows as in the inbox.
- **Conditions and steps.** The form follows the form widget's conditions, see
  [form widget conditions](form-widget-conditions.md), through the task's
  [form state route](task-api.md#form-state-of-a-task). The call sends `values` and `touched` and no
  `recordId`, and a field the answer hides or locks is not sent with the completion. A form with
  `steps` is a wizard, see
  [form widget conditions](form-widget-conditions.md#form-widget-conditions-and-steps), with the outcome
  buttons on the last step.
- **Outcomes.** The page has one button per outcome, with the outcome's label. A click sends the
  outcome, the changed editable values and the record's `version` to the complete endpoint. The
  `version` is sent even when no field changed, and the task API then ignores it. On success the page
  goes to the inbox page's plain address, without the paging the person came from.
- **Claim and Release.** An open task shows a Claim button and a Release button, each only when the
  task read's `commands` lists it, so none when `commands` is empty. `reassign` is ignored here. A click
  posts to the claim or release route, see [task deadlines](task-deadlines.md#claim-and-release), and
  the page then shows the task the route returns, with its new `commands`. The page keeps the form it
  already shows, so values the user typed stay. While a click is pending, its button shows as loading
  and the other is disabled. The page shows no claimed tag, because it does not know the signed-in
  user's id.
- **Reassign.** The task page shows Reassign, see
  [inbox and task page](task-deadlines.md#inbox-and-task-page).
- **Comments.** _(planned for M43)_ An open task has a comments section, for the people who may act on it, see
  [task comments](comments.md#task-comments).
- **Expired task.** An `expired` task, whose deadline passed before anyone decided it, shows only a
  message that the deadline passed, with no form, outcome buttons, Claim or Release.
- **Decided task.** A task that is neither `open` nor `expired` shows only the already completed
  message, with no form or outcome buttons, so nobody retries a decision that cannot succeed.
- **Texts.** The platform texts `shell.task.*` hold the assigned elsewhere, already completed and
  expired messages, the Claim, Release and Reassign labels, the refused and failed messages of those
  actions, the texts of the Reassign dialog, and the load and complete errors.

### Deciding from an email

_(planned for M43)_

The link `/{site}/{inboxpage}/{taskId}?outcome=<name>` opens the task page with that outcome chosen. Signing in
runs as usual, with its second step, and then the page shows the task. One Confirm button sends the completion.
A form that needs a value shows it first. The link carries no token, so a forwarded link decides nothing for
someone who may not act on the task. The texts are the `shell.task.*` texts.

### Answers

The page shows each refusal and failure in place:

| Answer                                                                                        | What the page does                                                                                                                   |
| --------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------ |
| `403` or `409` from claim or release                                                          | A platform text that the action is no longer possible, never the server's problem title, then it loads the task again                |
| `403` from complete                                                                           | A message that the task is assigned to someone else                                                                                  |
| `409` from complete for a decided task                                                        | A message that the task is already completed, then it loads the task again                                                           |
| `400` from complete                                                                           | Its keys mapped as the form widget does: `/values/<field>` under that field, and every other key, such as `/outcome`, above the form |
| `409` from complete that names keys                                                           | A stale `/version` or a duplicate unique value, mapped the same way, with no reload button                                           |
| A reload, after a `409` or on load, of a task that is no longer open                          | The expired or the already completed state above                                                                                     |
| `401` from the task read                                                                      | Sign-in starts, as for any other call                                                                                                |
| Any other failure to load the task or its subject record, to claim or release, or to complete | A generic error message                                                                                                              |
