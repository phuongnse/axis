# Process inspector widget

Detailed reference for the process inspector widget of the SPA. The conventions of
[architecture.md](../architecture.md) apply: sections marked _(planned for Mx)_ are not built yet,
and Dn refers to [decisions.md](../decisions.md). The rest of the frontend is in
[frontend.md](frontend.md).

- **Process inspector widget**. A page whose widget is a
  `processInspector` names one process. It lists the process's instances and
  opens one, through the [inspection API](process-inspection.md#process-inspection).

  ```json
  { "type": "processInspector", "process": "PurchaseRequestApproval" }
  ```

  - **Compile.** The `process` must name a loaded process, otherwise it is
    `AXC0192` at `/widgets/{i}/process`. The widget names none of `entity`,
    `dataSource` and `form` (`AXC0060` at that property).
  - **Metadata.** The widget has `"entity": null`, `"dataSource": null`,
    `"form": null` and `"tasksPath": null`. It adds
    `process`, an `instancesPath`, such as
    `"/api/apps/PurchasingApp/processes/PurchaseRequestApproval/instances"`,
    the list endpoint, and a `deadLettersPath`, such as
    `"/api/apps/PurchasingApp/processes/PurchaseRequestApproval/dead-letters"`,
    the dead letter list endpoint. The SPA never builds a path itself. It uses `process` to link the record history to the page, see
    **History** under the [form widget](form-widget.md). The widget has `"access": null` and
    `"actions": []`. The page endpoint answers `403` to a user without an
    `inspect` grant on the process, and site navigation leaves the page out.
  - **List.** The page lists the instances with their state, step, subject id,
    release and times, until the columns below are built. Rows keep the API's order, newest first. A clearable
    select above the list filters by state, and offers the five states with
    platform texts. `state`, `page` and `pageSize` live in the URL, as for a
    table. Changing the state goes back to page 1. A `state` in the URL that is
    not one of the five is dropped. The step column shows the step's name,
    because a list row carries no label key. Each row has an Open link.
  - **List columns** _(planned)_. The first column is the instance's record,
    named by `subjectLabel`, and is the link that opens the instance, or reads
    `shell.inspector.open` when `subjectLabel` is `null`. There is no Open
    column and no record id. The Release column shows `releaseLabel`, the
    release's number such as "Release 3", never its id. An instance still
    running shows a muted "—" under Ended
    ([D96](../decisions/d96-rules-from-the-design-system-review.md)). A
    process without a subject has no record to name its instances, so each
    instance gets a number within its process, such as #1042. That is agreed,
    and its design is not decided yet.
  - **Instance page.** A row opens `/{site}/{page}/{instanceId}`, with the
    page name in lower case. `new` and an id in the wrong form show the
    not-found page, and the instance is not requested. The page reads the
    instance detail and shows the steps as a list in graph order, each with
    its type and its transitions, with no diagram. Steps the instance has run
    are marked as taken, and the step it is at is marked as current, whatever
    the instance state. Below it are the history rows with the step, the
    times, the decision and the error, then the tasks, then the calls with
    their attempts. A call whose `inputHidden` is `true` shows "hidden" in
    place of its input. A shown input is a list of its top-level properties,
    where a number keeps its source digits.
  - **Commands.** The page has a Cancel instance and a Retry button for each
    of those commands that the instance detail's `commands` lists. With none
    listed, it has no button, and a command name this build does not know is
    left out. A button opens a dialog that asks for a reason. The reason is
    required, must not be blank and has at most 500 characters, and Send is
    blocked until it is valid. On success, the dialog closes and the page reads
    the instance detail again, so it shows the new state, tasks, calls and
    `commands`. On a `400`, `403`, `404` or `409`, the dialog shows the
    problem's title. Any other failure shows a generic text, and a `401`
    starts sign-in. The Retry dialog first warns when a call on the instance's
    current step has `failed`, and that call is a package call whose
    `idempotent` is not `true`. A failed connector call gets no warning. The
    server still checks every command, so the page never decides who may act.
  - **Resolve.** A call whose `resolvable` is `true` has a Resolve button,
    whether it is a connector or a package call. A call that is not resolvable
    has none, because the server decides what may be resolved and by whom. The
    button opens a dialog that asks for a reason, with the rules of the other
    commands, and an outcome, `succeeded` or `failed`. The outcome has no
    default, and Send is blocked until one is chosen. For `succeeded`, the
    dialog shows one input for each declared output of the call, labelled with
    its name and type, with the required ones marked and checked before Send.
    A `boolean` output is a true/false choice, and every other type is text,
    so a date or date-time is typed as the API takes it. The values go out
    typed: an integer or decimal keeps every digit, a boolean is `true` or
    `false`, and an empty optional output is left out. A `failed` outcome sends
    no output. On success, the dialog closes and the page reads the instance
    again. On a `400`, the message of each `/output/{name}` error shows under
    that output's field, and the problem's title shows in the dialog. Any other
    refusal shows its title, and any other failure shows a generic text.
  - **Dead letters.** Under the instance list, a Dead letters section lists the
    process's open dead letters from `{deadLettersPath}?state=open`, newest
    first, with the kind, the attempts, the last error and the time it was
    dead-lettered. The kind shows as the API sends it. The list has its own page
    number, which the URL does not hold, because the URL's `page` and `pageSize`
    belong to the instance list. With none open, it shows an empty text. A row
    has a Redeliver and a Discard button for each of those commands that the
    item's `commands` lists, which is an open dead letter and a user with the
    `retry` grant. A user with only `inspect` sees no button, and a command name
    this build does not know is left out. A button opens the same reason dialog
    as Cancel and Retry, with the same rules for the reason, and posts it to
    `{deadLettersPath}/{id}/redeliver` or `…/discard`. _(planned)_ Redeliver,
    with a send icon, and Discard, with a red trash icon, are icon buttons
    with their names as tooltips, and both still open the reason dialog. On
    success, the dialog
    closes and the list is requested again, so a settled dead letter leaves
    it. On a `400`, `403`, `404` or `409`, the dialog shows the problem's title,
    such as the `409` for a trigger that is not in the active release. Any other
    failure shows a generic text. If the list cannot be loaded, including a
    `403`, an error shows inside the section and the instance list stays
    usable. The server checks every command, so the widget never decides who
    may act.
  - **Labels.** Step labels come from the texts of the instance's `releaseId`,
    loaded with `GET /api/sites/{path}/texts/{locale}?release={releaseId}`, as
    the inbox does. A step without a label shows its name.
  - **Errors.** With nobody signed in, the API answers `401` and the widget
    starts sign-in like any other call. A `403` shows a message that the user
    may not inspect the process. A `404` on the instance page shows a message
    that the instance does not exist, inside the page. Any other failure shows
    a generic load error. The server checks every request, so the page never
    decides who may inspect.
  - The platform texts `shell.inspector.*` hold the column headers, the
    section titles, the instance states, the "hidden" text, the empty texts
    and the messages. The texts `shell.inspector.deadLetters.*` hold the
    dead letter section's title, column headers, empty text and load error. The
    texts `shell.inspector.commands.*` hold the command buttons, the dialog's
    labels, the reason errors, the retry warning and the resolve labels and
    errors.
- **Timers and sub-processes** _(planned for M14)_. The instance detail shows a
  waiting timer's due time and an instance's sub-processes, and an instance
  without a subject shows "no subject".
