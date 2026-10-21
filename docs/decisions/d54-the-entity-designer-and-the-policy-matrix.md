# D54. The entity designer and the policy matrix

Status: **Agreed**

M11 gives Studio an entity designer _(built)_ and a policy matrix _(built)_.
The designer edits the fields of an entity and adds the migrations that keep
stored data. The matrix shows who may do what in one place. A new `changes`
route writes an entity and its migration in one call. See
[Studio](../reference/studio.md) for the screens and
[the changes route](../reference/authoring.md#changes) for the route. The
designer follows
[D24](d24-roles-and-policies.md),
[D30](d30-schema-migrations.md),
[D38](d38-studio-edits-application-folders-through-an-authoring-api.md)
and
[D46](d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md).
The owner confirmed these points on issue #781 on 2026-10-20.

- **Entity designer:**
  - An `entity` resource opens in the designer in place of the generic form.
    The JSON view stays.
  - The fields are a table. Its columns are name, label, type, required,
    unique and the type's own settings: `maxLength`, precision and scale,
    enum `values`, the reference `target` and the child entity.
  - A computed field's expression and each validation use the expression
    editor.
  - Fields can be added, removed and reordered. The display field is picked
    from the fields.
  - A module entity is read-only, as today.
- **Migrations from the designer:**
  - The designer compares with the entity as Studio loaded it. It does not
    read the active release.
  - Renaming a loaded field asks whether to keep its data. Yes adds a
    `renameField` change. Removing a loaded field asks whether to drop its
    data. Yes adds a `dropField` change, and No keeps the field.
  - A change the column cannot follow asks for a `convert` expression. That
    covers a new type, a narrower `maxLength` and a removed enum value.
  - A field that becomes required, or a new required field, offers a `fill`.
  - Switching to or from `enum`, and adding `unique`, offer no migration. The
    designer warns, and Validate reports `AXC0016`. No migration type covers
    these two changes.
  - Renaming or removing a `child-collection` field offers no migration,
    because it has no column.
  - Each save that needs migrations creates one new `migration` resource,
    named `<Entity>Change<UTC yyyyMMddHHmmss>`. It holds every change of that
    save in edit order. It never edits an existing migration, so applied
    files stay unchanged. Migrations run in file path order, so the files of
    one entity sort by time.
  - A hand-written migration of the same entity whose name sorts later runs
    after the designer's files. Nothing enforces this.
  - Validate still runs every activation check, so a missing migration shows
    before a publish.
- **The changes route:**
  - `POST /api/studio/apps/{application}/changes` takes a list of creates and
    patches, each patch with its version.
  - It takes the write lock once and checks every change before it writes
    any. A stale version or a bad change refuses the whole call.
  - It takes at most 20 changes and no deletes. Two changes for the same `id`
    are `400`. A patch change without `version` is `428`.
  - A server that dies during the writes can leave the first files written.
    The next diagnostics show the mismatch.
- **Policy matrix:**
  - One view per application shows roles as rows. The columns are entities,
    data sources, processes and endpoints. Child entities are left out,
    because a grant on one is `AXC0093`.
  - A cell shows the actions the role holds on that target from all its
    policies, combined with OR, as the runtime does.
  - A cell opens its grants. Each grant shows its actions, its record filter
    and its field list. The filter uses the expression editor, and the fields
    use the field picker.
  - An edit writes to the policy `<Role>Access`, where `<Role>` is the
    PascalCase form of the role name. So `department-manager` writes
    `DepartmentManagerAccess`. The policy is created when it is missing, with
    only that role in `roles`.
  - A grant from a policy that names several roles is shown as shared and
    opens that policy. So does a grant from another policy that names only
    this role. The matrix never rewrites a hand-written policy.
  - A `<Role>Access` that names other roles, or more than one, counts as
    shared. The matrix does not write to it, turns editing off for that role
    and says why.
  - An action that a target does not take cannot be chosen.
- **Not in M11:** deleting an entity with `dropEntity`, splitting shared
  policies and comparing against the active release.

_Why:_ the designer asks about each change while the author still remembers
it. Today a missing migration shows only when activation refuses it. The cost
is that it compares with what it loaded, not with the tenant. A field added
and renamed before any publish gets a migration it does not need, and
Validate reports it. One policy per role keeps a matrix edit from widening the
access of other roles. The cost is that a hand-written policy with several
roles still works, but the matrix does not split it. The `changes` route lets
an entity and its migration be saved together. The cost is a multi-file write,
which is new in the authoring API. It checks everything before it writes, but
a server that dies in the middle can leave one file written.
