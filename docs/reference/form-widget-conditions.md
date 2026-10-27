# Form widget conditions and steps

Detailed reference for how the form widget follows the server's form states. The conventions of
[architecture.md](../architecture.md) apply: sections marked _(planned for Mx)_ are not built yet,
and Dn refers to [decisions.md](../decisions.md). The rest of the frontend, including the form
widget, is in [form-widget.md](form-widget.md) and [frontend.md](frontend.md).

- **Conditions and steps**. The widget asks the server which fields to
  show, require and lock, and which messages to show. It has no evaluator of
  its own
  ([D62](../decisions/d62-conditional-form-states-validation-as-you-type-and-wizard-steps.md)).
  The conditions and the steps are built.
  - **Calls.** It calls the [form state endpoint](record-api.md#form-state)
    with the draft values and the touched fields. The call goes out at once
    after a choice: a select, a checkbox, a date or a picked or cleared
    record. It goes out 300 ms after typing stops in a text or number input
    and after a change to the rows of a child collection. The first time a
    field loses focus, the call goes out at once, so an empty required field
    shows "Required.". The widget also calls once when a new form opens, and
    again each time a record is loaded or loaded again, with no touched
    field, so that a field whose `visibleWhen` is false is not shown before
    the first edit. The SPA builds the URL from the entity's `recordsPath`,
    as `/api/apps/{app}/forms/{form}/state`, because the form metadata has
    no state path. A widget that names an `entity` makes no call. Neither
    does a widget whose user has no `create` grant for a new record or no
    `update` grant for a saved one, because the endpoint answers `403` and
    every field is read-only.
  - **Pending answers.** While an answer is pending, the widget keeps the
    last answered states. It drops an answer that a newer call has
    overtaken. A call that fails, by a network error or any answer but
    `200`, keeps the last states and logs a warning. The widget shows
    nothing, because the server checks the save again.
  - **States.** Per the answer, a field is hidden, marked required or made
    read-only. A hidden or read-only field is not sent, in the save and in
    the `values` of the next call. A section whose fields are all hidden
    loses its title. The messages in `errors` show under the touched
    fields, inside a `role="alert"` element. A message from a save wins over
    the one from the answer. Only messages that name a top-level field show
    while the user types. A message for a row cell, such as
    `/values/lines/1/quantity`, shows after a save, as it does without the
    answer.
  - **Defaults.** For a new record the widget fills each empty field that the
    user has not touched from the `defaults` of an answer, as the form's
    starting value. A value that the user typed or cleared is kept. The widget
    does not send an unchanged default, so the save applies it, and clearing
    a default sends `null`. A saved record and the task page ignore
    `defaults`. See
    [D77](../decisions/d77-indexes-defaults-and-ranges.md).
  - **Touched.** A field becomes touched when it loses focus or changes.
  - **Save.** The widget always sends `?form=` with the form's name on
    create, on update and on the save before a `startProcess` action, so
    the server enforces `requiredWhen`. A widget that names an `entity`
    sends no `?form=`. The task page follows the conditions through the
    [task form state route](task-api.md#form-state-of-a-task), with no
    `recordId` and no `?form=`.
  - **Steps.** A form with `steps` shows one step at a time, under a
    progress bar of the step titles that marks the current step. The bar is
    not clickable, so the user moves only with Back and Next. Next sends
    every visible field of the step that the form does not lock as touched,
    in one call at once, and stays on the step when any key of `errors`
    names one of them. Back never checks. A step whose sections are all
    hidden is skipped and left out of the bar. Moving between steps keeps
    every typed value, and a save sends the changes from all steps. A form
    without `steps` looks as before.
  - **Steps bar** _(planned)_. Each step is as wide as its title and is
    joined to the next by a 48px line, so the bar sits on the left and never
    stretches across the form. A title is cut with an ellipsis at 160px and
    shows whole on hover. The current step always keeps its title. With seven
    steps or more, only the current step shows its title and the others show
    their number. A number shows its step's title on hover and on focus, and
    to assistive technology. The bar is never wider than the form
    ([D96](../decisions/d96-rules-from-the-design-system-review.md)).
  - **Failed check.** When the call for Next fails, by a network error or
    any answer but `200`, the form logs a warning and moves on to the next
    step, because the server checks the save again.
  - **Focus.** After Next or Back, focus moves to the first input of the new
    step, so a keyboard user types on at once. Nothing is focused when the
    form first opens. Enter on a step before the last acts as Next.
  - **Last step.** Save and the `startProcess` actions are on the last
    step. Cancel and the `navigate` actions are on every step, because a
    navigate action uses the record as loaded and not the draft. After a
    failed save, the form opens the first step that has an error. The
    current step stays out of the URL, because a draft does not survive a
    reload.
  - **Task page.** The task page shows the steps of the task's form the same
    way. The outcome buttons are on the last step, and Enter on the last
    step does nothing.
