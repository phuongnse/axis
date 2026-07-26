export const fieldTypeOrder = [
  'Text',
  'Integer',
  'Decimal',
  'Date',
  'DateTime',
  'Boolean',
  'Choice',
] as const;

export function fieldTypeTranslationKey(fieldType: string): string {
  return `businessObjects.fieldType${fieldType}`;
}

export function compareFieldTypes(left: string, right: string): number {
  const leftIndex = fieldTypeOrder.indexOf(left as (typeof fieldTypeOrder)[number]);
  const rightIndex = fieldTypeOrder.indexOf(right as (typeof fieldTypeOrder)[number]);
  if (leftIndex >= 0 && rightIndex >= 0) return leftIndex - rightIndex;
  if (leftIndex >= 0) return -1;
  if (rightIndex >= 0) return 1;
  return left.localeCompare(right);
}
