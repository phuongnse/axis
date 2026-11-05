/**
 * Proposed: the functions an expression may call, as the expressions reference lists them, each with its parameters,
 * its result, its group and an example. Its words are the text `shell.studio.expression.fn.<name>`. The editor's
 * completion, its signature line and its Fields and functions panel all read this one list. For the issue: the server's
 * `ExpressionFunctions` is to answer it, with where each function is allowed, so the client keeps no copy.
 */
export interface ExpressionFunction {
  name: string
  /** The parameters by their meaning, such as `text` or `days`. A last one ending in `…` repeats. */
  parameters: readonly string[]
  /** The result type, or a phrase such as `type of item`. */
  result: string
  group: 'text' | 'number' | 'date' | 'logic' | 'list' | 'user'
  example: string
}

export const expressionFunctions: readonly ExpressionFunction[] = [
  { name: 'length', parameters: ['text'], result: 'integer', group: 'text', example: 'length(title) <= 80' },
  { name: 'contains', parameters: ['text', 'part'], result: 'boolean', group: 'text', example: "contains(email, '@')" },
  {
    name: 'startsWith',
    parameters: ['text', 'start'],
    result: 'boolean',
    group: 'text',
    example: "startsWith(code, 'PR-')",
  },
  {
    name: 'endsWith',
    parameters: ['text', 'end'],
    result: 'boolean',
    group: 'text',
    example: "endsWith(email, '.vn')",
  },
  {
    name: 'concat',
    parameters: ['text', 'text…'],
    result: 'text',
    group: 'text',
    example: "concat(firstName, ' ', lastName)",
  },
  { name: 'lower', parameters: ['text'], result: 'text', group: 'text', example: 'lower(email)' },
  { name: 'upper', parameters: ['text'], result: 'text', group: 'text', example: 'upper(code)' },
  { name: 'trim', parameters: ['text'], result: 'text', group: 'text', example: "trim(title) != ''" },
  { name: 'abs', parameters: ['number'], result: 'type of number', group: 'number', example: 'abs(difference)' },
  {
    name: 'round',
    parameters: ['number', 'digits'],
    result: 'decimal',
    group: 'number',
    example: 'round(amount * 1.1, 2)',
  },
  { name: 'floor', parameters: ['number'], result: 'type of number', group: 'number', example: 'floor(hours)' },
  { name: 'ceiling', parameters: ['number'], result: 'type of number', group: 'number', example: 'ceiling(hours)' },
  { name: 'year', parameters: ['date'], result: 'integer', group: 'date', example: 'year(startDate)' },
  { name: 'month', parameters: ['date'], result: 'integer', group: 'date', example: 'month(startDate)' },
  { name: 'day', parameters: ['date'], result: 'integer', group: 'date', example: 'day(startDate)' },
  {
    name: 'addDays',
    parameters: ['date', 'days'],
    result: 'date',
    group: 'date',
    example: 'addDays(startDate, 30)',
  },
  {
    name: 'daysBetween',
    parameters: ['from', 'to'],
    result: 'integer',
    group: 'date',
    example: 'daysBetween(startDate, endDate) <= 14',
  },
  {
    name: 'date',
    parameters: ["'yyyy-mm-dd'"],
    result: 'date',
    group: 'date',
    example: "startDate >= date('2026-01-01')",
  },
  {
    name: 'dateTime',
    parameters: ["'yyyy-mm-ddThh:mm:ssZ'"],
    result: 'date-time',
    group: 'date',
    example: "dateTime('2026-01-01T00:00:00Z')",
  },
  { name: 'now', parameters: [], result: 'date-time', group: 'date', example: 'submittedAt <= now()' },
  { name: 'today', parameters: [], result: 'date', group: 'date', example: 'dueDate >= today()' },
  { name: 'toDate', parameters: ['date-time'], result: 'date', group: 'date', example: 'toDate(submittedAt)' },
  {
    name: 'if',
    parameters: ['condition', 'then', 'else'],
    result: 'type of then',
    group: 'logic',
    example: 'if(urgent, 1, 3)',
  },
  {
    name: 'coalesce',
    parameters: ['value', 'value…'],
    result: 'type of value',
    group: 'logic',
    example: 'coalesce(discount, 0)',
  },
  {
    name: 'count',
    parameters: ['list', 'condition?'],
    result: 'integer',
    group: 'list',
    example: 'count(lineItems) > 0',
  },
  {
    name: 'sum',
    parameters: ['list', 'item'],
    result: 'type of item',
    group: 'list',
    example: 'sum(lineItems, amount)',
  },
  { name: 'avg', parameters: ['list', 'item'], result: 'decimal', group: 'list', example: 'avg(lineItems, unitPrice)' },
  {
    name: 'min',
    parameters: ['list', 'item'],
    result: 'type of item',
    group: 'list',
    example: 'min(lineItems, dueDate)',
  },
  {
    name: 'max',
    parameters: ['list', 'item'],
    result: 'type of item',
    group: 'list',
    example: 'max(lineItems, amount)',
  },
  {
    name: 'any',
    parameters: ['list', 'condition'],
    result: 'boolean',
    group: 'list',
    example: 'any(lineItems, quantity > 100)',
  },
  {
    name: 'all',
    parameters: ['list', 'condition'],
    result: 'boolean',
    group: 'list',
    example: 'all(lineItems, amount > 0)',
  },
  {
    name: 'first',
    parameters: ['list', 'condition', 'item'],
    result: 'type of item',
    group: 'list',
    example: 'first(lineItems, amount > 1000, description)',
  },
  { name: 'currentUser', parameters: [], result: 'text', group: 'user', example: 'requester == currentUser()' },
]

