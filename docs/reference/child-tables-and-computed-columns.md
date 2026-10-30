# Child collections in storage

How a `child-collection` field and a computed field are stored, moved out of [storage.md](storage.md), which holds
the physical names, the column types, the database roles and the schema planning. The conventions of
[architecture.md](../architecture.md) apply: Dn refers to [decisions.md](../decisions.md).

## Child tables and computed columns

Child tables and computed columns are built (D17). The planning rules of
child tables are in [Schema planning](storage.md#schema-planning).

- **Child table.** The entity that a `child-collection` field names has a
  table with the usual names. Besides `id` (`uuid`) and its field columns, it
  has two system columns:

  | Column     | Type               | Meaning                                                                                                                              |
  | ---------- | ------------------ | ------------------------------------------------------------------------------------------------------------------------------------ |
  | `owner_id` | `uuid NOT NULL`    | The owner record's `id`. A foreign key to the owner table's `id` with `ON DELETE CASCADE`, named `fk_` + table + `_` + hash as usual |
  | `position` | `integer NOT NULL` | The row's order in the collection, the zero-based index in the array                                                                 |

  Rows are deleted with their owner.

- **No version.** A child table has no `version` column. Its rows live under
  the owner's version, and a write to the rows increments the owner's
  `version`.
- **Computed column.** A computed field is an ordinary
  column of its type. It is never `NOT NULL`, because a computed field cannot
  be `required`. It is written in the same transaction as the record, and for
  a child row before the owner's computed fields.
- **Existing rows.** A computed field added to a table that has rows is
  filled at activation through
  [Recomputing computed fields](migrations.md#recomputing-computed-fields). A row whose
  expression fails gets `NULL` and an error row, and activation goes on.
