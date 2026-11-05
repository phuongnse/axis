# D17. Entity logic and child collections

Status: **Agreed**

## Context

An entity needs rules that refuse bad data, values computed from its other fields, and rows it owns,
such as the line items of a purchase request.
[Logic in configuration](../domain/knowledge.md#logic-in-configuration) keeps that logic in typed
expressions without side effects, as [D16](d16-expression-language.md) defines them.

## Decision

- **Named rules.** A `rule` resource holds a named expression with typed parameters and a result
  type, and any expression can call it.
- **Validations.** An entity declares validations, each with a boolean expression, a text key for
  its message and the field it is reported on. The server checks them on every create and update,
  and a failure is a `400` at the field's path.
- **Stored computed fields.** A field may have an expression. Its value is stored in its column and
  computed again on every write of the record, in the same transaction, and clients cannot write
  it.
- **Child collections.** A `child-collection` field owns the rows of a child entity. The rows live
  in the child entity's table with a foreign key to the owner, are deleted with the owner, and are
  read and written only through the owner record, in one request and under the owner's version.

The resource shapes are in [entity logic](../reference/resource-files.md#entity-logic), the storage
in [child tables and computed columns](../reference/child-tables-and-computed-columns.md#child-tables-and-computed-columns),
and the API in [child rows, computed fields and validations](../reference/record-api.md#child-rows-computed-fields-and-validations).

## Consequences

- Line items share their owner's version and transaction, so an edit of a purchase request and its
  lines succeeds or fails as one.
- Computed values can be read, filtered and sorted like stored ones. When their expression or
  inputs change, the stored values must be computed again, as
  [D33](d33-recomputing-stored-computed-fields.md) says.
- A child row has no route or grant of its own: it is read, written and granted through its owner.
