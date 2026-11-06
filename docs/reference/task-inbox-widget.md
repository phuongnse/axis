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
- **Order.** Rows keep the task API's order: the earliest due date first, and tasks with no due date
  last. The SPA does not sort them again, and there is no sort.
- **Columns.** The columns are the task's label, from the step's label text key, the subject's label
  and the due date, formatted as a `date-time` cell. An empty subject label or due date shows the muted
  em dash, see [no value is a dash](actions-and-controls.md#text).
- **Labels from each release.** The inbox loads the texts of each release its rows name through
  `releaseId`, once per release, so tasks from several releases each show their own release's labels.
  The table stays loading until those texts have arrived, and a failed load shows the inbox's load
  error.
- **Tags.** An overdue task shows a red "Overdue" tag beside its due date. A task with `claimedBy` set
  shows a "Claimed by you" tag beside its label. The task list returns a claimed task only to its
  claimer, so the signed-in user is always the claimer, and the inbox does not fetch the user.
- **Paging.** `page` and `pageSize` live in the URL, as for a table.
- **Links.** Each row opens the task page at `/{site}/{inboxpage}/{taskId}`, with the inbox page name
  in lower case.
- **Signed out.** With nobody signed in, the task API answers `401`, and the widget shows the sign-in
  prompt like any other call.
- **Texts.** The platform texts `shell.tasks.*` hold the column headers, the empty text, the load error
  and the two tags.

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
- **Expired task.** An `expired` task, whose deadline passed before anyone decided it, shows only a
  message that the deadline passed, with no form, outcome buttons, Claim or Release.
- **Decided task.** A task that is neither `open` nor `expired` shows only the already completed
  message, with no form or outcome buttons, so nobody retries a decision that cannot succeed.
- **Texts.** The platform texts `shell.task.*` hold the assigned elsewhere, already completed and
  expired messages, the Claim, Release and Reassign labels, the refused and failed messages of those
  actions, the texts of the Reassign dialog, and the load and complete errors.

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
