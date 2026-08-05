import { queryOptions } from '@tanstack/react-query';
import { fetchApi } from '@/lib/api';
import type * as ApiTypes from '@/lib/api-generated';

export type RuleDefinitionSummary = ApiTypes.RuleDefinitionSummaryDto;
export type RuleDefinitionsPage = ApiTypes.RuleDefinitionSummaryDtoPagedResult;
export type RuleDefinitionDetail = ApiTypes.RuleDefinitionDetailDto;
export type RuleInputDefinition = ApiTypes.RuleInputDefinitionDto;
export type RuleDraftInputDefinition = ApiTypes.RuleDraftInputDefinitionDto;
export type RuleOutputContract = ApiTypes.RuleOutputContractDto;
export type RuleSimulationResult = ApiTypes.RuleSimulationResultDto;
export type CreateRuleDefinitionRequest = ApiTypes.CreateRuleDefinitionRequest;
export type SaveRuleDefinitionDraftRequest = ApiTypes.SaveRuleDefinitionDraftRequest;
export type RuleOrigin = ApiTypes.RuleOrigin;
export type RuleLifecycleStatus = ApiTypes.RuleLifecycleStatus;
export type RuleValueType = ApiTypes.RuleValueType;
export type RulePredicateOperator = ApiTypes.RulePredicateOperator;
export type RuleLogicalOperator = ApiTypes.RuleLogicalOperator;
export type RuleConditionNode = ApiTypes.RuleConditionNodeDto;
export type RuleOperand = ApiTypes.RuleOperandDto;
export type RuleExpressionLanguage = ApiTypes.RuleExpressionLanguageDto;
export type RuleExpressionFunction = ApiTypes.RuleExpressionFunction;
export type RuleExpressionCardinality = ApiTypes.RuleExpressionCardinality;
export type ProjectRuleConditionRequest = ApiTypes.ProjectRuleConditionRequest;
export type RuleConditionProjection = ApiTypes.RuleConditionProjectionDto;
export type RuleExpressionReferenceKind = ApiTypes.RuleExpressionReferenceKind;
export type RuleExpressionDisplayNode = ApiTypes.RuleExpressionDisplayNodeDto;
export type RuleExpressionGuide = ApiTypes.RuleExpressionGuideDto;
export type SearchRuleExpressionGuideRequest = ApiTypes.SearchRuleExpressionGuideRequest;
export type RuleBinding = ApiTypes.RuleBindingDto;
export type CreateRuleBindingRequest = ApiTypes.CreateRuleBindingRequest;
export type RuleBindingUsage = ApiTypes.RuleBindingUsageDto;
export type RuleAuthoringProjection = ApiTypes.RuleAuthoringProjectionDto;

export interface RuleDefinitionFilters {
  page?: number;
  pageSize?: number;
  origin?: RuleOrigin;
  status?: RuleLifecycleStatus;
  query?: string;
  language?: string;
}

const defaultFilters = { page: 1, pageSize: 20 } as const;

export const ruleDefinitionStaleTimeMs = 1000 * 60 * 5;

export const ruleDefinitionQueryKeys = {
  all: ['rule-definitions'] as const,
  list: (filters: RuleDefinitionFilters = defaultFilters) =>
    [...ruleDefinitionQueryKeys.all, 'list', filters] as const,
  detail: (definitionKey: string) =>
    [...ruleDefinitionQueryKeys.all, 'detail', definitionKey] as const,
  usage: (definitionKey: string, version: number) =>
    [...ruleDefinitionQueryKeys.all, 'usage', definitionKey, version] as const,
  expressionLanguage: () => [...ruleDefinitionQueryKeys.all, 'expression-language'] as const,
  conditionProjection: (request: ProjectRuleConditionRequest) =>
    [...ruleDefinitionQueryKeys.all, 'condition-projection', request] as const,
  expressionGuide: (request: SearchRuleExpressionGuideRequest) =>
    [...ruleDefinitionQueryKeys.all, 'expression-guide', request] as const,
};

export function ruleDefinitionsListQueryOptions(filters: RuleDefinitionFilters = defaultFilters) {
  return queryOptions({
    queryKey: ruleDefinitionQueryKeys.list(filters),
    queryFn: ({ signal }) => listRuleDefinitions(filters, signal),
    staleTime: ruleDefinitionStaleTimeMs,
  });
}

export function ruleDefinitionDetailQueryOptions(definitionKey: string) {
  return queryOptions({
    queryKey: ruleDefinitionQueryKeys.detail(definitionKey),
    queryFn: () => getRuleDefinition(definitionKey),
    staleTime: ruleDefinitionStaleTimeMs,
  });
}

export function ruleExpressionLanguageQueryOptions() {
  return queryOptions({
    queryKey: ruleDefinitionQueryKeys.expressionLanguage(),
    queryFn: getRuleExpressionLanguage,
    staleTime: Number.POSITIVE_INFINITY,
  });
}

export async function listRuleDefinitions(
  filters: RuleDefinitionFilters = defaultFilters,
  signal?: AbortSignal,
): Promise<RuleDefinitionsPage> {
  const search = new URLSearchParams({
    page: String(filters.page ?? defaultFilters.page),
    pageSize: String(filters.pageSize ?? defaultFilters.pageSize),
  });
  if (filters.origin) search.set('origin', filters.origin);
  if (filters.status) search.set('status', filters.status);
  if (filters.query?.trim()) search.set('query', filters.query.trim());
  if (filters.language) search.set('language', filters.language);
  return fetchApi<RuleDefinitionsPage>(`/rules?${search.toString()}`, { signal });
}

