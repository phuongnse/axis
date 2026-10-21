# D16. Expression language

Status: **Agreed**

One expression language holds the logic of validation, named rules, computed
fields and data source filters. See [the reference](../reference/expressions.md).

- **Syntax:** a small infix syntax familiar to authors, such as
  `quantity * unitPrice` and `status == 'submitted' and total >= 10000`.
- **Nothing else:** no loops, no variables or assignment, no side effects and
  no inline code (D6).
- **Bounded cost:** limits are checked at compile time, and a step budget
  applies at run time.
- **Two back ends:** one language, run by an interpreter and translated to SQL
  for data source filters.

_Why:_ see [knowledge](../domain/knowledge.md#logic-in-configuration).
