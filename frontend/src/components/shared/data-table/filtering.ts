import type {
  DataTableColumnDef,
  DataTableFilterCondition,
  DataTableFilterDefinition,
  DataTableFilterGroup,
  DataTableFilterOperator,
} from './types';

const valueFreeOperators = new Set<DataTableFilterOperator>(['isEmpty', 'isNotEmpty']);

const operatorsByKind = {
  text: [
    'contains',
    'notContains',
    'eq',
    'ne',
    'startsWith',
    'endsWith',
    'in',
    'notIn',
    'isEmpty',
    'isNotEmpty',
  ],
  number: [
    'eq',
    'ne',
    'lt',
    'lte',
    'gt',
    'gte',
    'between',
    'notBetween',
    'in',
    'notIn',
    'isEmpty',
    'isNotEmpty',
  ],
  date: ['eq', 'ne', 'lt', 'lte', 'gt', 'gte', 'between', 'notBetween', 'isEmpty', 'isNotEmpty'],
  dateTime: [
    'eq',
    'ne',
    'lt',
    'lte',
    'gt',
    'gte',
    'between',
    'notBetween',
    'isEmpty',
    'isNotEmpty',
  ],
  boolean: ['eq', 'ne', 'isEmpty', 'isNotEmpty'],
  singleChoice: ['eq', 'ne', 'in', 'notIn', 'isEmpty', 'isNotEmpty'],
  multiChoice: ['containsAny', 'containsAll', 'notContainsAny', 'isEmpty', 'isNotEmpty'],
} as const satisfies Record<
  DataTableFilterDefinition<unknown>['kind'],
  readonly DataTableFilterOperator[]
>;

export function createEmptyFilterExpression(): DataTableFilterGroup {
  return { id: crypto.randomUUID(), combinator: 'and', items: [] };
}

export function normalizeSearchValue(value: unknown): string {
  if (value === null || value === undefined) return '';
  if (Array.isArray(value)) return value.map(normalizeSearchValue).join(' ');
  if (typeof value === 'object') return Object.values(value).map(normalizeSearchValue).join(' ');
  return String(value).normalize('NFKD').toLocaleLowerCase();
}

export function filterOperatorsFor<TData>(
  definition: DataTableFilterDefinition<TData>,
): readonly DataTableFilterOperator[] {
  return operatorsByKind[definition.kind];
}

export function countFilterConditions(group: DataTableFilterGroup): number {
  return group.items.reduce(
    (count, item) => count + (isFilterGroup(item) ? countFilterConditions(item) : 1),
    0,
  );
}

export function pruneFilterExpression(
  group: DataTableFilterGroup,
  availableFields: ReadonlySet<string>,
): DataTableFilterGroup {
  const items: (DataTableFilterCondition | DataTableFilterGroup)[] = [];
  for (const item of group.items) {
    if (!isFilterGroup(item)) {
      if (availableFields.has(item.fieldId)) items.push(item);
      continue;
    }
    const nested = pruneFilterExpression(item, availableFields);
    if (nested.items.length > 0) items.push(nested);
  }
  return items.length === group.items.length &&
    items.every((item, index) => item === group.items[index])
    ? group
    : { ...group, items };
}

export function isValidFilterExpression<TData>(
  group: DataTableFilterGroup,
  definitions: ReadonlyMap<string, DataTableFilterDefinition<TData>>,
  root = true,
): boolean {
  if (!root && group.items.length === 0) return false;
  return group.items.every((item) => {
    if (isFilterGroup(item)) return isValidFilterExpression(item, definitions, false);
    const definition = definitions.get(item.fieldId);
    if (!definition || !filterOperatorsFor(definition).includes(item.operator)) return false;
    if (valueFreeOperators.has(item.operator)) return true;
    if (item.operator === 'between' || item.operator === 'notBetween') {
      return Array.isArray(item.value) && item.value.length === 2 && item.value.every(hasValue);
    }
    if (
      item.operator === 'in' ||
      item.operator === 'notIn' ||
      item.operator === 'containsAny' ||
      item.operator === 'containsAll' ||
      item.operator === 'notContainsAny'
    ) {
      return Array.isArray(item.value) && item.value.length > 0 && item.value.every(hasValue);
    }
    return hasValue(item.value);
  });
}

export function filterData<TData>(
  data: readonly TData[],
  expression: DataTableFilterGroup,
  columns: readonly DataTableColumnDef<TData>[],
): TData[] {
  if (expression.items.length === 0) return [...data];
  const evaluators = new Map(
    columns.flatMap((column) => {
      const fieldId = column.id ?? ('accessorKey' in column ? String(column.accessorKey) : '');
      const definition = column.meta?.filter;
      return fieldId && definition ? [[fieldId, { column, definition }] as const] : [];
    }),
  );
  return data.filter((row, index) => evaluateGroup(expression, row, index, evaluators));
}

