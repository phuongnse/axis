import { queryOptions } from '@tanstack/react-query';
import { fetchApi } from '@/lib/api';
import type * as ApiTypes from '@/lib/api-generated';

export type RuleDefinitionSummary = ApiTypes.RuleDefinitionSummaryDto;
export type RuleDefinitionsPage = ApiTypes.RuleDefinitionSummaryDtoPagedResult;
export type RuleDefinitionDetail = ApiTypes.RuleDefinitionDetailDto;
export type RuleContextSchema = ApiTypes.RuleContextSchemaDto;
export type RuleParameterDefinition = ApiTypes.RuleParameterDefinitionDto;
export type RuleSimulationResult = ApiTypes.RuleSimulationResultDto;
export type CreateRuleDefinitionRequest = ApiTypes.CreateRuleDefinitionRequest;
export type SaveRuleDefinitionDraftRequest = ApiTypes.SaveRuleDefinitionDraftRequest;
export type SimulateRuleRequest = ApiTypes.SimulateRuleRequest;
export type RuleScope = ApiTypes.RuleScope;
export type RuleOrigin = ApiTypes.RuleOrigin;
export type RuleLifecycleStatus = ApiTypes.RuleLifecycleStatus;
export type RuleValueType = ApiTypes.RuleValueType;
export type RulePredicateOperator = ApiTypes.RulePredicateOperator;
export type RuleLogicalOperator = ApiTypes.RuleLogicalOperator;
export type RuleSeverity = ApiTypes.RuleSeverity;
export type RuleDecision = ApiTypes.RuleDecision;
export type RuleConditionNode = ApiTypes.RuleConditionNodeDto;
export type RuleOperand = ApiTypes.RuleOperandDto;
export type RuleExpressionLanguage = ApiTypes.RuleExpressionLanguageDto;
export type RuleExpressionFunction = ApiTypes.RuleExpressionFunction;
export type RuleExpressionCardinality = ApiTypes.RuleExpressionCardinality;
export type AssistRuleExpressionRequest = ApiTypes.AssistRuleExpressionRequest;
export type RuleExpressionAuthoring = ApiTypes.RuleExpressionAuthoringDto;
export type RuleExpressionCompletion = ApiTypes.RuleExpressionCompletionDto;
export type RuleExpressionDisplayNode = ApiTypes.RuleExpressionDisplayNodeDto;
export type RuleExpressionGuide = ApiTypes.RuleExpressionGuideDto;
export type SearchRuleExpressionGuideRequest = ApiTypes.SearchRuleExpressionGuideRequest;

export interface RuleDefinitionFilters {
  page?: number;
  pageSize?: number;
  scope?: RuleScope;
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
  contextSchemas: () => [...ruleDefinitionQueryKeys.all, 'context-schemas'] as const,
  expressionLanguage: () => [...ruleDefinitionQueryKeys.all, 'expression-language'] as const,
  expressionAssist: (request: AssistRuleExpressionRequest) =>
    [...ruleDefinitionQueryKeys.all, 'expression-assist', request] as const,
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

export function ruleContextSchemasQueryOptions() {
  return queryOptions({
    queryKey: ruleDefinitionQueryKeys.contextSchemas(),
    queryFn: listRuleContextSchemas,
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
  if (filters.scope) search.set('scope', filters.scope);
  if (filters.origin) search.set('origin', filters.origin);
  if (filters.status) search.set('status', filters.status);
  if (filters.query?.trim()) search.set('query', filters.query.trim());
  if (filters.language) search.set('language', filters.language);
  return fetchApi<RuleDefinitionsPage>(`/rules?${search.toString()}`, { signal });
}

export async function listRuleContextSchemas(): Promise<RuleContextSchema[]> {
  return fetchApi<RuleContextSchema[]>('/rules/context-schemas');
}

export async function getRuleExpressionLanguage(): Promise<RuleExpressionLanguage> {
  return fetchApi<RuleExpressionLanguage>('/rules/expression-language');
}

export async function assistRuleExpression(
  request: AssistRuleExpressionRequest,
): Promise<RuleExpressionAuthoring> {
  return fetchApi<RuleExpressionAuthoring>('/rules/expression-language/assist', {
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

export async function saveRuleDefinitionDraft(
  definitionKey: string,
  request: SaveRuleDefinitionDraftRequest,
): Promise<RuleDefinitionDetail> {
  return fetchApi<RuleDefinitionDetail>(`/rules/${encodeURIComponent(definitionKey)}/draft`, {
    method: 'PUT',
    body: JSON.stringify(request),
  });
}

export async function publishRuleDefinition(
  definitionKey: string,
  expectedRevision: number,
): Promise<RuleDefinitionDetail> {
  return ruleRevisionAction(definitionKey, 'publish', expectedRevision);
}

export async function startRuleDefinitionDraft(
  definitionKey: string,
  expectedRevision: number,
): Promise<RuleDefinitionDetail> {
  return ruleRevisionAction(definitionKey, 'draft', expectedRevision);
}

export async function archiveRuleDefinition(
  definitionKey: string,
  expectedRevision: number,
): Promise<RuleDefinitionDetail> {
  return ruleRevisionAction(definitionKey, 'archive', expectedRevision);
}

export async function simulateRuleDefinition(
  definitionKey: string,
  request: SimulateRuleRequest,
): Promise<RuleSimulationResult> {
  return fetchApi<RuleSimulationResult>(`/rules/${encodeURIComponent(definitionKey)}/simulate`, {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

async function ruleRevisionAction(
  definitionKey: string,
  action: 'publish' | 'draft' | 'archive',
  expectedRevision: number,
): Promise<RuleDefinitionDetail> {
  return fetchApi<RuleDefinitionDetail>(`/rules/${encodeURIComponent(definitionKey)}/${action}`, {
    method: 'POST',
    body: JSON.stringify({ expectedRevision }),
  });
}
