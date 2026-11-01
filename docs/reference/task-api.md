# Task API

Detailed reference for the task API: the routes that list a user's tasks, read one task and
complete it, and their errors. The task states are in [processes.md](process-states.md#task-states).
Claim, release and reassign are in [task-deadlines.md](task-deadlines.md#claim-and-release), as are
overdue tasks, escalations and deadlines.
The conventions of [architecture.md](../architecture.md) apply: sections marked
_(planned for Mx)_ are not built yet, and Dn refers to [decisions.md](../decisions.md).

The task API lets the signed-in user list their open tasks, read one task and
complete it with an outcome. The user is the user of the
[session](security.md#sign-in-and-the-session)
([D23](../decisions/d23-sign-in-through-oidc-and-the-bff-session.md)).

**Who may act.** The user may act on a task when they are its assignee, or
when the task's assignee is a role and the user holds it. The user's roles
are the roles kept in their session. A role counts only when the active
release declares it, so a task assigned to a role the active release no
longer declares can be listed, read or completed by nobody. This is checked on every request, at
the moment of action. Policies add to this check and never replace it. A
claimed role task is listed and may be completed only by its claimer, see
[Claim and release](task-deadlines.md#claim-and-release).

**Policies on tasks.** These follow
[D24](../decisions/d24-roles-and-policies.md).

- The assignee check is enough to list, read and complete a task. The user
  needs no grant on the subject entity, and its read filters do not apply.
- The task's `form` and its `entity` leave out the fields the user may not
  read on the subject entity. A user with no `read` grant on it reads no
  field. A form section left with no field is left out too.
- Which fields the user may read comes from the policies of the active
  release, so a grant change applies to open tasks at once. The form's
  layout still comes from the task's release. Fields are matched by name.
- A value for a field the user may not read is refused as a field the form
  does not make editable, with "Cannot be set.".
- Field write grants do not apply to the values of a task completion. The
  form decides which fields are editable.
- `subject.label` uses the subject entity's display field from the task's
  release. Readability still comes from the active
  release's policies. The label is `null` when the user may not read that
  field, as in the record API's [label rule](record-api.md#record-shape).

**List my tasks.** `GET /api/apps/{app}/tasks?page=&pageSize=` lists the
`open` tasks the user may act on, whatever release their instance is pinned
to. `page` and `pageSize` follow the record API's
[paging rules](record-api.md#paging-and-sorting). There is no `sort` or
`search`. Tasks are ordered by `dueAt` ascending with no due date last, then
by `createdAt`, then by `id`.

```json
{
  "items": [
    {
      "id": "0192f4b1-7a2c-7d3e-8f40-5b6c7d8e9f01",
      "releaseId": "0192f49a-1b2c-7d3e-8f40-5b6c7d8e9f01",
      "process": "PurchaseRequestApproval",
      "step": "managerApproval",
      "labelKey": "purchaseRequest.managerApproval",
      "subject": {
        "entity": "PurchaseRequest",
        "id": "0192f4a7-3b1c-7e2d-9a4f-1c2d3e4f5a6b",
        "label": "PR-2026-00042"
      },
      "claimedBy": null,
      "dueAt": "2026-10-13T02:05:00Z",
      "overdue": false,
      "createdAt": "2026-10-10T02:05:00.123456Z"
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1
}
```

- `releaseId` names the task's release. The inbox loads
  the texts of each release its rows name, so each step label uses its own
  release's texts.
- `subject.label` is the value of the display field from the task's release.
  It is `null` when the active release's policies do not
  let the user read that field.
- `claimedBy` is the user id of the holder who claimed the role task, or
  `null`. A claimed task is listed for its claimer only.
- `dueAt` is `null` when the step has no `dueIn`.
- `overdue` is `true` when `dueAt` has passed. It is worked out when read with
  database time. See [task-deadlines.md](task-deadlines.md#task-states).
- **Speed.** The list answers within 300 ms for a holder of a role with 1,000
  open role tasks, 300 of them claimed by others, and its count reads through
  the task index. This is the Reads objective of
  [performance](performance.md#objectives). `TaskInboxPerformanceTests` fails
  when the best of three calls takes 300 ms or more.

**Read one task.** `GET /api/apps/{app}/tasks/{id}` returns one task in any
state:

```json
{
  "id": "0192f4b1-7a2c-7d3e-8f40-5b6c7d8e9f01",
  "releaseId": "0192f49a-1b2c-7d3e-8f40-5b6c7d8e9f01",
  "process": "PurchaseRequestApproval",
  "instanceId": "0192f4a9-2d3e-7f40-8a1b-2c3d4e5f6a7b",
  "step": "managerApproval",
  "labelKey": "purchaseRequest.managerApproval",
  "state": "open",
  "assignee": { "user": "maria", "role": null },
  "claimedBy": null,
  "subject": {
    "entity": "PurchaseRequest",
    "id": "0192f4a7-3b1c-7e2d-9a4f-1c2d3e4f5a6b",
    "label": "PR-2026-00042"
  },
  "form": {
    "name": "PurchaseRequestReview",
    "sections": [
      {
        "name": null,
        "titleKey": "purchaseRequest.sections.decision",
        "visibleWhen": false,
        "fields": [{ "name": "decisionComments", "readOnly": false, "visibleWhen": false }]
      }
    ],
    "steps": [],
    "entity": { "name": "PurchaseRequest", "fields": [] }
  },
  "outcomes": [
    { "name": "approve", "labelKey": "task.approve" },
    { "name": "return", "labelKey": "task.return" },
    { "name": "reject", "labelKey": "task.reject" }
  ],
  "dueAt": "2026-10-13T02:05:00Z",
  "overdue": false,
  "createdAt": "2026-10-10T02:05:00.123456Z",
  "completedAt": null,
  "completedBy": null,
  "outcome": null,
  "commands": [],
  "reassignRoles": []
}
```

- `releaseId` names the task's release. The task page
  loads its texts with `GET /api/sites/{path}/texts/{locale}?release={releaseId}`.
- `subject.label` uses the display field from the task's release. The active
  release's policies decide whether the user
  may read it. Otherwise the label is `null`.
- `assignee` has the user id or the role name, and the other is `null`.
- `claimedBy` is the user id of the holder who claimed the role task, or
  `null`. Other holders can still read a claimed task.
- `form` is the step's form in the task's release, so a task pinned to an
  earlier release keeps its own layout. It has the shape of a form widget's
  `form` metadata, plus `entity` in the shape of a widget's `entity`
  metadata, so the task page knows each field's type. See
  [form widget](form-widget.md) and [frontend](frontend.md). The example shortens both.
  The `form` carries the same additions as the form metadata in
  [form widget](form-widget.md): `visibleWhen` as a yes or no flag on each field
  and section, each section's `name` (`null` when it has none), and `steps`
  (`[]` when the form has none). Sections the user cannot read, and the steps
  left empty by that, are left out. The condition expressions stay on the
  server. See
  [Form conditions and steps](form-conditions.md#form-conditions-and-steps).
- `completedAt`, `completedBy` and `outcome` are `null` while the task is
  `open`.
- `overdue` is `true` only for an open task whose `dueAt` has passed. A
  completed, cancelled or expired task reads `false`.
- `commands` lists what the user may do with the task now, from `claim`,
  `release` and `reassign`, in that order. It is empty unless the task is
  `open`. The claim, release, reassign and complete responses carry it too.
- `reassignRoles` holds the `name` and `labelKey` of each role of the active
  release, and is empty unless `commands` has `reassign`. See
  [task-deadlines.md](task-deadlines.md#inbox-and-task-page).

**Complete a task.** `POST /api/apps/{app}/tasks/{id}/complete` records the
decision:

```http
POST /api/apps/{app}/tasks/{id}/complete
Content-Type: application/json

{ "outcome": "approve", "values": { "decisionComments": "Fine for Q4." }, "version": 3 }
```

- `outcome` is required. It names one of the step's outcomes, ignoring
  letter case.
- `values` is optional. It holds only fields the task's form makes editable
  and the user may read, in the record API's
  [value format](record-api.md#request-bodies-and-values).
- `version` is the subject record's version, as the record API reads it. It
  is required when `values` is not empty.
- The response is `200` with the completed task, in the shape of a single
  read.

## Form state of a task

`POST /api/apps/{app}/tasks/{id}/form-state` answers
what the task's form shows for a draft, as the record API's
[form state](record-api.md#form-state) does for a record form
([D62](../decisions/d62-conditional-form-states-validation-as-you-type-and-wizard-steps.md)).
It needs `Content-Type: application/json` and the
[CSRF header](record-api.md#create-update-and-delete).

```json
{ "values": { "decisionComments": "Fine for Q4." }, "touched": ["decisionComments"] }
```

- The body has `values` and `touched`, with the shapes of the record API.
  There is no `recordId`, because the subject record is the task's.
- The response has `sections`, `fields`, `errors` and `defaults`, as in the
  record API. `defaults` is always `{}`, because the subject record exists.
- It uses the form from the task's release and the subject record. Fields the
  user may not read are left out of `fields`.
- It writes nothing, and runs no unique or reference-existence check.
- A subject record that no longer exists is a `404` with the title "No record
  has this id.".
- The checks follow the task check order below: `401`, `404`, `403`, then
  `409` when the task is not `open`.

## Task API errors

Errors are problem details, as in the [record API](record-api.md#errors).
The titles are fixed and never contain text from the request. The content
type follows the record API's
[rules](record-api.md#create-update-and-delete), so a wrong one is a `415`.

A request is checked in this order, and the first failure is the response:
`401`, then `404`, then `403`, then `409`, then `415`, then `400`. So a user
who may not act on a task never learns its state. A completion or a form
state call without the [CSRF header](record-api.md#create-update-and-delete)
is a `403` before any of these checks.

- **`401`.** Nobody is signed in.
  Every task route needs a signed-in user.

  ```json
  {
    "type": "https://tools.ietf.org/html/rfc9110#section-15.5.2",
    "title": "Sign in to read or write this data.",
    "status": 401
  }
  ```

- **`404`.** An unknown application, an application with no active release,
  an unknown task, or an `{id}` that is not a UUID in the hyphenated form. A
  task of another application is an unknown task. For an unknown application,
  or one with no active release, the title is "No application is active under
  this name." instead of the one below.

  ```json
  {
    "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
    "title": "No task exists with this id.",
    "status": 404
  }
  ```
- **`403`.** The user is not the assignee and does not hold the assignee
  role, or the active release no longer declares that role. It is also the answer when a role holder completes a role task that another holder claimed. This applies to reading one task, asking for its form state and completing it.

  ```json
  {
    "type": "https://tools.ietf.org/html/rfc9110#section-15.5.4",
    "title": "This task is assigned to someone else.",
    "status": 403
  }
  ```

- **`409` for a decided task.** The task is not `open`, because another
  completion came first. This covers a later completion and the loser of two
  concurrent ones. It covers a `cancelled` task and an `expired` task too, see
  [Task states](task-deadlines.md#task-states). A task that is not `open` is
  not listed.

  ```json
  {
    "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
    "title": "This task is already completed.",
    "status": 409
  }
  ```

- **`400`.** The body is a validation problem keyed by JSON Pointer. Every
  invalid part is reported in the same response.
  - A missing outcome, or one the step does not have, is keyed `/outcome`.
  - A value for a field the form does not make editable, or the user may
    not read, is keyed `/values/<field>` with the message "Cannot be set.".
  - A missing `version` when `values` is not empty is keyed `/version`.
  - A visible field of the task's form whose `requiredWhen` is true and that
    is left `null` is keyed `/values/<field>` with "Required.". It is
    evaluated on the record as it will be stored, whether or not the body
    sends `values`: without `values` it reads the stored record. Nothing is
    written and the task stays open. A hidden field is not required.
  - A value for a field whose `readOnlyWhen` is true, or that is hidden, is
    keyed `/values/<field>` with "Cannot be set.". A field is hidden when its
    own `visibleWhen` or its section's is false. It is evaluated on the record
    as it will be stored, so a value in the same body can lock or hide another
    field. Nothing is written and the task stays open. The same value is
    accepted when the condition is false.
  - Any other property, a repeated property and a body that is not a JSON
    object get the record API's keys and messages, such as "Unknown
    property.".
  - The values are then checked by the record update command. Its parse
    errors and entity validations use the record API's keys and messages.

  ```json
  {
    "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
    "title": "One or more validation errors occurred.",
    "status": 400,
    "errors": {
      "/outcome": ["Must be one of the step's outcomes."],
      "/values/totalAmount": ["Cannot be set."]
    }
  }
  ```

- **`409` for a stale version.** The subject record's `version` is not the
  stored one. It is keyed `/version`. It is found when the values are
  written, so after the body checks, as in the record API.

  ```json
  {
    "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
    "title": "The record has changed since this version was read.",
    "status": 409,
    "errors": {
      "/version": ["Must be the record's current version."]
    }
  }
  ```

- **Other write failures.** The other storage outcomes of writing the values
  answer as in the record API's [errors](record-api.md#errors), such as a
  `409` for a unique value, or a `404` when the subject record no longer
  exists.
- **`503` for a changed release.** The application's active release changed on
  each of three attempts, found under the [start lock](processes.md#releases-and-running-work).
  The server already ran the completion again on the new release each time.
  Nothing was written, and the client may send the request again. It is not in
  the check order, because it can follow any check that passed.

  ```json
  {
    "type": "https://tools.ietf.org/html/rfc9110#section-15.6.4",
    "title": "The application's active release changed while the request ran. Try again.",
    "status": 503
  }
  ```

- **`503` for a write timeout.** The completion's transaction ran past
  `Data:WriteTimeout` and PostgreSQL cancelled it. Nothing was written, and the
  client may send the request again. The title is fixed: "The write took too
  long. Try again." See [data settings](host-settings.md#data-settings).

## Completing a task

One transaction records the decision:

1. It locks the task row and checks that the task is still `open`.
2. It writes `values`, when there are any, through the record update command,
   so computed fields are recomputed and validations run.
3. It sets the task's `outcome`, `completedBy` and `completedAt`, and its
   state to `completed`.
4. It sets the instance to `running` on the outcome's `next` step, with a
   new revision. The instance must be `waiting` on the task's step.
5. It writes the history row of the task step at the revision the instance
   waited at. Its `decision` is the outcome's declared name. Its input is
   `{ "subjectId", "subjectVersion" }`, with the version the values were
   written over, or `null` when there are no values. Its output is
   `{ "taskId", "outcome", "next" }`, plus `subjectVersion`, the version the
   values wrote, when there are any.
6. It writes the audit record `task.completed`, with the user as its
   actor, and details `{ "outcome", "fields" }`.
   `fields` names the changed fields in declaration order and holds no
   values. A completion writes no `record.updated`.
7. It deletes the task's pending `task.escalate` and `task.deadline` work items, see
   [Escalations and deadline](task-deadlines.md#escalations-and-deadline).
8. It inserts the work item for the outcome's `next` step.

The form, the outcomes and the record write all use the release the instance
is pinned to. When any part fails, nothing is written. The worker then runs
the next step as usual.
