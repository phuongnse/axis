# D78. References from child rows and many-to-many

Status: **Agreed**

## Context

Real line items point to a product, and an asset carries many tags and licences. A child entity
could not hold a reference, and a record could not hold a set of records. Policies and data source
filters also need to test child rows and sets, such as "has a line for product X" or "assigned to
me", which the SQL subset of [D16](d16-expression-language.md) could not say.

## Decision

- **A child row may point to a record.** A child entity may have `reference` fields, with a foreign
  key and an index, and a record's labels follow its rows. A child entity still holds no child
  collection and is never the target of a reference.
- **`references` holds a set of records.** The platform stores it in a junction table that keeps the
  written order. A write replaces the whole set under the owner's version, as a child collection
  write does, and its expression type is `list<Target>`, which only aggregates read.
- **The SQL subset adds `any` and `count`.** `any(list, condition)`, `count(list)` and
  `count(list, condition)` over a child collection or a `references` field translate to an `EXISTS`
  or a `count(*)` subquery. The other aggregates stay out of the subset.

The detail is in [entity field types](../reference/resource-files.md#entity-field-types-and-constraints),
[schema planning](../reference/storage.md#schema-planning),
[reading values](../reference/record-api.md#reading-values) and the
[SQL subset](../reference/expressions.md#sql-subset).

## Consequences

- Line items stay owned by their record while they point outward.
- A plain set of records needs no link entity, and the owner's version stays the one concurrency
  check.
- A link that carries data of its own, such as a role on an assignment, is still a link entity with
  two references. That is a pattern for authors, not platform code.
- The subqueries are served by the indexes of [D77](d77-indexes-defaults-and-ranges.md).

## Alternatives

- **A link entity for every set.** Authors would model and secure a second entity for what is only
  a list of records.
