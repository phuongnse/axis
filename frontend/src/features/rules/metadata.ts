export const fieldRuleDefinitionKeys = {
  required: 'field.required',
  numericRange: 'field.numeric_range',
  dateRange: 'field.date_range',
  textLength: 'field.text_length',
  textPattern: 'field.text_pattern',
  singleSelectOptions: 'field.single_select_options',
} as const;

export const fieldRuleDefinitionOrder = [
  fieldRuleDefinitionKeys.required,
  fieldRuleDefinitionKeys.numericRange,
  fieldRuleDefinitionKeys.dateRange,
  fieldRuleDefinitionKeys.textLength,
  fieldRuleDefinitionKeys.textPattern,
  fieldRuleDefinitionKeys.singleSelectOptions,
] as const;

export const fieldTypeOrder = [
  'Text',
  'Integer',
  'Decimal',
  'Date',
  'Boolean',
  'SingleSelect',
] as const;

export type FieldRuleDefinitionKey =
  (typeof fieldRuleDefinitionKeys)[keyof typeof fieldRuleDefinitionKeys];

export function fieldTypeTranslationKey(fieldType: string): string {
  return `objects.fieldType${fieldType}`;
}

export function fieldRuleNameTranslationKey(definitionKey: string | undefined): string | undefined {
  switch (definitionKey) {
    case fieldRuleDefinitionKeys.required:
      return 'rules.rule.required.name';
    case fieldRuleDefinitionKeys.numericRange:
      return 'rules.rule.numericRange.name';
    case fieldRuleDefinitionKeys.dateRange:
      return 'rules.rule.dateRange.name';
    case fieldRuleDefinitionKeys.textLength:
      return 'rules.rule.textLength.name';
    case fieldRuleDefinitionKeys.textPattern:
      return 'rules.rule.textPattern.name';
    case fieldRuleDefinitionKeys.singleSelectOptions:
      return 'rules.rule.singleSelectOptions.name';
    default:
      return undefined;
  }
}

export function fieldRuleDescriptionTranslationKey(
  definitionKey: string | undefined,
): string | undefined {
  switch (definitionKey) {
    case fieldRuleDefinitionKeys.required:
      return 'rules.rule.required.description';
    case fieldRuleDefinitionKeys.numericRange:
      return 'rules.rule.numericRange.description';
    case fieldRuleDefinitionKeys.dateRange:
      return 'rules.rule.dateRange.description';
    case fieldRuleDefinitionKeys.textLength:
      return 'rules.rule.textLength.description';
    case fieldRuleDefinitionKeys.textPattern:
      return 'rules.rule.textPattern.description';
    case fieldRuleDefinitionKeys.singleSelectOptions:
      return 'rules.rule.singleSelectOptions.description';
    default:
      return undefined;
  }
}

export function fieldRuleCategoryTranslationKey(
  definitionKey: string | undefined,
): string | undefined {
  switch (definitionKey) {
    case fieldRuleDefinitionKeys.required:
      return 'rules.categoryContract';
    case fieldRuleDefinitionKeys.numericRange:
    case fieldRuleDefinitionKeys.dateRange:
      return 'rules.categoryRange';
    case fieldRuleDefinitionKeys.textLength:
    case fieldRuleDefinitionKeys.textPattern:
      return 'rules.categoryText';
    case fieldRuleDefinitionKeys.singleSelectOptions:
      return 'rules.categoryOptions';
    default:
      return undefined;
  }
}

export function compareFieldRuleDefinitions(
  left: { definitionKey?: string },
  right: { definitionKey?: string },
): number {
  const leftIndex = fieldRuleDefinitionOrder.indexOf(left.definitionKey as FieldRuleDefinitionKey);
  const rightIndex = fieldRuleDefinitionOrder.indexOf(
    right.definitionKey as FieldRuleDefinitionKey,
  );

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
