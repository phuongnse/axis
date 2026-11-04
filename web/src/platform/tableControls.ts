/** Proposed: what a table's search, filter and columns controls work with, apart from how they draw. */

/** Proposed: the rows where a searchable column holds the text, ignoring letter case. */
export function searchRows<T>(
  rows: readonly T[],
  columns: readonly { searchText?: (row: T) => string | null }[],
  text: string,
): readonly T[] {
  const needle = text.trim().toLowerCase()
  const searchable = columns.flatMap((column) => (column.searchText ? [column.searchText] : []))
  return needle === ''
    ? rows
    : rows.filter((row) => searchable.some((read) => read(row)?.toLowerCase().includes(needle)))
}

/** Proposed: a column a table filters on, by the kind of value it holds. */
export interface FilterField {
  key: string
  label: string
  type: 'text' | 'enum' | 'number' | 'date'
  options?: { value: string; label: string }[]
}

export type FilterOperator =
  | 'contains'
  | 'notContains'
  | 'is'
  | 'isNot'
  | 'anyOf'
  | 'noneOf'
  | 'greater'
  | 'less'
  | 'before'
  | 'after'
  | 'empty'
  | 'notEmpty'

export interface FilterRule {
  kind: 'rule'
  field: string
  operator: FilterOperator
  value: string | string[]
}

/** A group joins what it holds with And or Or. A group may hold groups, to any depth. */
export interface FilterGroup {
  kind: 'group'
  join: 'and' | 'or'
  items: (FilterRule | FilterGroup)[]
}

export const operatorsOf: Record<FilterField['type'], FilterOperator[]> = {
  text: ['contains', 'notContains', 'is', 'isNot', 'empty', 'notEmpty'],
  enum: ['anyOf', 'noneOf', 'empty', 'notEmpty'],
  number: ['is', 'isNot', 'greater', 'less', 'empty', 'notEmpty'],
  date: ['before', 'after', 'empty', 'notEmpty'],
}

export const emptyFilter: FilterGroup = { kind: 'group', join: 'and', items: [] }

export const takesNoValue = (operator: FilterOperator) => operator === 'empty' || operator === 'notEmpty'

function isComplete(rule: FilterRule) {
  return takesNoValue(rule.operator) || (Array.isArray(rule.value) ? rule.value.length > 0 : rule.value !== '')
}

/** Drops the rules with no value yet and the groups left empty, so the filter holds only what applies. */
export function pruned(group: FilterGroup): FilterGroup {
  const items = group.items.flatMap<FilterRule | FilterGroup>((item) => {
    if (item.kind === 'rule') {
      return isComplete(item) ? [item] : []
    }
    const inner = pruned(item)
    return inner.items.length > 0 ? [inner] : []
  })
  return { ...group, items }
}

function ruleMatches(rule: FilterRule, value: string | number | null) {
  const empty = value === null || value === ''
  const text = String(value ?? '').toLowerCase()
  const wanted = Array.isArray(rule.value) ? rule.value : String(rule.value).toLowerCase()
  const number = Number(rule.value)
  const time = (date: string | number | null) => new Date(String(date)).getTime()
  switch (rule.operator) {
    case 'empty':
      return empty
    case 'notEmpty':
      return !empty
    case 'contains':
      return text.includes(wanted as string)
    case 'notContains':
      return !text.includes(wanted as string)
    case 'is':
      return text === wanted
    case 'isNot':
      return text !== wanted
    case 'anyOf':
      return !empty && (wanted as string[]).includes(String(value))
    case 'noneOf':
      return empty || !(wanted as string[]).includes(String(value))
    case 'greater':
      return !empty && Number(value) > number
    case 'less':
      return !empty && Number(value) < number
    case 'before':
      return !empty && time(value) < time(String(rule.value))
    case 'after':
      return !empty && time(value) > time(String(rule.value))
  }
}

/** Proposed: whether a row passes a filter, where `read` gives the row's value of a field. */
export function matchesFilter(group: FilterGroup, read: (field: string) => string | number | null): boolean {
  const passes = (item: FilterRule | FilterGroup) =>
    item.kind === 'group' ? matchesFilter(item, read) : ruleMatches(item, read(item.field))
  if (group.items.length === 0) {
    return true
  }
  return group.join === 'and' ? group.items.every(passes) : group.items.some(passes)
}

/** Proposed: reads a filter from the address. One that does not parse is no filter. */
export function parseFilter(value: string | null): FilterGroup {
  if (!value) {
    return emptyFilter
  }
  try {
    const parsed = JSON.parse(value) as FilterGroup
    return parsed.kind === 'group' && Array.isArray(parsed.items) ? parsed : emptyFilter
  } catch {
    return emptyFilter
  }
}

export const countRules = (group: FilterGroup): number =>
  group.items.reduce((total, item) => total + (item.kind === 'rule' ? 1 : countRules(item)), 0)

/** Proposed: the columns a person chose: their keys in order, and which of them they hid. */
export interface ColumnChoice {
  order: string[]
  hidden: string[]
}

/**
 * Proposed: reads a choice from the address, as `order` with a leading `-` on a hidden column. A key the table no longer has
 * is dropped, and a column the address does not name keeps its place at the end, so a new column always shows.
 */
export function parseColumnChoice(value: string | null, keys: readonly string[]): ColumnChoice {
  const entries = (value ?? '').split(',').filter(Boolean)
  const named = entries.map((entry) => entry.replace(/^-/, '')).filter((key) => keys.includes(key))
  return {
    order: [...named, ...keys.filter((key) => !named.includes(key))],
    hidden: entries.filter((entry) => entry.startsWith('-')).map((entry) => entry.slice(1)),
  }
}

export const writeColumnChoice = ({ order, hidden }: ColumnChoice) =>
  order.map((key) => (hidden.includes(key) ? `-${key}` : key)).join(',')

/** Proposed: the columns to draw: the identifying one first, then the others as chosen. */
export function chosenColumns<T extends { key: string }>(columns: readonly T[], choice: ColumnChoice): T[] {
  const [first, ...others] = columns
  const shown = choice.order
    .filter((key) => key !== first.key && !choice.hidden.includes(key))
    .flatMap((key) => others.filter((column) => column.key === key))
  return [first, ...shown]
}
