import { queryOptions } from '@tanstack/react-query';
import { fetchApi } from '@/lib/api';
import type { components } from '@/lib/api-types';

export type FieldRuleDefinition = components['schemas']['FieldRuleDefinitionDto'];
export type FieldRuleParameterDefinition = components['schemas']['FieldRuleParameterDefinitionDto'];
export type FieldRuleParameterType = components['schemas']['FieldRuleParameterType'];

export const fieldRuleDefinitionStaleTimeMs = 1000 * 60 * 5;

export const fieldRuleDefinitionQueryKeys = {
  all: ['field-rule-definitions'] as const,
  list: () => [...fieldRuleDefinitionQueryKeys.all, 'list'] as const,
};

export function fieldRuleDefinitionsListQueryOptions() {
  return queryOptions({
    queryKey: fieldRuleDefinitionQueryKeys.list(),
    queryFn: listFieldRuleDefinitions,
    staleTime: fieldRuleDefinitionStaleTimeMs,
  });
}

export async function listFieldRuleDefinitions(): Promise<FieldRuleDefinition[]> {
  return fetchApi<FieldRuleDefinition[]>('/rules/field-rule-definitions');
}
