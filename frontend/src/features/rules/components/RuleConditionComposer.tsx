import { Plus, Trash2 } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { AsyncContent } from '@/components/shared/AsyncContent';
import { Button } from '@/components/ui/button';
import { Field, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import type * as ApiTypes from '@/lib/api-generated';
import { referenceLabel } from '@/lib/reference-metadata';
import { valueTypeLabel } from '../reference';
import { RuleExpressionGuide } from './RuleExpressionGuide';

type Condition = ApiTypes.RuleConditionNodeDto;
type Operand = ApiTypes.RuleOperandDto;
type DraftInput = ApiTypes.RuleDraftInputDefinitionDto;
type Language = ApiTypes.RuleExpressionLanguageDto;
type ValueType = ApiTypes.RuleValueType;
type Cardinality = ApiTypes.RuleExpressionCardinality;
type Shape = ApiTypes.RuleExpressionValueShapeDto;
type SourceKind = 'input' | 'literal' | 'calculation';
type NodePath = number[];
type OperandShape = { types: ValueType[]; cardinality: Cardinality };

const valueTypes: ValueType[] = ['Text', 'Integer', 'Decimal', 'Date', 'DateTime', 'Boolean'];

export function RuleConditionComposer({
  condition,
  definitionKey,
  inputs,
  language,
  onChange,
}: {
  condition: Condition | null;
  definitionKey?: string;
  inputs: DraftInput[];
  language: Language | undefined;
  onChange: (condition: Condition | null) => void;
}) {
  const { t } = useTranslation();
  const usableInputs = inputs.filter(
    (input) => Boolean(input.key?.trim()) && Boolean(input.label?.trim()),
  );
  const canStart = Boolean(language && usableInputs.length > 0);

  if (!language) {
    return (
      <section aria-labelledby="rule-condition-composer-title" className="space-y-2">
        <h3 id="rule-condition-composer-title" className="text-sm font-semibold">
          {t('rules.expressionEditorTitle')}
        </h3>
        <AsyncContent pending pendingLabel={t('rules.referenceLoading')}>
          <span />
        </AsyncContent>
      </section>
    );
  }

  const create = () => createClause(usableInputs, language);
  const addCondition = () => {
    const next = create();
    if (next) onChange(condition ? appendToRoot(condition, next, language) : next);
  };
  const addGroup = () => {
    const child = create();
    if (child)
      onChange(condition ? appendToRoot(condition, createGroup('All', child), language) : child);
  };

  return (
    <section aria-labelledby="rule-condition-composer-title" className="space-y-3">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h3 id="rule-condition-composer-title" className="text-sm font-semibold">
            {t('rules.expressionEditorTitle')}
          </h3>
          <p className="text-xs text-muted-foreground">{t('rules.conditionsHelp')}</p>
        </div>
        <RuleExpressionGuide
          expressionLanguageVersion={language.version}
          definitionKey={definitionKey}
          inputs={usableInputs}
        />
      </div>
      {usableInputs.length === 0 ? (
        <p role="status" className="text-sm text-muted-foreground">
          {t('rules.guidedNoFields')}
        </p>
      ) : condition ? (
        <ConditionComposerNode
          root={condition}
          node={condition}
          path={[]}
          inputs={usableInputs}
          language={language}
          onChange={onChange}
        />
      ) : (
        <p className="text-sm text-muted-foreground">{t('rules.guidedEmpty')}</p>
      )}
      <div className="flex flex-wrap gap-2">
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={!canStart}
          onClick={addCondition}
        >
          <Plus data-icon="inline-start" />
          {t('rules.addCondition')}
        </Button>
        <Button type="button" variant="outline" size="sm" disabled={!canStart} onClick={addGroup}>
          <Plus data-icon="inline-start" />
          {t('rules.addGroup')}
        </Button>
      </div>
    </section>
  );
}

function ConditionComposerNode({
  root,
  node,
  path,
  inputs,
  language,
  onChange,
}: {
  root: Condition;
  node: Condition;
  path: NodePath;
  inputs: DraftInput[];
  language: Language;
  onChange: (condition: Condition | null) => void;
}) {
  return node.logicalOperator ? (
    <ConditionGroup
      root={root}
      node={node}
      path={path}
      inputs={inputs}
      language={language}
      onChange={onChange}
    />
  ) : (
    <ConditionClause
      root={root}
      node={node}
      path={path}
      inputs={inputs}
      language={language}
      onChange={onChange}
    />
  );
}

function ConditionGroup({
  root,
  node,
  path,
  inputs,
  language,
  onChange,
}: {
  root: Condition;
  node: Condition;
  path: NodePath;
  inputs: DraftInput[];
  language: Language;
  onChange: (condition: Condition | null) => void;
}) {
  const { t, i18n } = useTranslation();
  const operator = node.logicalOperator as ApiTypes.RuleLogicalOperator;
  const definition = (language.logicalOperators ?? []).find(
    (candidate) => candidate.operator === operator,
  );
  const children = node.children ?? [];
  const mayAdd =
    definition?.maximumChildren === null ||
    definition?.maximumChildren === undefined ||
    children.length < definition.maximumChildren;
  const update = (next: Condition) => onChange(updateAtPath(root, path, next));
  const addClause = () => {
    const child = createClause(inputs, language);
    if (child) update({ ...node, children: [...children, child] });
  };
  const addGroup = () => {
    const child = createClause(inputs, language);
    if (child) update({ ...node, children: [...children, createGroup('All', child)] });
  };

  return (
    <fieldset className="space-y-3 rounded-lg border p-3" data-slot="rule-condition-composer-group">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <Field className="w-full sm:w-auto">
          <FieldLabel htmlFor={pathId(path, 'group')}>{t('rules.conditionJoin')}</FieldLabel>
          <Select
            value={operator}
            onValueChange={(value) =>
              update({ ...node, logicalOperator: value as ApiTypes.RuleLogicalOperator })
            }
          >
            <SelectTrigger id={pathId(path, 'group')} aria-label={t('rules.conditionJoin')}>
              <SelectValue>{groupLabel(operator, language, i18n.language, t)}</SelectValue>
            </SelectTrigger>
            <SelectContent>
              {(language.logicalOperators ?? []).map((candidate) => {
                if (!candidate.operator) return null;
                const disabled = candidate.operator === 'Not' && children.length > 1;
                return (
                  <SelectItem
                    key={candidate.operator}
                    value={candidate.operator}
                    disabled={disabled}
                  >
                    {groupLabel(candidate.operator, language, i18n.language, t)}
                  </SelectItem>
                );
              })}
            </SelectContent>
          </Select>
        </Field>
        <Button
          type="button"
          variant="ghost"
          size="icon"
          aria-label={t('rules.removeGroup')}
          onClick={() => onChange(removeAtPath(root, path))}
        >
          <Trash2 />
        </Button>
      </div>
      <div className="space-y-3 border-l pl-3">
        {children.map((child, index) => (
          <ConditionComposerNode
            key={child.nodeId ?? `${path.join('-')}-${index}`}
            root={root}
            node={child}
            path={[...path, index]}
            inputs={inputs}
            language={language}
            onChange={onChange}
          />
        ))}
      </div>
      {mayAdd ? (
        <div className="flex flex-wrap gap-2">
          <Button type="button" variant="outline" size="sm" onClick={addClause}>
            <Plus data-icon="inline-start" />
            {t('rules.addCondition')}
          </Button>
          <Button type="button" variant="outline" size="sm" onClick={addGroup}>
            <Plus data-icon="inline-start" />
            {t('rules.addGroup')}
          </Button>
        </div>
      ) : null}
    </fieldset>
  );
}

function ConditionClause({
  root,
  node,
  path,
  inputs,
  language,
  onChange,
}: {
  root: Condition;
  node: Condition;
  path: NodePath;
  inputs: DraftInput[];
  language: Language;
  onChange: (condition: Condition | null) => void;
}) {
  const { t, i18n } = useTranslation();
  const left = node.left ?? inputOperand(inputs[0]);
  const leftShape = operandShape(left, inputs, language);
  const operators = (language.operators ?? []).filter(
    (candidate) => leftShape && matchesAnyShape(candidate.leftShapes ?? [], leftShape),
  );
  const operator =
    operators.find((candidate) => candidate.operator === node.predicateOperator) ?? operators[0];
  const rightShapes = operator ? compatibleRightShapes(operator, leftShape) : [];
  const right = rightShapes.length
    ? node.right && operandMatchesShapes(node.right, rightShapes, inputs, language)
      ? node.right
      : createOperandFor(rightShapes, inputs, language, true)
    : undefined;
  const normalized = {
    ...node,
    left,
    predicateOperator: operator?.operator,
    right: right ?? undefined,
    children: [],
  };
  const update = (next: Condition) => onChange(updateAtPath(root, path, next));

  return (
    <fieldset
      className="space-y-3 rounded-lg border p-3"
      data-slot="rule-condition-composer-clause"
    >
      <div className="flex justify-end">
        <Button
          type="button"
          variant="ghost"
          size="icon"
          aria-label={t('rules.removeCondition')}
          onClick={() => onChange(removeAtPath(root, path))}
        >
          <Trash2 />
        </Button>
      </div>
      <div className="grid gap-3 lg:grid-cols-3">
        <ValueComposer
          label={t('rules.conditionCheck')}
          operand={left}
          acceptedShapes={anyShapes(language)}
          inputs={inputs}
          language={language}
          path={pathId(path, 'left')}
          allowFixedValue={false}
          onChange={(next) =>
            update(reconcileClause({ ...normalized, left: next }, inputs, language))
          }
        />
        <Field>
          <FieldLabel htmlFor={pathId(path, 'operator')}>
            {t('rules.conditionComparison')}
          </FieldLabel>
          <Select
            value={operator?.operator ?? ''}
            onValueChange={(value) => {
              const selected = operators.find((candidate) => candidate.operator === value);
              if (selected)
                update(
                  reconcileClause(
                    { ...normalized, predicateOperator: selected.operator },
                    inputs,
                    language,
                  ),
                );
            }}
          >
            <SelectTrigger
              id={pathId(path, 'operator')}
              aria-label={t('rules.conditionComparison')}
            >
              <SelectValue>
                {operator
                  ? referenceLabel(operator.documentation, i18n.language, operator.operator)
                  : t('rules.selectValue')}
              </SelectValue>
            </SelectTrigger>
            <SelectContent>
              {operators.map((candidate) =>
                candidate.operator ? (
                  <SelectItem key={candidate.operator} value={candidate.operator}>
                    {referenceLabel(candidate.documentation, i18n.language, candidate.operator)}
                  </SelectItem>
                ) : null,
              )}
            </SelectContent>
          </Select>
        </Field>
        {rightShapes.length > 0 && right ? (
          <ValueComposer
            label={t('rules.compareWith')}
            operand={right}
            acceptedShapes={rightShapes}
            inputs={inputs}
            language={language}
            path={pathId(path, 'right')}
            allowFixedValue
            onChange={(next) => update({ ...normalized, right: next })}
          />
        ) : (
          <Field>
            <FieldLabel>{t('rules.compareWith')}</FieldLabel>
            <p className="text-sm text-muted-foreground">{t('rules.unaryCondition')}</p>
          </Field>
        )}
      </div>
    </fieldset>
  );
}

function ValueComposer({
  label,
  operand,
  acceptedShapes,
  inputs,
  language,
  path,
  allowFixedValue,
  depth = 0,
  onChange,
}: {
  label: string;
  operand: Operand;
  acceptedShapes: Shape[];
  inputs: DraftInput[];
  language: Language;
  path: string;
  allowFixedValue: boolean;
  depth?: number;
  onChange: (operand: Operand) => void;
}) {
  const { t, i18n } = useTranslation();
  const source = sourceKind(operand);
  const compatibleInputs = inputs.filter((input) => inputMatchesShapes(input, acceptedShapes));
  const calculations = functionsForShapes(acceptedShapes, inputs, language, depth);
  const canUseFixedValue =
    allowFixedValue && literalTypesForShapes(acceptedShapes, 'Scalar').length > 0;

  const selectSource = (next: SourceKind) => {
    const value = createOperandForSource(next, acceptedShapes, inputs, language, depth);
    if (value) onChange(value);
  };

  return (
    <div className="space-y-2">
      <Field>
        <FieldLabel htmlFor={`${path}-source`}>{label}</FieldLabel>
        <Select value={source} onValueChange={(value) => selectSource(value as SourceKind)}>
          <SelectTrigger id={`${path}-source`} aria-label={label}>
            <SelectValue>{sourceLabel(source, language, i18n.language, t)}</SelectValue>
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="input" disabled={compatibleInputs.length === 0}>
              {sourceLabel('input', language, i18n.language, t)}
            </SelectItem>
            {allowFixedValue ? (
              <SelectItem value="literal" disabled={!canUseFixedValue}>
                {sourceLabel('literal', language, i18n.language, t)}
              </SelectItem>
            ) : null}
            <SelectItem value="calculation" disabled={calculations.length === 0}>
              {sourceLabel('calculation', language, i18n.language, t)}
            </SelectItem>
          </SelectContent>
        </Select>
      </Field>
      {source === 'input' ? (
        <Field>
          <FieldLabel htmlFor={`${path}-input`}>{t('rules.chooseInput')}</FieldLabel>
          <Select
            value={operand.reference ?? ''}
            onValueChange={(reference) => onChange({ kind: 'Input', reference, arguments: [] })}
          >
            <SelectTrigger id={`${path}-input`} aria-label={`${label}: ${t('rules.chooseInput')}`}>
              <SelectValue>{inputLabel(operand.reference, inputs)}</SelectValue>
            </SelectTrigger>
            <SelectContent>
              {compatibleInputs.map((input) => (
                <SelectItem key={input.key} value={input.key ?? ''}>
                  {input.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </Field>
      ) : null}
      {source === 'literal' && operand.literal ? (
        <FixedValueComposer
          label={label}
          value={operand.literal}
          acceptedShapes={acceptedShapes}
          language={language}
          path={path}
          onChange={(literal) => onChange({ kind: 'Literal', literal, arguments: [] })}
        />
      ) : null}
      {source === 'calculation' && operand.function ? (
        <CalculationComposer
          label={label}
          operand={operand}
          acceptedShapes={acceptedShapes}
          inputs={inputs}
          language={language}
          path={path}
          depth={depth}
          onChange={onChange}
        />
      ) : null}
    </div>
  );
}

function CalculationComposer({
  label,
  operand,
  acceptedShapes,
  inputs,
  language,
  path,
  depth,
  onChange,
}: {
  label: string;
  operand: Operand;
  acceptedShapes: Shape[];
  inputs: DraftInput[];
  language: Language;
  path: string;
  depth: number;
  onChange: (operand: Operand) => void;
}) {
  const { t, i18n } = useTranslation();
  const calculations = functionsForShapes(acceptedShapes, inputs, language, depth);
  const selected =
    calculations.find((candidate) => candidate.function === operand.function) ?? calculations[0];
  if (!selected?.function) return null;
  const argumentsForCalculation =
    operand.function === selected.function
      ? (operand.arguments ?? [])
      : (createCalculationOperand(selected, inputs, language, depth).arguments ?? []);
  return (
    <div className="space-y-3 border-l pl-3">
      <Field>
        <FieldLabel htmlFor={`${path}-calculation`}>{t('rules.chooseCalculation')}</FieldLabel>
        <Select
          value={selected.function}
          onValueChange={(value) => {
            const next = calculations.find((candidate) => candidate.function === value);
            if (next) onChange(createCalculationOperand(next, inputs, language, depth));
          }}
        >
          <SelectTrigger
            id={`${path}-calculation`}
            aria-label={`${label}: ${t('rules.chooseCalculation')}`}
          >
            <SelectValue>
              {referenceLabel(selected.documentation, i18n.language, selected.function)}
            </SelectValue>
          </SelectTrigger>
          <SelectContent>
            {calculations.map((candidate) =>
              candidate.function ? (
                <SelectItem key={candidate.function} value={candidate.function}>
                  {referenceLabel(candidate.documentation, i18n.language, candidate.function)}
                </SelectItem>
              ) : null,
            )}
          </SelectContent>
        </Select>
      </Field>
      {(selected.parameters ?? []).map((parameter, index, parameters) => {
        const accepted = parameterShapes(parameter);
        const argument =
          argumentsForCalculation[index] ??
          createOperandFor(accepted, inputs, language, true, depth + 1);
        if (!argument) return null;
        const parameterKey = `${selected.function}-${parameter.cardinality}-${(parameter.acceptedTypes ?? []).join('-')}-${parameters.slice(0, index).filter((candidate) => candidate.cardinality === parameter.cardinality && (candidate.acceptedTypes ?? []).join('-') === (parameter.acceptedTypes ?? []).join('-')).length}`;
        return (
          <ValueComposer
            key={`${path}-argument-${parameterKey}`}
            label={t('rules.calculationInput', { index: index + 1 })}
            operand={argument}
            acceptedShapes={accepted}
            inputs={inputs}
            language={language}
            path={`${path}-argument-${index}`}
            allowFixedValue
            depth={depth + 1}
            onChange={(next) => {
              const nextArguments = argumentsForCalculation.map((argument, argumentIndex) =>
                argumentIndex === index ? next : argument,
              );
              onChange({ kind: 'Function', function: selected.function, arguments: nextArguments });
            }}
          />
        );
      })}
    </div>
  );
}

function FixedValueComposer({
  label,
  value,
  acceptedShapes,
  language,
  path,
  onChange,
}: {
  label: string;
  value: ApiTypes.RuleValueDto;
  acceptedShapes: Shape[];
  language: Language;
  path: string;
  onChange: (value: ApiTypes.RuleValueDto) => void;
}) {
  const { t, i18n } = useTranslation();
  const type = value.type ?? literalTypesForShapes(acceptedShapes, 'Scalar')[0] ?? 'Text';
  const types = literalTypesForShapes(acceptedShapes, 'Scalar');
  const current = value.values?.[0] ?? defaultLiteralValue(type);
  const update = (next: string) => onChange({ type, values: [next] });
  return (
    <div className="grid gap-2 sm:grid-cols-2">
      <Field>
        <FieldLabel htmlFor={`${path}-type`}>{t('rules.valueType')}</FieldLabel>
        <Select
          value={type}
          onValueChange={(next) =>
            onChange({ type: next as ValueType, values: [defaultLiteralValue(next as ValueType)] })
          }
        >
          <SelectTrigger id={`${path}-type`} aria-label={`${label}: ${t('rules.valueType')}`}>
            <SelectValue>{valueTypeLabel(language, type, i18n.language)}</SelectValue>
          </SelectTrigger>
          <SelectContent>
            {types.map((candidate) => (
              <SelectItem key={candidate} value={candidate}>
                {valueTypeLabel(language, candidate, i18n.language)}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </Field>
      <Field>
        <FieldLabel htmlFor={`${path}-value`}>{t('rules.enterValue')}</FieldLabel>
        {type === 'Boolean' ? (
          <Select value={current} onValueChange={(next) => next && update(next)}>
            <SelectTrigger id={`${path}-value`} aria-label={`${label}: ${t('rules.enterValue')}`}>
              <SelectValue>{current}</SelectValue>
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="true">{t('rules.booleanTrue')}</SelectItem>
              <SelectItem value="false">{t('rules.booleanFalse')}</SelectItem>
            </SelectContent>
          </Select>
        ) : (
          <Input
            id={`${path}-value`}
            type={inputType(type)}
            value={toInputValue(type, current)}
            onChange={(event) => update(fromInputValue(type, event.target.value))}
          />
        )}
      </Field>
    </div>
  );
}

function sourceKind(operand: Operand): SourceKind {
  if (operand.kind === 'Literal') return 'literal';
  if (operand.kind === 'Function') return 'calculation';
  return 'input';
}

function sourceLabel(
  source: SourceKind,
  language: Language,
  locale: string,
  t: ReturnType<typeof useTranslation>['t'],
) {
  const kind = source === 'literal' ? 'Literal' : source === 'calculation' ? 'Function' : 'Input';
  const definition = (language.operandKinds ?? []).find((candidate) => candidate.kind === kind);
  const fallback =
    source === 'literal'
      ? t('rules.fixedValue')
      : source === 'calculation'
        ? t('rules.calculatedValue')
        : t('rules.ruleInput');
  return referenceLabel(definition?.documentation, locale, fallback);
}

function groupLabel(
  operator: ApiTypes.RuleLogicalOperator,
  language: Language,
  locale: string,
  t: ReturnType<typeof useTranslation>['t'],
) {
  const definition = (language.logicalOperators ?? []).find(
    (candidate) => candidate.operator === operator,
  );
  const fallback =
    operator === 'Any'
      ? t('rules.conditionAny')
      : operator === 'Not'
        ? t('rules.conditionNot')
        : t('rules.conditionAll');
  return referenceLabel(definition?.documentation, locale, fallback);
}

function createClause(inputs: DraftInput[], language: Language): Condition | null {
  const input = inputs[0];
  if (!input) return null;
  const left = inputOperand(input);
  const shape = operandShape(left, inputs, language);
  const operator = (language.operators ?? []).find(
    (candidate) => shape && matchesAnyShape(candidate.leftShapes ?? [], shape),
  );
  if (!operator?.operator) return null;
  const rightShapes = compatibleRightShapes(operator, shape);
  const right = rightShapes.length
    ? createOperandFor(rightShapes, inputs, language, true)
    : undefined;
  return {
    nodeId: newNodeId(),
    predicateOperator: operator.operator,
    left,
    right: right ?? undefined,
    children: [],
  };
}

function createGroup(operator: ApiTypes.RuleLogicalOperator, child: Condition): Condition {
  return { nodeId: newNodeId(), logicalOperator: operator, children: [child] };
}

function appendToRoot(root: Condition, child: Condition, language: Language): Condition {
  if (root.logicalOperator && root.logicalOperator !== 'Not') {
    const definition = (language.logicalOperators ?? []).find(
      (candidate) => candidate.operator === root.logicalOperator,
    );
    const children = root.children ?? [];
    if (
      definition?.maximumChildren === null ||
      definition?.maximumChildren === undefined ||
      children.length < definition.maximumChildren
    ) {
      return { ...root, children: [...children, child] };
    }
  }
  return { nodeId: newNodeId(), logicalOperator: 'All', children: [root, child] };
}

function reconcileClause(node: Condition, inputs: DraftInput[], language: Language): Condition {
  const left = node.left;
  if (!left) return node;
  const leftShape = operandShape(left, inputs, language);
  const operators = (language.operators ?? []).filter(
    (candidate) => leftShape && matchesAnyShape(candidate.leftShapes ?? [], leftShape),
  );
  const operator =
    operators.find((candidate) => candidate.operator === node.predicateOperator) ?? operators[0];
  if (!operator?.operator) return node;
  const rightShapes = compatibleRightShapes(operator, leftShape);
  const right = rightShapes.length
    ? node.right && operandMatchesShapes(node.right, rightShapes, inputs, language)
      ? node.right
      : createOperandFor(rightShapes, inputs, language, true)
    : undefined;
  return { ...node, predicateOperator: operator.operator, right: right ?? undefined, children: [] };
}

function createOperandForSource(
  source: SourceKind,
  acceptedShapes: Shape[],
  inputs: DraftInput[],
  language: Language,
  depth: number,
): Operand | null {
  if (source === 'input') {
    const input = inputs.find((candidate) => inputMatchesShapes(candidate, acceptedShapes));
    return input ? inputOperand(input) : null;
  }
  if (source === 'literal') {
    const type = literalTypesForShapes(acceptedShapes, 'Scalar')[0];
    return type ? literalOperand(type) : null;
  }
  const calculation = functionsForShapes(acceptedShapes, inputs, language, depth)[0];
  return calculation ? createCalculationOperand(calculation, inputs, language, depth) : null;
}

function createOperandFor(
  acceptedShapes: Shape[],
  inputs: DraftInput[],
  language: Language,
  preferFixedValue: boolean,
  depth = 0,
): Operand | null {
  const input = inputs.find((candidate) => inputMatchesShapes(candidate, acceptedShapes));
  const type = literalTypesForShapes(acceptedShapes, 'Scalar')[0];
  if (preferFixedValue && type) return literalOperand(type);
  if (input) return inputOperand(input);
  if (type) return literalOperand(type);
  const calculation = functionsForShapes(acceptedShapes, inputs, language, depth)[0];
  return calculation ? createCalculationOperand(calculation, inputs, language, depth) : null;
}

function createCalculationOperand(
  definition: ApiTypes.RuleExpressionFunctionDefinitionDto,
  inputs: DraftInput[],
  language: Language,
  depth: number,
): Operand {
  return {
    kind: 'Function',
    function: definition.function,
    arguments: (definition.parameters ?? []).flatMap((parameter) => {
      const operand = createOperandFor(
        parameterShapes(parameter),
        inputs,
        language,
        false,
        depth + 1,
      );
      return operand ? [operand] : [];
    }),
  };
}

function inputOperand(input: DraftInput): Operand {
  return { kind: 'Input', reference: input.key, arguments: [] };
}

function literalOperand(type: ValueType): Operand {
  return { kind: 'Literal', literal: { type, values: [defaultLiteralValue(type)] }, arguments: [] };
}

function functionsForShapes(
  acceptedShapes: Shape[],
  inputs: DraftInput[],
  language: Language,
  depth: number,
) {
  if (depth >= (language.limits?.maxDepth ?? 12) - 1) return [];
  return (language.functions ?? []).filter((definition) => {
    if (!definition.function || !definition.returnType || !definition.returnCardinality)
      return false;
    if (!matchesAnyShape(acceptedShapes, functionShape(definition))) return false;
    return (definition.parameters ?? []).every((parameter) =>
      canCreateOperand(parameterShapes(parameter), inputs),
    );
  });
}

function canCreateOperand(acceptedShapes: Shape[], inputs: DraftInput[]) {
  return (
    inputs.some((input) => inputMatchesShapes(input, acceptedShapes)) ||
    literalTypesForShapes(acceptedShapes, 'Scalar').length > 0
  );
}

function compatibleRightShapes(
  definition: ApiTypes.RulePredicateOperatorDefinitionDto,
  left: OperandShape | null,
) {
  const right = definition.rightShapes ?? [];
  return !definition.requiresMatchingTypes || !left
    ? right
    : right.filter((shape) => left.types.includes(shape.type as ValueType));
}

function operandMatchesShapes(
  operand: Operand,
  shapes: Shape[],
  inputs: DraftInput[],
  language: Language,
) {
  const shape = operandShape(operand, inputs, language);
  return shape ? matchesAnyShape(shapes, shape) : false;
}

function operandShape(
  operand: Operand,
  inputs: DraftInput[],
  language: Language,
): OperandShape | null {
  if (operand.kind === 'Input') {
    const input = inputs.find((candidate) => candidate.key === operand.reference);
    return input
      ? {
          types: (input.types ?? []).filter(isValueType),
          cardinality: input.allowMultiple ? 'Multiple' : 'Scalar',
        }
      : null;
  }
  if (operand.kind === 'Literal' && operand.literal?.type) {
    return { types: [operand.literal.type], cardinality: 'Scalar' };
  }
  if (operand.kind === 'Function') {
    const definition = (language.functions ?? []).find(
      (candidate) => candidate.function === operand.function,
    );
    return definition ? functionShape(definition) : null;
  }
  return null;
}

function functionShape(definition: ApiTypes.RuleExpressionFunctionDefinitionDto): OperandShape {
  return {
    types: definition.returnType ? [definition.returnType] : [],
    cardinality: definition.returnCardinality ?? 'Scalar',
  };
}

function inputMatchesShapes(input: DraftInput, shapes: Shape[]) {
  return matchesAnyShape(shapes, {
    types: (input.types ?? []).filter(isValueType),
    cardinality: input.allowMultiple ? 'Multiple' : 'Scalar',
  });
}

function matchesAnyShape(shapes: Shape[], operand: OperandShape) {
  return shapes.some(
    (shape) =>
      Boolean(shape.type) &&
      operand.types.includes(shape.type as ValueType) &&
      (shape.cardinality === 'Any' || shape.cardinality === operand.cardinality),
  );
}

function parameterShapes(parameter: ApiTypes.RuleExpressionFunctionParameterDto): Shape[] {
  return (parameter.acceptedTypes ?? []).map((type) => ({
    type,
    cardinality: parameter.cardinality ?? 'Scalar',
  }));
}

function literalTypesForShapes(shapes: Shape[], cardinality: Exclude<Cardinality, 'Any'>) {
  return [
    ...new Set(
      shapes
        .filter((shape) => shape.cardinality === cardinality || shape.cardinality === 'Any')
        .map((shape) => shape.type)
        .filter(isValueType),
    ),
  ];
}

function anyShapes(language: Language): Shape[] {
  const types = (language.valueTypes ?? [])
    .map((definition) => definition.type)
    .filter(isValueType);
  return (types.length ? types : valueTypes).map((type) => ({ type, cardinality: 'Any' }));
}

function isValueType(value: unknown): value is ValueType {
  return typeof value === 'string' && valueTypes.includes(value as ValueType);
}

function inputLabel(reference: string | null | undefined, inputs: DraftInput[]) {
  return inputs.find((input) => input.key === reference)?.label ?? '—';
}

function updateAtPath(root: Condition, path: NodePath, next: Condition): Condition {
  if (path.length === 0) return next;
  const [index, ...remaining] = path;
  return {
    ...root,
    children: (root.children ?? []).map((child, childIndex) =>
      childIndex === index ? updateAtPath(child, remaining, next) : child,
    ),
  };
}

function removeAtPath(root: Condition, path: NodePath): Condition | null {
  if (path.length === 0) return null;
  const parentPath = path.slice(0, -1);
  const childIndex = path[path.length - 1];
  const parent = pathAt(root, parentPath);
  if (!parent) return root;
  const children = (parent.children ?? []).filter((_, index) => index !== childIndex);
  if (children.length === 0) return removeAtPath(root, parentPath);
  return updateAtPath(root, parentPath, { ...parent, children });
}

function pathAt(root: Condition, path: NodePath): Condition | null {
  return path.reduce<Condition | null>(
    (current, index) => current?.children?.[index] ?? null,
    root,
  );
}

function pathId(path: NodePath, name: string) {
  return `rule-condition-${path.length ? path.join('-') : 'root'}-${name}`;
}

function newNodeId() {
  return `condition-${crypto.randomUUID()}`;
}

function defaultLiteralValue(type: ValueType) {
  if (type === 'Boolean') return 'false';
  if (type === 'Integer' || type === 'Decimal') return '0';
  return '';
}

function inputType(type: ValueType) {
  if (type === 'Integer' || type === 'Decimal') return 'number';
  if (type === 'Date') return 'date';
  if (type === 'DateTime') return 'datetime-local';
  return 'text';
}

function toInputValue(type: ValueType, value: string) {
  if (type !== 'DateTime') return value;
  const date = new Date(value);
  return Number.isNaN(date.valueOf()) ? '' : date.toISOString().slice(0, 16);
}

function fromInputValue(type: ValueType, value: string) {
  if (type !== 'DateTime' || !value) return value;
  return new Date(value).toISOString();
}
