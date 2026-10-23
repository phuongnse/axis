# Task inbox widget

Detailed reference for the task inbox widget and the task page of the SPA. The conventions of
[architecture.md](../architecture.md) apply: sections marked _(planned for Mx)_ are not built yet,
and Dn refers to [decisions.md](../decisions.md). The rest of the frontend is in
[frontend.md](frontend.md).

- **Task inbox widget**. A page whose widget is a
  `taskInbox` lists the signed-in user's open tasks in the application,
  through the [task API](task-api.md).

  ```json
  {
    "id": "e1a2b3c4-d5e6-4f70-8a9b-0c1d2e3f4a5b",
    "kind": "page",
    "name": "MyTasks",
    "formatVersion": 1,
    "title": { "textKey": "pages.myTasks.title" },
    "widgets": [{ "type": "taskInbox" }]
  }
  ```

  - **Compile.** A `taskInbox` names no `entity`, `dataSource` or `form`,
    otherwise it is `AXC0060` at that property.
  - **Metadata.** The widget has `"entity": null`, `"dataSource": null`,
    `"form": null` and a `tasksPath`, such as
    `"/api/apps/PurchasingApp/tasks"`. `tasksPath` is `null` on every other
    widget and never left out.
  - **Order.** Rows keep the task API's order: the earliest due date first,
    and tasks with no due date last. The SPA does not sort them again.
  - **Columns.** The task's label, from the step's label text key, the
    subject's label and the due date, formatted as a `date-time` cell. An
    empty subject label or due date is an empty cell.
    The inbox loads the texts of each release its rows name through
    `releaseId`, once per release. Tasks from several releases each show
    their own release's labels. The table stays loading until those texts
    have arrived, and a failed load shows the inbox's load error.
  - **Paging.** `page` and `pageSize` live in the URL, as for a table. There
    is no sort.
  - **Links.** Each row opens the task page at
    `/{site}/{inboxpage}/{taskId}`, with the inbox page name in lower case.
  - **Signed out.** With nobody signed in, the task API answers `401`, and
    the widget starts sign-in like any other call. The platform texts
    `shell.tasks.*` hold the column headers, the empty text and the load
    error.
- **Task page**. The address `/{site}/{inboxpage}/{taskId}`
  shows one task, under the page of its task inbox.
  - It loads the texts of the task's `releaseId` with
    `GET /api/sites/{path}/texts/{locale}?release={releaseId}`, for the
    current and the fallback locale. The step, form and outcome labels use
    those texts first, then the site texts and the platform texts. If they
    fail to load, the page shows its load error.
  - It shows the step's label, the subject's label, the due date and the
    form's sections over the subject record. The layout and the field types
    come from the task read's `form`, and the values from the subject
    record, read through the record API.
  - The form follows the form widget's conditions, see
    [form widget conditions](form-widget-conditions.md), through the task's
    [form state route](task-api.md#form-state-of-a-task). The call sends
    `values` and `touched` and no `recordId`. A field the answer hides or
    locks is not sent with the completion.
  - *(Planned for M12.)* The form's steps, with the outcome buttons on the
    last step.
  - It has one button per outcome, with the outcome's label.
  - A click sends the outcome, the changed editable values and the record's
    `version` to the complete endpoint. On success it returns to the inbox.
  - A `403` shows a message that the task is assigned to someone else.
  - A `409` for a decided task shows a message that the task is already
    completed, and loads the task again.
  - A `400` maps its keys as the form widget does: `/values/<field>` under
    that field, and every other key, such as `/outcome`, above the form.
  - A `409` that names keys, such as a stale `/version` or a duplicate
    unique value, is mapped the same way. There is no reload button.
  - A task that is not `open`, when the page loads or after a `409` loads it
    again, shows only the already completed message, with no form or
    outcome buttons. So nobody retries a decision that cannot succeed.
  - With nobody signed in, the task read answers `401`, and the page starts
    sign-in like any other call.
  - Any other failure to load the task or its subject record, or to
    complete it, shows a generic error message.
  - On success the page goes to the inbox page's plain address, without the
    paging the person came from.
  - The `version` is sent even when no field changed. The task API ignores
    it then.
  - The task page has no history panel.
  - The platform texts `shell.task.*` hold the assigned elsewhere and
    already completed messages, and the load and complete errors.
