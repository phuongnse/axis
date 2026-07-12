import { useEffect, useMemo, useState } from 'react';
import type { FullField, RuleGroupType, RuleType } from 'react-querybuilder';
import { QueryBuilder } from 'react-querybuilder';
import { filterOperatorsFor, isValidFilterExpression } from './filtering';
import {
  ShadcnQueryBuilderAction,
  ShadcnQueryBuilderSelector,
  ShadcnQueryBuilderValueEditor,
} from './QueryBuilderShadcn';
import type {
  DataTableFilterDefinition,
  DataTableFilterGroup,
  DataTableFilterOperator,
  DataTableMessages,
} from './types';

export interface DataTableFilterField<TData> {
  id: string;
  label: string;
  definition: DataTableFilterDefinition<TData>;
}

export function DataTableFilterBuilder<TData>({
  fields,
  expression,
  messages,
  onChange,
}: {
  fields: readonly DataTableFilterField<TData>[];
  expression: DataTableFilterGroup;
  messages: DataTableMessages;
  onChange: (expression: DataTableFilterGroup) => void;
}) {
  const [draft, setDraft] = useState<RuleGroupType>(() => toBuilderQuery(expression));
  const definitions = useMemo(
    () => new Map(fields.map((field) => [field.id, field.definition])),
    [fields],
  );
  const builderFields = useMemo<FullField[]>(
    () =>
      fields.map((field) => ({
        name: field.id,
        value: field.id,
        label: field.label,
        inputType: inputTypeFor(field.definition),
        values:
          'options' in field.definition
            ? field.definition.options.map((option) => ({
                name: option.value,
                value: option.value,
                label: option.label,
              }))
            : field.definition.kind === 'boolean'
              ? [
                  { name: 'true', value: 'true', label: messages.trueValue },
                  { name: 'false', value: 'false', label: messages.falseValue },
                ]
              : undefined,
      })),
    [fields, messages.falseValue, messages.trueValue],
  );
  const expressionDraft = fromBuilderQuery(draft, definitions);
  const valid = isValidFilterExpression(expressionDraft, definitions);

  useEffect(() => setDraft(toBuilderQuery(expression)), [expression]);

  return (
    <div className="grid gap-2" data-invalid={!valid || undefined}>
      <QueryBuilder
        fields={builderFields}
        query={draft}
        onQueryChange={(query) => {
          setDraft(query);
          const next = fromBuilderQuery(query, definitions);
          if (isValidFilterExpression(next, definitions)) onChange(next);
        }}
        combinators={[
          { name: 'and', label: messages.filterAnd },
          { name: 'or', label: messages.filterOr },
        ]}
        getOperators={(field) => {
          const definition = definitions.get(String(field));
          return definition
            ? filterOperatorsFor(definition).map((operator) => ({
                name: operator,
                value: operator,
                label: messages.filterOperators[operator],
              }))
            : [];
        }}
        getValueEditorType={(field, operator) => {
          const definition = definitions.get(String(field));
          if (!definition) return 'text';
          if (definition.kind === 'boolean') return 'select';
          if (
            (definition.kind === 'singleChoice' && (operator === 'in' || operator === 'notIn')) ||
            definition.kind === 'multiChoice'
          ) {
            return 'multiselect';
          }
          return definition.kind === 'singleChoice' ? 'select' : 'text';
        }}
        getInputType={(field) => inputTypeFor(definitions.get(String(field)))}
        getValues={(field) => {
          const definition = definitions.get(String(field));
          if (definition?.kind === 'boolean') {
            return [
              { name: 'true', value: 'true', label: messages.trueValue },
              { name: 'false', value: 'false', label: messages.falseValue },
            ];
          }
          return definition && 'options' in definition
            ? definition.options.map((option) => ({
                name: option.value,
                value: option.value,
                label: option.label,
              }))
            : [];
        }}
        getDefaultOperator={(field) => {
          const definition = definitions.get(String(field));
          return definition ? filterOperatorsFor(definition)[0] : 'eq';
        }}
        getDefaultValue={() => ''}
        listsAsArrays
        controlElements={{
          actionElement: ShadcnQueryBuilderAction,
          valueEditor: ShadcnQueryBuilderValueEditor,
          valueSelector: ShadcnQueryBuilderSelector,
        }}
        controlClassnames={{
          queryBuilder: 'grid gap-2',
          ruleGroup: 'grid gap-2 rounded-sm border border-border bg-muted/20 p-2',
          header: 'flex flex-wrap items-center gap-2',
          body: 'grid gap-2',
          rule: 'grid gap-2 rounded-sm border border-border bg-card p-2 sm:grid-cols-2 lg:grid-cols-4',
          fields: 'w-full',
          operators: 'w-full',
          value: 'w-full',
        }}
        translations={{
          fields: {
            title: messages.selectFilterField,
            placeholderName: messages.selectFilterField,
          },
          operators: {
            title: messages.selectFilterOperator,
            placeholderName: messages.selectFilterOperator,
          },
          values: {
            title: messages.selectFilterValue,
            placeholderName: messages.selectFilterValue,
          },
          addRule: { label: messages.addCondition, title: messages.addCondition },
          addGroup: { label: messages.addFilterGroup, title: messages.addFilterGroup },
          removeRule: { label: messages.removeCondition, title: messages.removeCondition },
          removeGroup: { label: messages.removeFilterGroup, title: messages.removeFilterGroup },
        }}
      />
      {!valid ? (
        <p role="alert" className="text-sm text-destructive">
          {messages.filterIncomplete}
        </p>
      ) : null}
    </div>
  );
}

function toBuilderQuery(group: DataTableFilterGroup): RuleGroupType {
  return {
    id: group.id,
    combinator: group.combinator,
    rules: group.items.map((item) =>
      'items' in item
        ? toBuilderQuery(item)
        : {
            id: item.id,
            field: item.fieldId,
            operator: item.operator,
            value: item.value,
          },
    ),
  };
}

function fromBuilderQuery<TData>(
  group: RuleGroupType,
  definitions: ReadonlyMap<string, DataTableFilterDefinition<TData>>,
): DataTableFilterGroup {
  return {
    id: String(group.id ?? crypto.randomUUID()),
    combinator: group.combinator === 'or' ? 'or' : 'and',
    items: group.rules.map((item) =>
      'rules' in item
        ? fromBuilderQuery(item, definitions)
        : fromBuilderRule(item, definitions.get(String(item.field))),
    ),
  };
}

function fromBuilderRule<TData>(rule: RuleType, definition?: DataTableFilterDefinition<TData>) {
  return {
    id: String(rule.id ?? crypto.randomUUID()),
    fieldId: String(rule.field),
    operator: rule.operator as DataTableFilterOperator,
    value: coerceValue(rule.value, definition, rule.operator as DataTableFilterOperator),
  };
}

function coerceValue<TData>(
  value: unknown,
  definition: DataTableFilterDefinition<TData> | undefined,
  operator: DataTableFilterOperator,
) {
  if (operator === 'isEmpty' || operator === 'isNotEmpty') return null;
  if (definition?.kind === 'number') {
    if (Array.isArray(value)) return value.map((item) => String(item));
    return value === '' ? '' : Number(value);
  }
  if (definition?.kind === 'boolean') return String(value) === 'true';
  return Array.isArray(value) ? value.map(String) : String(value ?? '');
}

function inputTypeFor<TData>(definition?: DataTableFilterDefinition<TData>) {
  if (definition?.kind === 'number') return 'number' as const;
  if (definition?.kind === 'date') return 'date' as const;
  if (definition?.kind === 'dateTime') return 'datetime-local' as const;
  return 'text' as const;
}
