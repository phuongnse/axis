import type * as ApiTypes from '@/lib/api-generated';

export function toDraftInputs(
  inputs: (ApiTypes.RuleInputDefinitionDto | ApiTypes.RuleDraftInputDefinitionDto)[],
): ApiTypes.RuleDraftInputDefinitionDto[] {
  return inputs.map((input) => ({
    key: input.key ?? '',
    label: input.label ?? '',
    types: input.types ?? [],
    isRequired: input.isRequired ?? false,
    allowMultiple: input.allowMultiple ?? false,
    allowedValues: input.allowedValues ?? [],
  }));
}
