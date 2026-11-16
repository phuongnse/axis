/**
 * One operator or value of the expression language, as the panel of the expanded editor lists it. `name` is what the
 * list shows, `insert` is the text a click puts at the cursor, and `cursorInside` puts the cursor one character before
 * the end of it, between the brackets or quotes where a value goes. The words of an operator are the text
 * `shell.studio.expression.op.<id>`.
 */
export interface ExpressionOperator {
  id: string
  name: string
  insert: string
  example: string
  cursorInside: boolean
}

function operator(id: string, name: string, insert: string, example: string, cursorInside = false): ExpressionOperator {
  return { id, name, insert, example, cursorInside }
}

/**
 * The client's one list of the language's operators and values, see
 * [the expression editor](docs/reference/studio.md#expression-editor) and
 * [operators and precedence](docs/reference/expressions.md#operators-and-precedence). An infix operator inserts spaces
 * around itself, so that `amount` and `==` do not run together.
 */
export const expressionOperators: readonly ExpressionOperator[] = [
  operator('equal', '==', ' == ', "status == 'Open'"),
  operator('not-equal', '!=', ' != ', "status != 'Open'"),
  operator('less', '<', ' < ', 'amount < 100'),
  operator('less-or-equal', '<=', ' <= ', 'amount <= 100'),
  operator('greater', '>', ' > ', 'amount > 100'),
  operator('greater-or-equal', '>=', ' >= ', 'amount >= 100'),
  operator('is-null', 'is null', ' is null', 'dueDate is null'),
  operator('is-not-null', 'is not null', ' is not null', 'dueDate is not null'),
  operator('and', 'and', ' and ', "amount > 100 and status == 'Open'"),
  operator('or', 'or', ' or ', "status == 'Open' or status == 'Waiting'"),
  operator('not', 'not', 'not ', "not (status == 'Closed')"),
  operator('in', 'in (…)', ' in ()', "status in ('Open', 'Waiting')", true),
  operator('add', '+', ' + ', 'price + tax'),
  operator('subtract', '-', ' - ', 'price - discount'),
  operator('multiply', '*', ' * ', 'quantity * price'),
  operator('divide', '/', ' / ', 'total / quantity'),
  operator('true', 'true', 'true', 'urgent == true'),
  operator('false', 'false', 'false', 'urgent == false'),
  operator('null', 'null', 'null', 'dueDate == null'),
  operator('text', "'text'", "''", "'Open'", true),
  operator('date', "date('…')", "date('')", "date('2026-01-31')", true),
]