export async function getRuleExpressionLanguage(): Promise<RuleExpressionLanguage> {
  return fetchApi<RuleExpressionLanguage>('/rules/expression-language');
}

export async function projectRuleCondition(
  request: ProjectRuleConditionRequest,
): Promise<RuleConditionProjection> {
  return fetchApi<RuleConditionProjection>('/rules/condition/project', {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export async function projectRuleAuthoring(
  request: ApiTypes.ProjectRuleAuthoringRequest,
): Promise<RuleAuthoringProjection> {
  return fetchApi<RuleAuthoringProjection>('/rules/authoring/project', {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export async function completeRuleAuthoring(
  request: ApiTypes.CompleteRuleAuthoringRequest,
): Promise<ApiTypes.RuleAuthoringCompletionDto[]> {
  return fetchApi<ApiTypes.RuleAuthoringCompletionDto[]>('/rules/authoring/complete', {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export async function searchRuleExpressionGuide(
  request: SearchRuleExpressionGuideRequest,
  signal?: AbortSignal,
): Promise<RuleExpressionGuide> {
  return fetchApi<RuleExpressionGuide>('/rules/expression-language/guide', {
    method: 'POST',
    body: JSON.stringify(request),
    signal,
  });
}

export async function getRuleDefinition(definitionKey: string): Promise<RuleDefinitionDetail> {
  return fetchApi<RuleDefinitionDetail>(`/rules/${encodeURIComponent(definitionKey)}`);
}

export async function createRuleDefinition(
  request: CreateRuleDefinitionRequest,
): Promise<RuleDefinitionDetail> {
  return fetchApi<RuleDefinitionDetail>('/rules', {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export async function createRuleBinding(request: CreateRuleBindingRequest): Promise<RuleBinding> {
  return fetchApi<RuleBinding>('/rule-bindings', {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export async function deleteRuleBinding(
  bindingId: string,
  expectedRevision: number,
): Promise<void> {
  await fetchApi<void>(`/rule-bindings/${encodeURIComponent(bindingId)}`, {
    method: 'DELETE',
    body: JSON.stringify({ expectedRevision }),
  });
}

export async function updateRuleBinding(
  bindingId: string,
  request: ApiTypes.UpdateRuleBindingRequest,
): Promise<RuleBinding> {
  return fetchApi<RuleBinding>(`/rule-bindings/${encodeURIComponent(bindingId)}`, {
    method: 'PUT',
    body: JSON.stringify(request),
  });
}

export async function getRuleBinding(bindingId: string): Promise<RuleBinding> {
  return fetchApi<RuleBinding>(`/rule-bindings/${encodeURIComponent(bindingId)}`);
}

export function ruleBindingUsageQueryOptions(definitionKey: string, version: number) {
  return queryOptions({
    queryKey: ruleDefinitionQueryKeys.usage(definitionKey, version),
    queryFn: () => listRuleBindingUsage(definitionKey, version),
  });
}

export async function listRuleBindingUsage(
  definitionKey: string,
  version: number,
): Promise<RuleBindingUsage[]> {
  return fetchApi<RuleBindingUsage[]>(
    `/rules/${encodeURIComponent(definitionKey)}/bindings?version=${version}`,
  );
}

export async function saveRuleDefinitionDraft(
  definitionKey: string,
  request: SaveRuleDefinitionDraftRequest,
): Promise<RuleDefinitionDetail> {
  return fetchApi<RuleDefinitionDetail>(`/rules/${encodeURIComponent(definitionKey)}/draft`, {
    method: 'PUT',
    body: JSON.stringify(request),
  });
}

export async function createRuleDefinitionVersion(
  definitionKey: string,
  expectedRevision: number,
): Promise<RuleDefinitionDetail> {
  return fetchApi<RuleDefinitionDetail>(`/rules/${encodeURIComponent(definitionKey)}/versions`, {
    method: 'POST',
    body: JSON.stringify({ expectedRevision }),
  });
}

export async function deactivateRuleDefinition(
  definitionKey: string,
  expectedRevision: number,
): Promise<RuleDefinitionDetail> {
  return fetchApi<RuleDefinitionDetail>(`/rules/${encodeURIComponent(definitionKey)}/active-version`, {
    method: 'DELETE',
    body: JSON.stringify({ expectedRevision }),
  });
}

export async function activateRuleDefinitionVersion(
  definitionKey: string,
  version: number,
  expectedRevision: number,
): Promise<RuleDefinitionDetail> {
  return fetchApi<RuleDefinitionDetail>(`/rules/${encodeURIComponent(definitionKey)}/active-version`, {
    method: 'PUT',
    body: JSON.stringify({ version, expectedRevision }),
  });
}

export async function archiveRuleDefinition(
  definitionKey: string,
  expectedRevision: number,
): Promise<RuleDefinitionDetail> {
  return fetchApi<RuleDefinitionDetail>(`/rules/${encodeURIComponent(definitionKey)}/archive`, {
    method: 'POST',
    body: JSON.stringify({ expectedRevision }),
  });
}

export async function simulateRuleDefinitionDraft(
  definitionKey: string,
  request: ApiTypes.SimulateRuleDraftRequest,
): Promise<RuleSimulationResult> {
  return fetchApi<RuleSimulationResult>(`/rules/${encodeURIComponent(definitionKey)}/draft/simulate`, {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export async function simulateRuleDefinitionVersion(
  definitionKey: string,
  version: number,
  request: ApiTypes.SimulateRuleVersionRequest,
): Promise<RuleSimulationResult> {
  return fetchApi<RuleSimulationResult>(
    `/rules/${encodeURIComponent(definitionKey)}/versions/${version}/simulate`,
    {
    method: 'POST',
      body: JSON.stringify(request),
    },
  );
}
