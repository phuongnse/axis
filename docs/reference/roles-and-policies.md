# Roles and policies

Detailed reference for the `role` and `policy` resources, the record creator and sensitive
fields. How the server applies them is in [security.md](security.md#authorization).
The conventions of [architecture.md](../architecture.md) apply: sections marked
_(planned for Mx)_ are not built yet, and Dn refers to [decisions.md](../decisions.md).

The design follows
[D24](../decisions/d24-roles-and-policies.md), and
[Authorization](security.md#authorization) describes how the server
applies it. The `role` and `policy` resources, the check of task assignees
and the policy checks are built. So are matching the roles claim and
enforcing every grant. That covers entity grants with filters and fields on
the record API, data source grants with root read filters and field checks,
and process `start` grants. The `endpoint` grant with its `invoke` action and
its compile checks are built too, and the endpoint call enforces it. So is the
`inspect` action on a `process` grant, which the process instance list
and the instance detail enforce, see [Process inspection](process-inspection.md#process-inspection). Each
compile check below is a compile error.

A `role` resource declares a role that users can hold:

```json
{
  "id": "f373f3ea-4e4d-4842-9791-4daf7be47f0a",
  "kind": "role",
  "name": "employee",
  "formatVersion": 1,
  "label": { "textKey": "role.employee" }
}
```

```json
{
  "id": "6fedaeb5-8afd-4fed-9aad-af8dc6b60f58",
  "kind": "role",
  "name": "department-manager",
  "formatVersion": 1,
  "label": { "textKey": "role.departmentManager" }
}
```

```json
{
  "id": "83e56e02-92d0-46d4-a67d-78f6e76468a4",
  "kind": "role",
  "name": "finance-reviewer",
  "formatVersion": 1,
  "label": { "textKey": "role.financeReviewer" }
}
```

- **Name.** A role name is lower-case letters, digits and hyphens. It starts
  with a letter and is at most 60 characters long. Roles claim values and
  task assignees already use names such as `finance-reviewer`, which the
  usual resource name rule refuses. With one lower-case form, letter case
  never matters.
- **Claim values.** A role matches a roles claim value exactly. A claim value
  that names no declared role is ignored.
- **Label.** The `label` is required. Its text key joins the `AXC0028`
  check.
- **Schema.** A name in another form, or a missing `label`, is `AXC0004`.
  Two roles with the same name are `AXC0006`.

A `policy` resource gives roles grants. The record API and the data source
endpoint enforce entity and data source grants. Both enforce the filter and
fields rules below: the data source endpoint adds the root entity's read
filters to its query and refuses a data source that shows a field the user
may not read. The purchase request sample uses the four policies below.
Employees reach their own requests, department managers the requests of
their department, and finance reviewers the submitted requests and their
outcomes. Operators inspect the approvals and change nothing:

```json
{
  "id": "2eeb31d5-47d9-4fb9-bd84-eb7ffdc11f12",
  "kind": "policy",
  "name": "EmployeeAccess",
  "formatVersion": 1,
  "roles": ["employee"],
  "grants": [
    { "entity": "PurchaseRequest", "actions": ["read"], "filter": "requester == currentUser()" },
    {
      "entity": "PurchaseRequest",
      "actions": ["create", "update"],
      "filter": "requester == currentUser() and status in ('draft', 'returned')",
      "fields": ["title", "department", "supplier", "currency", "lineItems", "status"]
    },
    { "entity": "Department", "actions": ["read"] },
    { "entity": "Supplier", "actions": ["read"] },
    { "dataSource": "PurchaseRequestList", "actions": ["read"] },
    { "process": "PurchaseRequestApproval", "actions": ["start"] }
  ]
}
```

```json
{
  "id": "7d9f722e-7406-4a39-87d5-e99293353a05",
  "kind": "policy",
  "name": "DepartmentManagerAccess",
  "formatVersion": 1,
  "roles": ["department-manager"],
  "grants": [
    { "entity": "PurchaseRequest", "actions": ["read"], "filter": "department.manager == currentUser()" },
    { "entity": "Department", "actions": ["read"] },
    { "entity": "Supplier", "actions": ["read"] },
    { "dataSource": "PurchaseRequestList", "actions": ["read"] },
    { "dataSource": "RequestsByDepartment", "actions": ["read"] }
  ]
}
```

```json
{
  "id": "d9788ec8-1186-488d-8c83-cbc9e5441d11",
  "kind": "policy",
  "name": "FinanceReviewerAccess",
  "formatVersion": 1,
  "roles": ["finance-reviewer"],
  "grants": [
    { "entity": "PurchaseRequest", "actions": ["read"], "filter": "status in ('submitted', 'approved', 'returned', 'rejected')" },
    { "entity": "Department", "actions": ["read"] },
    { "entity": "Supplier", "actions": ["read"] },
    { "dataSource": "PurchaseRequestList", "actions": ["read"] },
    { "dataSource": "RequestsByDepartment", "actions": ["read"] },
    { "process": "PurchaseRequestApproval", "actions": ["inspect", "cancel", "retry", "resolve"] }
  ]
}
```

```json
{
  "id": "ad149043-1f24-4e7d-a9b8-1ba518f8e010",
  "kind": "policy",
  "name": "OperatorAccess",
  "formatVersion": 1,
  "roles": ["operator"],
  "grants": [
    { "entity": "PurchaseRequest", "actions": ["read"] },
    { "entity": "Department", "actions": ["read"] },
    { "entity": "Supplier", "actions": ["read"] },
    { "process": "PurchaseRequestApproval", "actions": ["inspect"] }
  ]
}
```

- **Roles.** `roles` lists one or more declared roles. A user who holds any
  of them gets every grant of the policy.
- **Grants.** `grants` lists one or more grants. Each has exactly one target,
  `entity`, `dataSource`, `process` or `endpoint`, and one or more `actions`:
  - an `entity` takes `read`, `create`, `update`, `delete`, `export` and
    `import`;
  - a `dataSource` takes `read` and `export`;
  - a `process` takes `start`, `inspect`, `cancel`, `retry`, `resolve` and
    `reassign`, see [Reassign](task-deadlines.md#reassign);
  - an `endpoint` takes `invoke`.
  - `export` reaches only what the user's `read` grants reach, and needs a
    `read` grant on the same target in the same policy, or it is `AXC0265`.
    `import` writes through the user's `create` grants, and needs a `create`
    grant on the same entity in the same policy, or it is `AXC0265`. The
    import checks `update` as well for an import with a key
    _(planned for M15)_. See [import and export](import-export.md#grants) and
    [D79](../decisions/d79-import-and-export-under-policies.md).
- **Filter.** An entity grant may have a `filter`, a boolean
  [expression](expressions.md#names-and-references) over the record. It sees
  the entity's fields, paths through `reference` fields of up to 3 hops, the
  named rules and [`currentUser()`](expressions.md#current-user). `any` and
  `count` test a child collection or a `references` field of the entity, so
  `any(assignees, userId == currentUser())` reads only the records that list
  the user. Without it,
  the grant covers every record. On `read`, `update` and
  `delete` the stored record must match it. On `create` and `update` the new
  record must match it too. The employee's write grant above keeps a request
  at `draft` or `returned`, so a new request is saved with one of those
  statuses, and the process moves it on.
- **Fields.** An entity grant may have `fields`, a list of field names of the
  entity. Without it, the grant covers every field. A `child-collection`
  field covers its rows as a whole. A user reads the union of the `fields`
  of their `read` grants, creates those of their `create` grants and updates
  those of their `update` grants. A delete removes the whole record, so
  `fields` on a `delete` grant limits nothing. The `delete` grant is what
  shows a table's delete action. Only the server checks
  the record filter, on `DELETE`, so a shown button can still meet a `403`.
- **Combining.** The grants of all the user's policies combine with OR. A
  filter that is `false`, `null` or fails gives nothing through its grant.
  See [Combining grants](security.md#combining-grants).

The `cancel` action of a `process` grant lets a user cancel that process's
instances, see [Cancel](process-inspection.md#cancel). The `retry` action lets a user
retry its failed instances, see [Retry](process-inspection.md#retry). The `resolve`
action lets a user settle an unknown connector call, see
[Resolve](process-inspection.md#resolve). That covers a connector call and a package call.
Each is checked on its own, and none implies another. `start` implies none of
them. A grant takes no `filter` or `fields`, as for `start`. The finance
reviewer policy above lets its reviewers inspect, cancel, retry and resolve an approval. An operator role
that holds all three has:

```json
{ "process": "PurchaseRequestApproval", "actions": ["retry", "cancel", "resolve"] }
```

A policy also grants inbound [endpoints](integration-resources.md#endpoint-resource). The target is
`endpoint`, and its only action is `invoke`. An `endpoint` grant takes no
`filter` or `fields`. The grant, its compile checks and its enforcement are
built. Holding it is enough to call the endpoint: the record create and the
process start inside the call need no `entity` or `process` grant of the
caller. The purchase request sample gives the purchasing system this policy, in
`policies/purchasing-system-access.json`:

```json
{
  "id": "1a742db5-543d-4e8d-8c66-61bf42a1b93b",
  "kind": "policy",
  "name": "PurchasingSystemAccess",
  "formatVersion": 1,
  "roles": ["purchasing-system"],
  "grants": [{ "endpoint": "SubmitPurchaseRequest", "actions": ["invoke"] }]
}
```

Its role, in `roles/purchasing-system.json`:

```json
{
  "id": "ae4c47b6-4ece-4666-ae0a-4e7b6731bf7d",
  "kind": "role",
  "name": "purchasing-system",
  "formatVersion": 1,
  "label": { "textKey": "role.purchasingSystem" }
}
```

The caller holds the role through the roles claim of its token, as a user
does. See [Machine callers](security.md#machine-callers). The sample
holds this policy and role with its `SubmitPurchaseRequest` endpoint.

Compile checks, each a compile error:

- A process task's `assignee.role` that names no declared role, compared
  exactly, is `AXC0089`.
- A policy `roles` entry that names no declared role, compared exactly, is
  `AXC0090`.
- A grant without exactly one of `entity`, `dataSource`, `process` and
  `endpoint` is `AXC0091`.
- A target that names no loaded entity, data source, process or endpoint,
  ignoring letter case, is `AXC0092`.
- An action that the target's kind does not take, such as `start` on an
  entity or `read` on an endpoint, is `AXC0091`. So is `inspect` on anything
  but a process.
- An `export` without a `read`, or an `import` without a `create`, on the
  same target in the same policy, ignoring letter case, is `AXC0265` at the
  action. An action the target's kind does not take is left to `AXC0091`.
- A grant on a child entity is `AXC0093`. Child rows are read and written
  through their owner's grants.
- A `filter` or `fields` on a `dataSource`, `process` or `endpoint` grant is
  `AXC0091`.
- A `fields` entry that names no field of the entity, ignoring letter case,
  is `AXC0094`.
- A `filter` that is not boolean is `AXC0048`, and one that uses something
  outside the [SQL subset](expressions.md#sql-subset), such as an aggregate
  other than `any` and `count`, or `lower`, is `AXC0053`. It gets the other
  [expression diagnostics](expressions.md#diagnostics) too.
- `currentUser()` anywhere but a policy filter is `AXC0095`.

The schema checks the shape of each property: `roles` and `grants` are
non-empty lists, each action is one of `read`, `create`, `update`, `delete`,
`export`, `import`, `start`, `invoke`, `inspect`, `cancel`, `retry`, `resolve` and `reassign`, and a grant has no other properties. A problem there is
`AXC0004`. The schema also lists the actions each target kind takes, as
`entityAction`, `dataSourceAction`, `processAction` and `endpointAction`. The
compiler, the policy matrix and Studio read those lists, so each lives in one
place. The compiler, not the schema, refuses an action the target's kind does
not take.

## Record creator

A `text` field can declare `"creator": true`. On create it gets the signed-in
user's id, and policies use it, for example `requester == currentUser()`:

```json
{
  "id": "d9ccbf89-5004-4d52-8885-2ee377b7caa5",
  "kind": "entity",
  "name": "PurchaseRequest",
  "formatVersion": 1,
  "label": { "textKey": "purchaseRequest.label" },
  "displayField": "title",
  "fields": [
    { "name": "title", "type": "text", "required": true, "maxLength": 200 },
    { "name": "requester", "type": "text", "maxLength": 100, "creator": true }
  ]
}
```

- **Value.** A create through the record API sets it to the user id from the
  session, in the create's transaction. A create by the system or by seed
  data leaves it `null`.
- **Not writable.** Clients cannot write it, as for a
  [sequence](resource-files.md#sequences) field. A body that sets it on create or update, even
  to `null`, is a `400` at its pointer with the message "Cannot be set.". A
  seed value for it is `AXC0033`.
- **Allowed fields.** `creator` is allowed only on a `text` field. It cannot
  be combined with `required: true`, `expression` or `sequence`, and a child
  entity's field cannot have it. Each is `AXC0013` at `/fields/{i}/creator`.

## Sensitive fields

A field can declare `"sensitive": true`. Its value never
appears in [process inspection](process-inspection.md#values-and-masking), for any
user:

```json
{
  "id": "6b0f3c1e-8a52-4d7b-9e14-0c5a7d2f9b36",
  "kind": "entity",
  "name": "Supplier",
  "formatVersion": 1,
  "label": { "textKey": "supplier.label" },
  "displayField": "name",
  "fields": [
    { "name": "name", "type": "text", "required": true, "maxLength": 200 },
    { "name": "bankAccount", "type": "text", "maxLength": 34, "sensitive": true }
  ]
}
```

- **Effect.** A call input that reads or writes the field is hidden in
  inspection, as [Values and masking](process-inspection.md#values-and-masking) says.
  The record API ignores the flag and still shows the field to users whose
  grants cover it.
- **Default.** `sensitive` defaults to `false`. It changes no table, so adding
  or removing it needs no migration.
- **Allowed fields.** `sensitive` is allowed on every type except
  `child-collection`, and on the fields of a child entity. On a
  `child-collection` field it is `AXC0013` at `/fields/{i}/sensitive`. A value
  that is not a boolean is `AXC0004`. Even `"sensitive": false` on a
  `child-collection` field is `AXC0013`, as `required` and `unique` are. A
  `child-collection` field in a call mapping counts as sensitive when any field
  of its child entity is sensitive.
- **Two releases.** A field counts as sensitive when the release an instance is
  pinned to or the active release marks it.
- **Notifications.** A `values` expression of a `notify` step may not read a
  sensitive field, directly or through a path. It is
  [`AXC0280`](notifications.md#placeholders), because a notification stores its
  values and shows them to every recipient.
