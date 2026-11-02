# Form conditions

Detailed reference for the conditions and wizard steps of a `form` resource. The resource file shape
and the entity fields are in [resource files](resource-files.md).

## Form conditions and steps

A `form` resource (see [form widget](form-widget.md)) can show, require and lock
fields depending on the record, and can split its sections into wizard steps
([D62](../decisions/d62-conditional-form-states-validation-as-you-type-and-wizard-steps.md)).
The schema, the compile checks, the compiled model, the page and task metadata and the
evaluation through the [form state endpoint](record-api.md#form-state) and the
[task form state route](task-api.md#form-state-of-a-task) are built.
The server enforces `requiredWhen` on write. On task completion it also refuses a value for a
hidden or `readOnlyWhen` field.

- **Conditions.** A field entry may have `visibleWhen`, `requiredWhen` and
  `readOnlyWhen`. A section may have `visibleWhen`. Each is a boolean
  expression in the [expression language](expressions.md). It reads the
  entity's own fields by name, as a validation does. Aggregates over child
  collections, rules and `now()` are allowed.
- **False by default.** A condition that is `null` or stops with a run-time
  error counts as false. So a field with a `visibleWhen` that fails is hidden.
- **Only listed fields.** A condition reads only fields the form lists. This
  limits what a condition can reveal. A condition over a field the user may
  not read still changes the state the user sees, which can leak one bit of
  that field. Authors keep that in mind.
- **Hidden.** A hidden section hides its fields. A hidden field is shown
  nowhere, is never sent and is not required by `requiredWhen`. An entity field
  marked `required` cannot have `visibleWhen`, because the entity would still
  require a field nobody can fill.
- **Read-only.** `readOnly: true` wins over `readOnlyWhen`. A computed,
  sequence or creator field stays read-only.
- **When evaluated.** The server evaluates the conditions on the record as it
  will be stored, after computed fields are calculated. This is the point
  where validations run. The SPA gets the answers from the
  [form state endpoint](record-api.md#form-state). It has no evaluator of its
  own.
- **On write.** The record API enforces `requiredWhen` only when the write
  names the form in `?form=`. It never enforces `readOnlyWhen`. The task API
  enforces `requiredWhen` on every completion, and it refuses a value for a
  `readOnlyWhen` or hidden field too. See
  [the record API](record-api.md#create-update-and-delete) and
  [the task API](task-api.md#task-api-errors).
- **Steps.** `steps` is an optional list on the form. Each step has a `name`,
  unique in the form ignoring letter case, a `title`
  label and `sections`, a list of section names. When `steps` is present,
  every section has a `name` and is in exactly one step. The steps show in
  the order listed. A step whose sections are all hidden is skipped. Keep to
  about six steps: from seven the steps bar shows only numbers. Studio warns
  above six, and never blocks.
- **Field widths**. A field entry may have `width`, a whole number
  from 1 to 12 in twelfths of its section, 12 when left out, so fields sit in
  columns. See [fields in columns](form-widget.md).
- **Compile checks.** Each condition is type-checked as a boolean over the
  form's entity, as an entity validation is, and a problem has the
  expression code at the condition's path, such as `AXC0048` for a condition
  that is not boolean and `AXC0046` for an unknown name. A condition on a
  module's form calls no rules, as a module's data source filter does. The
  new checks have their own codes:
  - `AXC0225`: a condition reads a field the form does not list, at the
    condition. It looks at the fields the condition names, a reference path's
    first field and an aggregate's collection field. It does not look at the
    child row's fields inside an aggregate.
  - `AXC0226`: `visibleWhen` on a field the entity marks `required`, at the
    field's `visibleWhen`.
  - `AXC0227`: a section `name` already used, ignoring letter case, at
    `/sections/{i}/name`.
  - `AXC0360`: a step `name` already used, ignoring letter case, at
    `/steps/{s}/name`. A step without a `name` is `AXC0004`.
  - `AXC0228`: a step names a section the form does not have, at
    `/steps/{s}/sections/{k}`.
  - `AXC0229`: a step names a section that is already in a step, at the same
    path.
  - `AXC0230`: the form has `steps` and a section has no name or is in no
    step, at `/sections/{i}`.

  Each step `title` is a text key and joins the `AXC0028` check, at
  `/steps/{s}/title/textKey`.
- **Not enforced as security.** Conditions on the record API are not a
  security boundary, because a caller that does not name the form skips
  `requiredWhen`. A rule that must always hold belongs in an entity
  [validation](resource-files.md#entity-logic).
- **Left out.** Conditions on the cells of a child collection.

A service desk ticket needs a resolution only when it is resolved. The
`TicketUpdate` form below lists `status` and `resolution` of the `Ticket`
entity of the service desk sample:

```json
{
  "id": "6c1d7e52-0a4b-4f83-9d2e-3b5a7c9e1f40",
  "kind": "form",
  "name": "TicketUpdate",
  "formatVersion": 1,
  "entity": "Ticket",
  "sections": [
    {
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

1. An agent opens a ticket with the status `assigned`. The form state
   endpoint answers that `resolution` is not visible and not required. The
   form does not show it.
2. The agent chooses `resolved`. The form calls the endpoint at once, and the
   answer says that `resolution` is visible and required.
3. The agent saves with `resolution` empty. The write names the form in
   `?form=TicketUpdate`, and the answer is a `400` keyed `/values/resolution`
   with "Required.".
4. The agent switches the status back to `assigned`. The answer hides
   `resolution` again, and the form does not send it.

A wizard names its sections and lists them in `steps`. This form has two steps:

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