function evaluateGroup<TData>(
  group: DataTableFilterGroup,
  row: TData,
  rowIndex: number,
  evaluators: ReadonlyMap<
    string,
    { column: DataTableColumnDef<TData>; definition: DataTableFilterDefinition<TData> }
  >,
): boolean {
  const results = group.items.map((item) => {
    if (isFilterGroup(item)) return evaluateGroup(item, row, rowIndex, evaluators);
    const evaluator = evaluators.get(item.fieldId);
    if (!evaluator) return true;
    return evaluateCondition(
      item,
      readValue(row, rowIndex, evaluator.column, evaluator.definition),
      evaluator.definition,
    );
  });
  return group.combinator === 'and' ? results.every(Boolean) : results.some(Boolean);
}

function evaluateCondition<TData>(
  condition: DataTableFilterCondition,
  candidate: unknown,
  definition: DataTableFilterDefinition<TData>,
): boolean {
  const empty =
    candidate === null ||
    candidate === undefined ||
    candidate === '' ||
    (Array.isArray(candidate) && candidate.length === 0);
  if (condition.operator === 'isEmpty') return empty;
  if (condition.operator === 'isNotEmpty') return !empty;
  if (empty) return false;

  const expectedValues = arrayValue(condition.value);
  const candidateValues = arrayValue(candidate);
  if (condition.operator === 'containsAny') {
    return expectedValues.some((value) =>
      candidateValues.some((item) => equal(item, value, definition.kind)),
    );
  }
  if (condition.operator === 'containsAll') {
    return expectedValues.every((value) =>
      candidateValues.some((item) => equal(item, value, definition.kind)),
    );
  }
  if (condition.operator === 'notContainsAny') {
    return expectedValues.every((value) =>
      candidateValues.every((item) => !equal(item, value, definition.kind)),
    );
  }
  if (condition.operator === 'in' || condition.operator === 'notIn') {
    const included = expectedValues.some((value) => equal(candidate, value, definition.kind));
    return condition.operator === 'in' ? included : !included;
  }

  const expected = expectedValues[0];
  if (condition.operator === 'contains' || condition.operator === 'notContains') {
    const included = normalizeSearchValue(candidate).includes(normalizeSearchValue(expected));
    return condition.operator === 'contains' ? included : !included;
  }
  if (condition.operator === 'startsWith') {
    return normalizeSearchValue(candidate).startsWith(normalizeSearchValue(expected));
  }
  if (condition.operator === 'endsWith') {
    return normalizeSearchValue(candidate).endsWith(normalizeSearchValue(expected));
  }
  if (condition.operator === 'eq' || condition.operator === 'ne') {
    const matches = equal(candidate, expected, definition.kind);
    return condition.operator === 'eq' ? matches : !matches;
  }

  const current = comparable(candidate, definition.kind);
  const first = comparable(expected, definition.kind);
  if (!Number.isFinite(current) || !Number.isFinite(first)) return false;
  if (condition.operator === 'lt') return current < first;
  if (condition.operator === 'lte') return current <= first;
  if (condition.operator === 'gt') return current > first;
  if (condition.operator === 'gte') return current >= first;
  const second = comparable(expectedValues[1], definition.kind);
  const between = Number.isFinite(second) && current >= first && current <= second;
  return condition.operator === 'between' ? between : !between;
}

function readValue<TData>(
  row: TData,
  index: number,
  column: DataTableColumnDef<TData>,
  definition: DataTableFilterDefinition<TData>,
): unknown {
  if (definition.getValue) return definition.getValue(row);
  if ('accessorFn' in column && column.accessorFn) return column.accessorFn(row, index);
  if ('accessorKey' in column && column.accessorKey) {
    return String(column.accessorKey)
      .split('.')
      .reduce<unknown>(
        (value, key) =>
          value && typeof value === 'object' ? (value as Record<string, unknown>)[key] : undefined,
        row,
      );
  }
  return undefined;
}

function comparable(value: unknown, kind: DataTableFilterDefinition<unknown>['kind']): number {
  if (kind === 'number') return Number(value);
  if (kind === 'date' || kind === 'dateTime') return new Date(String(value)).getTime();
  return Number.NaN;
}

function equal(
  left: unknown,
  right: unknown,
  kind: DataTableFilterDefinition<unknown>['kind'],
): boolean {
  if (kind === 'number') return Number(left) === Number(right);
  if (kind === 'date' || kind === 'dateTime')
    return comparable(left, kind) === comparable(right, kind);
  if (kind === 'boolean') return String(left) === String(right);
  return normalizeSearchValue(left) === normalizeSearchValue(right);
}

function arrayValue(value: unknown): unknown[] {
  return Array.isArray(value) ? value : [value];
}

function hasValue(value: unknown): boolean {
  return value !== null && value !== undefined && String(value).trim() !== '';
}

function isFilterGroup(
  item: DataTableFilterCondition | DataTableFilterGroup,
): item is DataTableFilterGroup {
  return 'items' in item;
}
