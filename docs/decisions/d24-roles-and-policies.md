# D24. Roles and policies

Status: **Agreed**

M4 adds authorization through roles and policies. The `role` and `policy`
resources and their compile checks are built, and so is the compile check of
`currentUser()`. The record API enforces entity grants, their record
filters and their field lists. The data source endpoint enforces grants,
the root entity's read filters and the readable fields. A process start
enforces the `start` grant and read access to the subject record. The task
API keeps the assignee check and leaves out the fields the user may not
read, and the worker's steps run outside policies. Navigation and page
metadata follow the grants, and page metadata leaves out the fields the user
may not read. Everything below is in place. See
[architecture](../reference/security.md#authorization) for the details. It builds on [D12](d12-authorization-is-server-side-and-default-deny.md).

- **Role resource:** a `role` resource has a name and a label text key. A
  policy and a task's `role` assignee must name a declared role, or
  compilation fails. Role claim values that name no declared role are
  ignored.
- **Policy resource:** a `policy` names one or more roles and holds grants.
  Each grant has one target and its actions:
  - an `entity`, with `read`, `create`, `update` and `delete`;
  - a `dataSource`, with `read`;
  - a `process`, with `start`.
  - [D79](d79-import-and-export-under-policies.md) adds `export` and
    `import` to entity grants and `export` to data source grants.
- **Record filter:** an entity grant may have a `filter`, a boolean
  expression over the record. It may use reference paths, rules and
  `currentUser()`.
- **`currentUser()`:** a new expression function that gives the signed-in
  user's id as `text`. It is allowed only in policy filters. Anywhere else it
  is a compile error.
- **Field list:** an entity grant may have `fields`. Without it, the grant
  covers every field.
- **Default-deny:**
  - No session is `401`.
  - A signed-in user with no matching grant gets `403`.
  - A record outside every read filter does not exist for the user, so it is
    `404`.
- **Combining:** the grants of all the user's policies combine with OR. A
  filter that is false, `null` or fails gives no records through its grant,
  so it can never widen access. A failure is logged.
- **Writes:** update and delete need the stored record to match an `update`
  or `delete` filter. Create and update also need the new values to match,
  so a user cannot move a record out of their reach.
- **Fields on reads:** the user may read the union of the `fields` of their
  `read` grants on the entity. Other fields are left out of records, data
  source rows, history details and page metadata. The readable fields are
  per entity, not per record.
- **Fields on writes:** writing a field that no `create` or `update` grant
  covers is `403` with the field's path.
- **Data sources:** the root entity's read filters are added to the data
  source's own filter in SQL. A data source that projects or aggregates a
  field the user may not read is `403` as a whole.
- **Reference labels and lookups:** the label of a referenced record shows
  when the user may read the target's display field. The target's record
  filters do not hide it. Lookups list target records through the user's read
  filters.
- **Record history:** needs read access to the record. Details leave out
  fields the user may not read.
- **Process start:** needs a `start` grant on the process and read access to
  the subject record.
- **Tasks:** the assignee check stays and is enough to list, read and
  complete a task. The task form leaves out fields the user may not read.
- **System work:** the worker, seed data and `updateRecord` steps run as
  `system` and are not checked by policies.
- **Record creator:** a `text` field can declare `"creator": true`. On create
  it gets the signed-in user's id, and clients cannot write it, like a
  sequence field. Policies use it, for example `requester == currentUser()`.
- **UI:** page metadata says which actions the user may take on the page's
  widget. Navigation leaves out pages the user cannot read. This only shapes
  the UI. The server checks every request.
- **Module:** `Axis.Policy` holds the compiled policies and their
  evaluation. Every endpoint and engine command calls it with a resource and
  an action.

_Why:_ see [knowledge](../domain/knowledge.md#authorization). A filter that is
false, `null` or fails adds nothing, so a mistake in one policy can never
widen access. Readable fields per entity keep queries simple: they do not
depend on which grant matched a record.
