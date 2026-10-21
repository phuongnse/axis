# D17. Entity logic and child collections

Status: **Agreed**

Entities get logic and owned rows in M2. See
[configuration](../reference/resource-files.md#entity-logic),
[storage](../reference/storage.md#child-tables-and-computed-columns) and the
[record API](../reference/record-api.md#child-rows-computed-fields-and-validations).

- **Named rules:** a `rule` resource holds a named expression with typed
  parameters and a result type. Any expression can call it.
- **Validation:** an entity declares validation rules. Each has a boolean
  expression, a text key for its message and the field it is reported on. The
  server checks them on create and update, and a failure is a `400` at the
  field's path. Validation on the client as the user types comes later
  ([D20](d20-human-tasks-task-inbox-and-forms.md)).
- **Computed fields:** a field can have an expression. Its value is stored in
  its column and recomputed on every write of the record, in the same
  transaction. Clients cannot write it.
- **Child collections:** a field type that owns the rows of a child entity,
  such as line items. The rows are stored in the child entity's table with a
  foreign key to the owner, are deleted with the owner, and are read and
  written only through the owner record, in one request, under the owner's
  version.

_Why:_ see [knowledge](../domain/knowledge.md#logic-in-configuration). Logic
stays in typed expressions with no side effects (D16). Line items are part of
their owner, so they share its version and its transaction.
