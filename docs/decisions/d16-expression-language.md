# D16. Expression language

Status: **Agreed**

## Context

[D6](d6-in-configuration-logic-uses-a-typed-expression-language.md) asks for one typed expression
language without side effects. Its expressions run in the server and the worker, and data source
filters must also run inside PostgreSQL.

## Decision

- **One language** holds the logic of validations, named rules, computed fields and data source
  filters.
- **A small infix syntax** that authors know, such as `quantity * unitPrice` and
  `status == 'submitted' and total >= 10000`.
- **Nothing else.** No loops, no variables or assignment, no side effects and no inline code.
- **Bounded cost.** Limits are checked at compile time, and a step budget applies at run time.
- **Two back ends.** An interpreter runs the language, and a translation to SQL runs data source
  filters.

The grammar, types, functions, limits and the SQL subset are in
[expressions](../reference/expressions.md).

## Consequences

- An author learns one language for every kind of logic, and one compile check covers it all.
- The interpreter and the SQL translation must give the same answer, so each feature is built
  twice, or kept out of the SQL subset.
