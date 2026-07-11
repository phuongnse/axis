export const ruleDefinitionKeys = {
  required: 'field.required',
  numericRange: 'field.numeric_range',
  decimalPrecision: 'field.decimal_precision',
  dateRange: 'field.date_range',
  dateTimeRange: 'field.datetime_range',
  textLength: 'field.text_length',
  textPattern: 'field.text_pattern',
  textFormat: 'field.text_format',
  choiceSelectionCount: 'field.choice_selection_count',
} as const;

export const ruleDefinitionOrder = [
  ruleDefinitionKeys.required,
  ruleDefinitionKeys.numericRange,
  ruleDefinitionKeys.decimalPrecision,
  ruleDefinitionKeys.dateRange,
  ruleDefinitionKeys.dateTimeRange,
  ruleDefinitionKeys.textLength,
  ruleDefinitionKeys.textPattern,
  ruleDefinitionKeys.textFormat,
  ruleDefinitionKeys.choiceSelectionCount,
] as const;

export const fieldTypeOrder = [
  'Text',
  'Integer',
  'Decimal',
  'Date',
  'DateTime',
  'Boolean',
  'Choice',
] as const;

export type RuleDefinitionKey = (typeof ruleDefinitionKeys)[keyof typeof ruleDefinitionKeys];

export function fieldTypeTranslationKey(fieldType: string): string {
  return `businessObjects.fieldType${fieldType}`;
}

export function ruleNameTranslationKey(definitionKey: string | undefined): string | undefined {
  const segment = definitionKey ? ruleTranslationSegment(definitionKey) : undefined;
  return segment ? `rules.rule.${segment}.name` : undefined;
}

export function ruleDescriptionTranslationKey(
  definitionKey: string | undefined,
): string | undefined {
  const segment = definitionKey ? ruleTranslationSegment(definitionKey) : undefined;
  return segment ? `rules.rule.${segment}.description` : undefined;
}

export function ruleSetupTranslationKey(definitionKey: string | undefined): string | undefined {
  const segment = definitionKey ? ruleTranslationSegment(definitionKey) : undefined;
  return segment ? `rules.setup.${segment}` : undefined;
}

export function compareRuleDefinitions(
  left: { definitionKey?: string },
  right: { definitionKey?: string },
): number {
  const leftIndex = ruleDefinitionOrder.indexOf(left.definitionKey as RuleDefinitionKey);
  const rightIndex = ruleDefinitionOrder.indexOf(right.definitionKey as RuleDefinitionKey);
  if (leftIndex >= 0 && rightIndex >= 0) return leftIndex - rightIndex;
  if (leftIndex >= 0) return -1;
  if (rightIndex >= 0) return 1;
  return (left.definitionKey ?? '').localeCompare(right.definitionKey ?? '');
}

export function compareFieldTypes(left: string, right: string): number {
  const leftIndex = fieldTypeOrder.indexOf(left as (typeof fieldTypeOrder)[number]);
  const rightIndex = fieldTypeOrder.indexOf(right as (typeof fieldTypeOrder)[number]);
  if (leftIndex >= 0 && rightIndex >= 0) return leftIndex - rightIndex;
  if (leftIndex >= 0) return -1;
  if (rightIndex >= 0) return 1;
  return left.localeCompare(right);
}

function ruleTranslationSegment(definitionKey: string): string | undefined {
  const match = Object.entries(ruleDefinitionKeys).find(([, key]) => key === definitionKey);
  return match?.[0];
}
