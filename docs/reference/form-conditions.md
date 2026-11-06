# Form conditions

This page describes the conditions and wizard steps of a `form` resource: how a form shows, requires
and locks fields depending on the record, and splits its sections into steps. The rest of the form
resource is in [form widget](form-widget.md), how the SPA follows the conditions in
[form widget conditions](form-widget-conditions.md), and the resource file shape and the entity fields
in [resource files](resource-files.md).

## Form conditions and steps

A form's conditions are expressions that the server evaluates through the
[form state endpoint](record-api.md#form-state) and the
[task form state route](task-api.md#form-state-of-a-task), so the SPA has no evaluator of its own. The
server enforces `requiredWhen` on write, and on task completion it also refuses a value for a hidden or
`readOnlyWhen` field.

### Conditions

- **Where they go.** A field entry may have `visibleWhen`, `requiredWhen` and `readOnlyWhen`, and a
  section may have `visibleWhen`. Conditions apply to field entries and sections, never to the cells
  of a child collection.
- **What they read.** Each condition is a boolean expression in the
  [expression language](expressions.md). It reads the entity's own fields by name, as a validation
  does, and aggregates over child collections, rules and `now()` are allowed.
- **False by default.** A condition that is `null` or stops with a run-time error counts as false, so
  a field with a `visibleWhen` that fails is hidden.
- **Only listed fields.** A condition reads only fields the form lists, which limits what a condition
  can reveal. A condition over a field the user may not read still changes the state the user sees,
  and can so leak one bit of that field, which an author keeps in mind.
- **Hidden.** A hidden section hides its fields. A hidden field is shown nowhere, is never sent and is
  not required by `requiredWhen`. An entity field marked `required` cannot have `visibleWhen`, because
  the entity would still require a field nobody can fill.
- **Read-only.** `readOnly: true` wins over `readOnlyWhen`. A computed, sequence, creator or matched
  reference field stays read-only.
- **When evaluated.** The server evaluates the conditions on the record as it will be stored, after
  computed fields are calculated, which is the point where validations run.
- **On write.** The record API enforces `requiredWhen` only when the write names the form in `?form=`,
  and never enforces `readOnlyWhen`. The task API enforces `requiredWhen` on every completion, and it
  refuses a value for a `readOnlyWhen` or hidden field too. See
  [the record API](record-api.md#create-update-and-delete) and
  [the task API](task-api.md#task-api-errors).
- **Not a security boundary.** Conditions on the record API are not a security boundary, because a
  caller that does not name the form skips `requiredWhen`. A rule that must always hold belongs in an
  entity [validation](resource-files.md#entity-logic).

### Steps

- **Shape.** `steps` is an optional list on the form. Each step has a `name`, unique in the form
  ignoring letter case, a `title` label and `sections`, a list of section names. A
  [customization layer](customization-layers.md#list-keys) finds a step by its `name`.
- **Every section in one step.** When `steps` is present, every section is in exactly one step. The
  steps show in the order listed, and a step whose sections are all hidden is skipped.
- **About six steps.** Keep to about six steps, because from seven the steps bar shows only numbers.
  Studio warns above six, and never blocks.
- **Field widths.** A field entry may have `width`, a whole number from 1 to 12 in twelfths of its
  section, 12 when left out, so fields sit in columns, see
  [fields in columns](form-widget.md#fields-in-columns).

### Compile checks

Each condition is type-checked as a boolean over the form's entity, as an entity validation is, and a
problem has the expression code at the condition's path, such as `AXC0048` for a condition that is not
boolean and `AXC0046` for an unknown name. A condition on a module's form calls no rules, as a module's
data source filter does. The conditions and steps have these codes of their own:

| Diagnostic | Problem                                                                                                                                                                                                                 | Path                      |
| ---------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------- |
| `AXC0225`  | A condition reads a field the form does not list. It looks at the fields the condition names, a reference path's first field and an aggregate's collection field, and not at the child row's fields inside an aggregate | The condition             |
| `AXC0226`  | `visibleWhen` on a field the entity marks `required`                                                                                                                                                                    | The field's `visibleWhen` |
| `AXC0227`  | A section `name` already used, ignoring letter case                                                                                                                                                                     | `/sections/{i}/name`      |
| `AXC0360`  | A step `name` already used, ignoring letter case. A step without a `name` is `AXC0004`                                                                                                                                  | `/steps/{s}/name`         |
| `AXC0228`  | A step names a section the form does not have                                                                                                                                                                           | `/steps/{s}/sections/{k}` |
| `AXC0229`  | A step names a section that is already in a step                                                                                                                                                                        | `/steps/{s}/sections/{k}` |
| `AXC0230`  | The form has `steps` and a section is in no step                                                                                                                                                                        | `/sections/{i}`           |

Each step `title` is a text key and joins the `AXC0028` check, at `/steps/{s}/title/textKey`.

### Example: a condition

A service desk ticket needs a resolution only when it is resolved. The `TicketUpdate` form below lists
`status` and `resolution` of the `Ticket` entity of the service desk sample:

```json
{
  "id": "6c1d7e52-0a4b-4f83-9d2e-3b5a7c9e1f40",
  "kind": "form",
  "name": "TicketUpdate",
  "formatVersion": 1,
  "entity": "Ticket",
  "sections": [
    {
      "name": "update",
      "title": { "textKey": "ticket.sections.update" },
      "fields": [
        { "field": "status" },
        {
          "field": "resolution",
          "visibleWhen": "status == \"resolved\"",
          "requiredWhen": "status == \"resolved\""
        }
      ]
    }
  ]
}
```

1. An agent opens a ticket with the status `assigned`. The form state endpoint answers that
   `resolution` is not visible and not required, and the form does not show it.
2. The agent chooses `resolved`. The form calls the endpoint at once, and the answer says that
   `resolution` is visible and required.
3. The agent saves with `resolution` empty. The write names the form in `?form=TicketUpdate`, and the
   answer is a `400` keyed `/values/resolution` with "Required.".
4. The agent switches the status back to `assigned`. The answer hides `resolution` again, and the form
   does not send it.

### Example: steps

A wizard lists its sections by name in `steps`. This form has two steps:

```json
{
  "id": "2f8b4d60-7e1c-4a95-b3d6-8c0e2a4f6b71",
  "kind": "form",
  "name": "TicketWizard",
  "formatVersion": 1,
  "entity": "Ticket",
  "sections": [
    {
      "name": "problem",
      "title": { "textKey": "ticket.sections.problem" },
      "fields": [{ "field": "title" }, { "field": "description" }]
    },
    {
      "name": "triage",
      "title": { "textKey": "ticket.sections.triage" },
      "fields": [{ "field": "category" }, { "field": "priority" }]
    }
  ],
  "steps": [
    { "name": "step1", "title": { "textKey": "ticket.steps.problem" }, "sections": ["problem"] },
    { "name": "step2", "title": { "textKey": "ticket.steps.triage" }, "sections": ["triage"] }
  ]
}
```
