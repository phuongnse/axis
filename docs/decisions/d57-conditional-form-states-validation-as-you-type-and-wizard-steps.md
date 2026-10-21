# D57. Conditional form states, validation as you type and wizard steps

Status: **Agreed**

A form can show, require and lock fields depending on the record, can check the draft as the
user types, and can split its sections into wizard steps. All of it is _(planned for M12)_. The
server evaluates every condition. NexKit autopilot confirmed these points for the owner on
issue #934 on 2026-10-21. The reference text is in
[Form conditions and steps](../reference/resource-files.md#form-conditions-and-steps), the
[form state endpoint](../reference/record-api.md#form-state) and the
[task form state endpoint](../reference/task-api.md#form-state-of-a-task).

- **Conditions:**
  - A form field may have `visibleWhen`, `requiredWhen` and `readOnlyWhen`. A form section may
    have `visibleWhen`.
  - Each is a boolean expression over the record, in the
    [expression language](d16-expression-language.md). `null` and a run-time error count as
    false.
  - A condition reads only fields the form lists. A field that the entity marks `required`
    cannot have `visibleWhen`.
- **Where they are evaluated:**
  - The server evaluates them, through the form state endpoint. There is no evaluator in the
    SPA in M12.
  - The endpoint takes the draft values and answers each field's state. It writes nothing and
    does not run unique or reference-existence checks.
  - The SPA calls it at once after a choice and 300 ms after typing stops. While an answer is
    pending, it keeps the last answered states and drops answers that a newer call overtook.
  - There are two endpoints. One is for a record form, with entity grants. The other is for a
    task form, with the assignee check ([D20](d20-human-tasks-task-inbox-and-forms.md),
    [D24](d24-roles-and-policies.md)).
- **On write:**
  - The record API enforces `requiredWhen` only when a create or update names the form in
    `?form=`. A hidden field is not required.
  - The record API never enforces `readOnlyWhen`. It follows `readOnly`: field write grants
    stay the guard.
  - The task API enforces both. A value for a hidden field or a `readOnlyWhen` field is
    "Cannot be set.", and a visible `requiredWhen` field left `null` is "Required.".
  - A rule that must always hold belongs in an entity validation
    ([D17](d17-entity-logic-and-child-collections.md)), where a caller cannot skip it.
- **Validation as you type:** the same endpoint runs the entity's validations on the draft and
  returns messages only for the fields the user has touched.
- **Wizard:**
  - A form may declare `steps`, each a list of its named sections. Every section is in exactly
    one step.
  - Next checks the step's fields through the endpoint. Back never checks. Save and the
    `startProcess` actions are on the last step. A task form may have steps too, and its
    outcome buttons are on the last step.
  - The current step stays out of the URL.

_Why:_ evaluating on the server keeps one interpreter, so decimal, date and time zone rules
cannot drift ([D16](d16-expression-language.md)). A local evaluator for a small subset may come
later, if the measured wait in #956 is too long. Naming the form in `?form=` lets the record
API apply a form's rules without guessing which form a write came from. Leaving `readOnlyWhen`
out of the record API keeps field write grants as the only guard there. The cost is that a
caller who does not name the form skips `requiredWhen`.
