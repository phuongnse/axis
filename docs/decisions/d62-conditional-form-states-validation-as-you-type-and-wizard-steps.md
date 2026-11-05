# D62. Conditional form states, validation as you type and wizard steps

Status: **Agreed**

## Context

A form must show, require and lock fields depending on the record, check the draft while the user
types, and split a long form into steps. The conditions are expressions, and the decimal, date and
time zone rules of the [expression language](d16-expression-language.md) must not drift between two
interpreters.

## Decision

- **Conditions.** A form field may have `visibleWhen`, `requiredWhen` and `readOnlyWhen`, and a
  section `visibleWhen`: boolean expressions over the fields the form lists, where `null` or an error
  counts as false.
- **The server evaluates them.** A form state endpoint takes the draft, answers each field's state
  and the validation messages of the touched fields, and writes nothing. A record form and a task
  form each have one, under their own access checks
  ([D20](d20-human-tasks-task-inbox-and-forms.md), [D24](d24-roles-and-policies.md)). The SPA has no
  evaluator.
- **On write.** The record API enforces `requiredWhen` only when a write names its form in `?form=`,
  and leaves `readOnlyWhen` to the field write grants. The task API enforces both. A rule that must
  always hold is an entity validation ([D17](d17-entity-logic-and-child-collections.md)).
- **Wizard steps.** A form may group its sections into `steps`. Next checks the step through the
  endpoint, Back never checks, saving is on the last step, and the step stays out of the URL.

The rules are in [form conditions](../reference/form-conditions.md#form-conditions-and-steps) and
[form widget conditions](../reference/form-widget-conditions.md), and the endpoints in
[form state](../reference/record-api.md#form-state) and
[form state of a task](../reference/task-api.md#form-state-of-a-task).

## Consequences

- One interpreter evaluates every condition, and each change of state waits for the server.
- A record API caller that does not name the form skips `requiredWhen`, so conditions are not a
  security boundary.

## Alternatives

- **An evaluator in the SPA.** It would answer at once, but drift from the server's.
- **Enforcing `readOnlyWhen` in the record API.** It would add a second guard beside the field write
  grants.
