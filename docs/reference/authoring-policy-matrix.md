# Policy matrix in the authoring API

`GET .../policy-matrix` of the [authoring API](authoring.md) answers what each role may do on each root entity, data source, process and
endpoint, and which grants give that access. Studio's [policy matrix](studio.md#policy-matrix) is built on
it. The answer is:

```
{ targets: [{ kind, id, name, module, actions: [...], entity }],
  roles: [{ id, name, accessPolicy: { name, id }, editable, readOnlyReason,
            cells: [{ kind, target, actions: [...], missingEntityRead,
                      grants: [{ policyId, policy, index, actions, filter, fields, shared, module }] }] }] }
```

- **Columns.** `targets` lists root entities first, with module entities before the application's
  own, then data sources, processes and endpoints. Child entities are left out. `kind` is `entity`,
  `dataSource`, `process` or `endpoint`. A name is written as in [Names](authoring.md#names), and `module` is the
  module id, or null. `entity` is a data source's root entity, and null for the other kinds. A column
  is identified by `kind` and `name`, not by `id`.
- **`actions`.** On a target, they are the actions it accepts, in the order
  [roles and policies](roles-and-policies.md) lists them. An entity takes `read`, `create`, `update`,
  `delete`, `export` and `import`. A data source takes `read` and `export`. A process takes `start`,
  `inspect`, `cancel`, `retry`, `resolve` and `reassign`. An endpoint takes `invoke`.
- **Rows and cells.** Each declared role is one row, in the order of the `roles` resources. Each row
  has one cell per column, in column order, empty ones included. A cell's `actions` are the role's
  actions there from all its policies, combined with OR as in
  [combining grants](security.md#combining-grants). An action that the column does not accept is left
  out, though the grant still lists its actions as written.
- **`export` and `import`.** An entity cell has `export` only when it also has `read`, and `import`
  only when it also has `create`. A data source cell has `export` only when it also has `read`, so it
  is dropped with `read` when the role cannot read the root entity. This is what the policy evaluator
  decides.
- **Data source `read`.** A data source cell has `read` only when the role can also read the data
  source's root entity. When it has a `read` grant but not that entity read, `read` is left out and
  `missingEntityRead` names the root entity. It is null otherwise.
- **`grants`.** Each grant of a policy that names the role: its `policyId`, `policy` name, `index`,
  and `actions`, `filter` and `fields` as written, not evaluated. `fields` is null for every field.
  `module` is the module id of a grant from a
  [module policy](modules-and-templates.md#processes-pages-policies-and-dependencies), else null.
- **`shared`.** A grant is shared unless it comes from the role's own `<Role>Access` policy and that
  policy names only that role.
- **`<Role>Access`.** `accessPolicy` is the PascalCase role name plus `Access`. It is found by name,
  ignoring letter case, and `id` is null when it does not exist. `editable` is true when it does not
  exist, or when its `roles` is exactly that one role. Otherwise `editable` is false and
  `readOnlyReason` is `accessPolicyShared`, for which Studio writes its own message.
- **Compiled in memory.** The folder is compiled in memory, as for [diagnostics](authoring.md#diagnostics), and no
  database is touched. A folder with errors still answers, from the files that loaded. A policy file
  left out for its own errors is absent, so its grants are missing.
