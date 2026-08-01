import type * as ApiTypes from '@/lib/api-generated';

type Condition = ApiTypes.RuleConditionNodeDto;
type Operand = ApiTypes.RuleOperandDto;

export function toDraftInputs(
  inputs: (ApiTypes.RuleInputDefinitionDto | ApiTypes.RuleDraftInputDefinitionDto)[],
): ApiTypes.RuleDraftInputDefinitionDto[] {
  return inputs.map((input) => ({
    label: input.label ?? '',
    types: input.types ?? [],
    isRequired: input.isRequired ?? false,
    allowMultiple: input.allowMultiple ?? false,
    allowedValues: input.allowedValues ?? [],
  }));
}

export function replaceConditionInputReferences(
  condition: Condition | null | undefined,
  replacements: ReadonlyMap<string, string>,
): Condition | null {
  if (!condition) return null;
  return {
    ...condition,
    left: replaceOperandReference(condition.left, replacements),
    right: replaceOperandReference(condition.right, replacements),
    children: (condition.children ?? []).map(
      (child) => replaceConditionInputReferences(child, replacements) ?? child,
    ),
  };
}

function replaceOperandReference(
  operand: Operand | undefined,
  replacements: ReadonlyMap<string, string>,
): Operand | undefined {
  if (!operand) return undefined;
  return {
    ...operand,
    reference:
      operand.kind === 'Input' && operand.reference
        ? (replacements.get(operand.reference) ?? operand.reference)
        : operand.reference,
    arguments: (operand.arguments ?? []).map(
      (argument) => replaceOperandReference(argument, replacements) ?? argument,
    ),
  };
}
