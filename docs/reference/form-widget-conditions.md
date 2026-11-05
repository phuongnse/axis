# Form widget conditions and steps

This page describes how the form widget and the task page follow the server's form states: which
fields to show, require and lock, which messages to show, and how a form with `steps` runs as a wizard.
The widget has no evaluator of its own, so a condition reads the same on the server and in the
browser. The conditions themselves are in [form conditions](form-conditions.md), and the rest of the
form widget in [form widget](form-widget.md).

## Conditions

The widget asks the [form state endpoint](record-api.md#form-state) for the states of the fields as the
person edits, and shows its answer.

- **Calls.** The widget calls the endpoint with the draft values and the touched fields.
  - The call goes out at once after a choice: a select, a checkbox, a date, or a picked or cleared
    record. It goes out 300 ms after typing stops in a text or number input, and after a change to the
    rows of a child collection.
  - The first time a field loses focus, the call goes out at once, so an empty required field shows
    "Required.".
  - The widget also calls once when a new form opens, and again each time a record is loaded or loaded
    again, with no touched field, so that a field whose `visibleWhen` is false is not shown before the
    first edit.
  - The SPA builds the URL from the entity's `recordsPath`, as `/api/apps/{app}/forms/{form}/state`,
    because the form metadata has no state path.
  - A widget that names an `entity` makes no call. Neither does a widget whose user has no `create`
    grant for a new record or no `update` grant for a saved one, because the endpoint answers `403` and
    every field is read-only.
- **Pending answers.** While an answer is pending, the widget keeps the last answered states, and it
  drops an answer that a newer call has overtaken. A call that fails, by a network error or any answer
  but `200`, keeps the last states and logs a warning. The widget shows nothing, because the server
  checks the save again.
- **States.** Following the answer, a field is hidden, marked required or made read-only. A hidden or
  read-only field is not sent, in the save and in the `values` of the next call. A section whose fields
  are all hidden loses its title.
- **Messages.** The messages in `errors` show under the touched fields, inside a `role="alert"`
  element, and a message from a save wins over the one from the answer. Only messages that name a
  top-level field show while the user types. A message for a row cell, such as
  `/values/lines/1/quantity`, shows after a save, as it does without the answer.
- **Touched.** A field becomes touched when it loses focus or changes.
- **Defaults.** For a new record the widget fills each empty field that the user has not touched from
  the `defaults` of an answer, as the form's starting value. A value that the user typed or cleared is
  kept. The widget does not send an unchanged default, so the save applies it, and clearing a default
  sends `null`. A saved record and the task page ignore `defaults`.
- **Save.** The widget always sends `?form=` with the form's name on create, on update and on the save
  before a `startProcess` action, so the server enforces `requiredWhen`. A widget that names an
  `entity` sends no `?form=`.
- **Task page.** The task page follows the conditions through the
  [task form state route](task-api.md#form-state-of-a-task), with no `recordId` and no `?form=`.

## Steps

A form with `steps` shows one step at a time, so a long form is filled in parts, and a save still sends
the changes of every step.

- **Moving.** The person moves only with Back and Next. Next sends every visible field of the step
  that the form does not lock as touched, in one call at once, and stays on the step when any key of
  `errors` names one of them. Back never checks. A step whose sections are all hidden is skipped and
  left out of the bar. Moving between steps keeps every typed value, and a save sends the changes from
  all steps.
- **Failed check.** When the call for Next fails, by a network error or any answer but `200`, the form
  logs a warning and moves on to the next step, because the server checks the save again.
- **Focus.** After Next or Back, focus moves to the first input of the new step, so a keyboard user
  types on at once. Nothing is focused when the form first opens. Enter on a step before the last acts
  as Next.
- **Last step.** Save and the `startProcess` actions are on the last step. Cancel and the `navigate`
  actions are on every step, because a navigate action uses the record as loaded and not the draft.
  After a failed save, the form opens the first step that has an error. The current step stays out of
  the URL, because a draft does not survive a reload.
- **Task page.** The task page shows the steps of the task's form the same way. The outcome buttons
  are on the last step, and Enter on the last step does nothing.

## Steps bar

A bar over the form shows the step titles and marks the current step. It is not clickable.

- **Width.** Each step is as wide as its title and is joined to the next by a short line, so the bar
  sits on the left and never stretches across the form. The bar is never wider than the form, and
  when the steps do not fit it wraps between steps.
- **Titles.** A long title is cut with an ellipsis and shows whole on hover. The current step always
  keeps its title and is marked with `aria-current="step"`.
- **Finished steps.** A finished step shows a check mark in place of its number.
- **Seven steps or more.** Only the current step shows its title, and the others show their number,
  or a check mark when finished. A number shows its step's title on hover and on focus, and to
  assistive technology.