/** The groups in the order the panel shows them. */
export const expressionFunctionGroups = ['text', 'number', 'date', 'logic', 'list', 'user'] as const

/** The functions that read the transaction's time, which a computed field may not call (AXC0086). */
export const timeFunctions = new Set(['now', 'today', 'toDate'])

/** The aggregates, which need a child collection, so a default, which has no record, cannot call them. */
export const aggregateFunctions = new Set(
  expressionFunctions.filter((entry) => entry.group === 'list').map((entry) => entry.name),
)

/** A function as its signature reads: `daysBetween(from, to) → integer`. */
export function signatureOf(entry: ExpressionFunction): string {
  return `${entry.name}(${entry.parameters.join(', ')}) → ${entry.result}`
}

/**
 * Proposed: the operators, keywords and values of the language, as its grammar has them, each with what it does and an
 * example, in `shell.studio.expression.op.<id>`. `after` ones follow a value, such as `>` or `is null`, and `operand`
 * ones start one, such as `not` or `true`. `types` are the value types an `after` one takes; with none it takes any.
 * `insert` is what goes in, and `cursorBack` puts the cursor that far before its end, inside a bracket or quotes.
 */
export interface ExpressionOperator {
  id: string
  insert: string
  cursorBack?: number
  group: 'compare' | 'empty' | 'logic' | 'list' | 'math' | 'value'
  position: 'after' | 'operand'
  types?: readonly string[]
  example: string
}

const numeric = ['integer', 'decimal', 'money']
const ordered = [...numeric, 'date', 'date-time']
const comparable = ['text', 'enum', 'boolean', 'reference', ...ordered]

export const expressionOperators: readonly ExpressionOperator[] = [
  {
    id: 'equals',
    insert: '== ',
    group: 'compare',
    position: 'after',
    types: comparable,
    example: "status == 'Approved'",
  },
  {
    id: 'differs',
    insert: '!= ',
    group: 'compare',
    position: 'after',
    types: comparable,
    example: "status != 'Draft'",
  },
  { id: 'greater', insert: '> ', group: 'compare', position: 'after', types: ordered, example: 'totalAmount > 0' },
  { id: 'atLeast', insert: '>= ', group: 'compare', position: 'after', types: ordered, example: 'quantity >= 1' },
  { id: 'less', insert: '< ', group: 'compare', position: 'after', types: ordered, example: 'startDate < endDate' },
  { id: 'atMost', insert: '<= ', group: 'compare', position: 'after', types: ordered, example: 'discount <= 50' },
  { id: 'isNull', insert: 'is null', group: 'empty', position: 'after', example: 'totalAmount is null' },
  { id: 'isNotNull', insert: 'is not null', group: 'empty', position: 'after', example: 'approvedAt is not null' },
  {
    id: 'in',
    insert: 'in ()',
    cursorBack: 1,
    group: 'list',
    position: 'after',
    types: ['text', 'enum', ...ordered],
    example: "status in ('Draft', 'Submitted')",
  },
  { id: 'and', insert: 'and ', group: 'logic', position: 'after', example: 'quantity > 0 and unitPrice > 0' },
  { id: 'or', insert: 'or ', group: 'logic', position: 'after', example: 'totalAmount is null or totalAmount > 0' },
  { id: 'not', insert: 'not ', group: 'logic', position: 'operand', example: "not (status == 'Closed')" },
  { id: 'plus', insert: '+ ', group: 'math', position: 'after', types: numeric, example: 'subtotal + tax' },
  { id: 'minus', insert: '- ', group: 'math', position: 'after', types: numeric, example: 'total - discount' },
  { id: 'times', insert: '* ', group: 'math', position: 'after', types: numeric, example: 'quantity * unitPrice' },
  { id: 'divide', insert: '/ ', group: 'math', position: 'after', types: numeric, example: 'total / quantity' },
  { id: 'true', insert: 'true', group: 'value', position: 'operand', example: 'urgent == true' },
  { id: 'false', insert: 'false', group: 'value', position: 'operand', example: 'archived == false' },
  { id: 'null', insert: 'null', group: 'value', position: 'operand', example: 'coalesce(discount, null)' },
  { id: 'text', insert: "''", cursorBack: 1, group: 'value', position: 'operand', example: "title == 'Laptop'" },
  {
    id: 'dateValue',
    insert: "date('')",
    cursorBack: 2,
    group: 'value',
    position: 'operand',
    example: "startDate >= date('2026-01-01')",
  },
]

/** The operator groups in the order the panel shows them. */
export const expressionOperatorGroups = ['compare', 'empty', 'logic', 'list', 'math', 'value'] as const

/** The symbol or words an operator reads as, without the space or brackets it inserts. */
export function operatorSymbol(operator: ExpressionOperator): string {
  return operator.insert.replace(/ $/, '')
}

/** Whether a function's result is the type a place needs, so completion lists it first. */
export function givesType(entry: ExpressionFunction, type: string | undefined): boolean {
  if (type === undefined) {
    return false
  }
  if (entry.result === type) {
    return true
  }
  return (
    numeric.includes(type) &&
    (entry.result === 'integer' || entry.result === 'decimal' || entry.result.startsWith('type of'))
  )
}
