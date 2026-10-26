# D78. References from child rows and many-to-many

Status: **Agreed**

M15 lets a child row point to a record, adds a field that holds a set of
records, and lets filters test child rows and sets. The owner agreed these
points on 2026-10-25. All of it is _(planned for M15)_. See
[entity logic](../reference/resource-files.md#entity-logic) and the
[SQL subset](../reference/expressions.md#sql-subset). It lifts one of the M2
limits on a child entity, and extends the SQL subset of
[D16](d16-expression-language.md) for data source and policy filters.

- **References from child rows:**
  - A child entity may have `reference` fields. `AXC0041` keeps refusing a
    `child-collection` field in a child entity.
  - A child entity is still never the `target` of a reference (`AXC0040`).
  - The child table gets a foreign key and an index on the column.
  - Labels follow the rows: a record's `labels` holds
    `"lineItems": [{ "product": "Desk" }]`, one object per row in row order.
  - Deleting a record that a child row points to is refused with the
    referenced-record `409`, as for a reference on a root entity.
  - Computed fields still never read a reference path.
- **Many-to-many:**
  - A new field type, `references`, holds a set of records of its `target`.
  - It is stored in a platform junction table named `m_` + entity id + `_` +
    hash of the field name, with `owner_id` (deleted with the owner) and
    `target_id` (a delete of the target is refused while linked), a
    `position` that keeps the written order, and a primary key over the first
    two.
  - The value is an array of ids, and its labels an array in the same order.
    A write replaces the whole set under the owner's version, as a child
    collection does. A set holds at most 1,000 ids.
  - Its expression type is `list<Target>`, which only aggregates read.
  - A link that carries its own data, such as a role on an assignment, stays
    a link entity with two references. That is a pattern, not platform code.
- **`any` and `count` in the SQL subset:**
  - `any(list, condition)`, `count(list)` and `count(list, condition)` over a
    child collection or a `references` field translate to an `EXISTS` or a
    `count(*)` subquery.
  - Inside the condition, names are the child row's fields, or the target's
    fields for `references`, with the usual paths through references.
  - So a policy or a data source filter can say "has a line for product X" or
    "assigned to me". The other aggregates stay out of the subset.
- **Studio:** the entity designer offers `references` with a target picker,
  and `reference` on a child entity.

_Why:_ real line items point to a product, and assets carry many tags and
licences. A child reference keeps line items owned by their record while they
point outward. A junction table managed by the platform gives many-to-many
without asking authors to model a link entity for a plain set, and writes of
the whole set keep the owner's version as the one concurrency check. Filters
need `any` and `count` to reach these sets, and both translate to plain
subqueries that the indexes of [D77](d77-indexes-defaults-and-ranges.md)
serve.
