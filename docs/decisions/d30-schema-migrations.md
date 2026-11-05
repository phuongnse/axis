# D30. Schema migrations

Status: **Agreed**

## Context

Provisioning only adds to entity storage. Renaming or dropping a field or an entity, making a field
required on a table with rows, or changing a field's type needs a change that is not additive and
may lose data. Running work stays on its release, as [D11](d11-durable-process-engine.md) and
[D29](d29-release-pinning-and-text-resources.md) say, and may still read the old columns.
[Execution](../domain/knowledge.md#execution) rules out a partial commit that leaves storage and the
active release disagreeing.

## Decision

- **A migration is a resource.** A `migration` file changes one entity through an ordered list of
  changes: `renameField`, `dropField`, `dropEntity`, `fill` and `convert`. Migration files are part
  of the release and run in the order of their paths.
- **Values are expressions.** A `fill` or `convert` computes its values with an expression over the
  record, which must translate to the SQL subset and always reaches SQL as parameters.
- **Applied at activation, all or nothing.** Pending migrations run before additive provisioning,
  in the transaction that switches the active release. Any failure rolls back everything.
- **Once per tenant.** Each tenant database records the migrations it applied with their content
  hash. An applied migration that changed or is missing stops the activation.
- **Forward only.** A release that lacks an applied migration cannot be activated. The way back is
  a new release with a new forward migration.
- **Destructive changes wait for old work.** Activation is refused while running or waiting work
  pinned to another release reads a column that a rename, drop or conversion changes. A lock that
  every process start takes keeps that check true until the activation commits. A `fill` never
  waits.
- **Versions, not audit records.** A `fill` or `convert` increments the version of every row it
  changes, so an edit begun before it fails rather than overwrites it, and the migration's record is
  the only trace.
- **Computed fields follow.** A changed value that a computed field reads is computed again, as
  [D33](d33-recomputing-stored-computed-fields.md) says.

The resource is in [resource files](../reference/resource-files.md#resource-file-shape), and the
statements, their order, the locks and an example in [migrations](../reference/migrations.md#migrations).

## Consequences

- A removal or an incompatible change without a migration stays a compile diagnostic, so data is
  never lost by accident.
- On a busy application a destructive migration may be refused until old work ends, and an operator
  may have to drain it. Shipping a `fill` in an earlier release avoids the wait.
- A large `fill` or `convert` holds its locks until commit and delays process starts meanwhile.
