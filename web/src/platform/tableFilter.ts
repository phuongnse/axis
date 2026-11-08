// A table's filter has the record API's JSON shape and operators, see docs/reference/record-api.md#paging-and-sorting.

/** The kind of value a column filters on: the record API's `text`, `enum`, number and date field types. */
export type FilterKind = 'text' | 'enum' | 'number' | 'date'
export type FilterOp = 'eq' | 'ne' | 'contains' | 'lt' | 'lte' | 'gt' | 'gte' | 'empty' | 'notEmpty'
export type FilterValue = string | number

export interface FilterCondition {
  field: string
  op: FilterOp
  value?: FilterValue
}
export type FilterGroup = { all: FilterNode[] } | { any: FilterNode[] }
export type FilterNode = FilterCondition | FilterGroup

/** The operators each kind of value takes, in the order a picker lists them. */
export const filterOps: Record<FilterKind, FilterOp[]> = {
  text: ['eq', 'ne', 'contains', 'empty', 'notEmpty'],
  enum: ['eq', 'ne', 'empty', 'notEmpty'],
  number: ['eq', 'ne', 'lt', 'lte', 'gt', 'gte', 'empty', 'notEmpty'],
  date: ['eq', 'ne', 'lt', 'lte', 'gt', 'gte', 'empty', 'notEmpty'],
}

const allOps = new Set<string>(filterOps.number.concat(filterOps.text))

/** Whether the operator takes no value. */
export function isUnary(op: FilterOp): boolean {
  return op === 'empty' || op === 'notEmpty'
}

export function isGroup(node: FilterNode): node is FilterGroup {
  return 'all' in node || 'any' in node
}

/** Whether a group holds all its nodes (`all`) or any of them (`any`). */
export function groupMode(group: FilterGroup): 'all' | 'any' {
  return 'all' in group ? 'all' : 'any'
}

export function groupNodes(group: FilterGroup): FilterNode[] {
  return 'all' in group ? group.all : group.any
}

/** A group of the given mode that holds `nodes`. */
export function makeGroup(mode: 'all' | 'any', nodes: FilterNode[]): FilterGroup {
  return mode === 'all' ? { all: nodes } : { any: nodes }
}

function parseNode(value: unknown, fieldNames: readonly string[]): FilterNode | undefined {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return undefined
  }
  const keys = Object.keys(value)
  if (keys.length === 1 && (keys[0] === 'all' || keys[0] === 'any')) {
    const items: unknown = (value as Record<string, unknown>)[keys[0]]
    if (!Array.isArray(items)) {
      return undefined
    }
    const nodes: FilterNode[] = []
    for (const item of items) {
      const node = parseNode(item, fieldNames)
      if (!node) {
        return undefined
      }
      nodes.push(node)
    }
    return makeGroup(keys[0], nodes)
  }
  const { field, op, value: given } = value as Record<string, unknown>
  if (keys.some((key) => key !== 'field' && key !== 'op' && key !== 'value')) {
    return undefined
  }
  if (typeof field !== 'string' || !fieldNames.includes(field) || typeof op !== 'string' || !allOps.has(op)) {
    return undefined
  }
  if (isUnary(op as FilterOp)) {
    return given === undefined ? { field, op: op as FilterOp } : undefined
  }
  return typeof given === 'string' || typeof given === 'number'
    ? { field, op: op as FilterOp, value: given }
    : undefined
}

/**
 * Reads a filter from the `filter` text of the address, with `fieldNames` the columns that can be filtered on. Text that
 * is not valid JSON of the record API's shape, a field or operator it does not know, a null value or a value given
 * to `empty` or `notEmpty` makes the whole filter `undefined`. The filter is pruned, see {@link pruneFilter}.
 */
export function parseFilter(text: string, fieldNames: readonly string[]): FilterGroup | undefined {
  let json: unknown
  try {
    json = JSON.parse(text)
  } catch {
    return undefined
  }
  const root = parseNode(json, fieldNames)
  return root && isGroup(root) ? pruneFilter(root) : undefined
}

function pruneNode(node: FilterNode): FilterNode | undefined {
  if (isGroup(node)) {
    return pruneFilter(node)
  }
  if (isUnary(node.op)) {
    return { field: node.field, op: node.op }
  }
  return node.value === undefined || node.value === '' ? undefined : node
}

/**
 * The filter without a condition that has no value, except `empty` and `notEmpty`, and without a group left with no
 * condition. `undefined` when nothing is left.
 */
export function pruneFilter(group: FilterGroup): FilterGroup | undefined {
  const nodes = groupNodes(group).flatMap((node) => pruneNode(node) ?? [])
  return nodes.length > 0 ? makeGroup(groupMode(group), nodes) : undefined
}

/** How many conditions the filter holds, at any depth. */
export function countConditions(group: FilterGroup | undefined): number {
  return group ? groupNodes(group).reduce((sum, node) => sum + (isGroup(node) ? countConditions(node) : 1), 0) : 0
}

/** How a table reads one column of a row for the filter. */
export interface FilterReader<T> {
  kind: FilterKind
  value: (row: T) => FilterValue | null
}

function same(kind: FilterKind, cell: FilterValue, given: FilterValue): boolean {
  return kind === 'number' ? cell === given : String(cell) === String(given)
}

/** The order of a cell against a given value, or `undefined` when they cannot be ordered. Dates compare as ISO text. */
function order(kind: FilterKind, cell: FilterValue, given: FilterValue): number | undefined {
  if (kind === 'number') {
    return typeof cell === 'number' && typeof given === 'number' ? cell - given : undefined
  }
  const left = String(cell)
  const right = String(given)
  return left < right ? -1 : left > right ? 1 : 0
}

function holds(
  op: FilterOp,
  kind: FilterKind,
  cell: FilterValue | null,
  given: FilterValue | undefined,
  locale: string,
) {
  const empty = cell === null || cell === ''
  if (op === 'empty') {
    return empty
  }
  if (op === 'notEmpty') {
    return !empty
  }
  if (op === 'ne') {
    return empty || given === undefined || !same(kind, cell, given)
  }
  if (empty || given === undefined) {
    return false
  }
  if (op === 'eq') {
    return same(kind, cell, given)
  }
  if (op === 'contains') {
    return String(cell).toLocaleLowerCase(locale).includes(String(given).toLocaleLowerCase(locale))
  }
  const sign = order(kind, cell, given)
  if (sign === undefined) {
    return false
  }
  return op === 'lt' ? sign < 0 : op === 'lte' ? sign <= 0 : op === 'gt' ? sign > 0 : sign >= 0
}

/**
 * Whether a row matches the filter, as the record API matches a record: a value that is null, or the empty text, is
 * empty, `ne` also keeps an empty value, `contains` ignores letter case and the other operators never match an empty
 * value. A condition on a column `read` does not know matches every row.
 */
export function matchesFilter<T>(
  row: T,
  group: FilterGroup,
  read: (field: string) => FilterReader<T> | undefined,
  locale: string,
): boolean {
  const test = (node: FilterNode): boolean => {
    if (isGroup(node)) {
      return matchesFilter(row, node, read, locale)
    }
    const reader = read(node.field)
    return reader === undefined || holds(node.op, reader.kind, reader.value(row), node.value, locale)
  }
  const nodes = groupNodes(group)
  return groupMode(group) === 'all' ? nodes.every(test) : nodes.some(test)
}
