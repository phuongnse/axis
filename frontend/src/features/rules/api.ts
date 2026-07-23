import { queryOptions } from '@tanstack/react-query';
import { fetchApi } from '@/lib/api';
import type { components } from '@/lib/api-types';

export type RuleDefinitionSummary = components['schemas']['RuleDefinitionSummaryDto'];
export type RuleDefinitionsPage = components['schemas']['RuleDefinitionSummaryDtoPagedResult'];
export type RuleDefinitionDetail = components['schemas']['RuleDefinitionDetailDto'];
export type RuleContextSchema = components['schemas']['RuleContextSchemaDto'];
export type RuleParameterDefinition = components['schemas']['RuleParameterDefinitionDto'];
export type RuleSimulationResult = components['schemas']['RuleSimulationResultDto'];
export type CreateRuleDefinitionRequest = components['schemas']['CreateRuleDefinitionRequest'];
export type SaveRuleDefinitionDraftRequest =
  components['schemas']['SaveRuleDefinitionDraftRequest'];
export type SimulateRuleRequest = components['schemas']['SimulateRuleRequest'];
export type RuleScope = components['schemas']['RuleScope'];
export type RuleOrigin = components['schemas']['RuleOrigin'];
export type RuleLifecycleStatus = components['schemas']['RuleLifecycleStatus'];
export type RuleValueType = components['schemas']['RuleValueType'];
export type RulePredicateOperator = components['schemas']['RulePredicateOperator'];
export type RuleLogicalOperator = components['schemas']['RuleLogicalOperator'];
export type RuleSeverity = components['schemas']['RuleSeverity'];
export type RuleDecision = components['schemas']['RuleDecision'];
export type RuleConditionNode = components['schemas']['RuleConditionNodeDto'];
export type RuleOperand = components['schemas']['RuleOperandDto'];
export type RuleExpressionLanguage = components['schemas']['RuleExpressionLanguageDto'];
export type RuleExpressionFunction = components['schemas']['RuleExpressionFunction'];
export type RuleExpressionCardinality = components['schemas']['RuleExpressionCardinality'];

export interface RuleDefinitionFilters {
  page?: number;
  pageSize?: number;
  scope?: RuleScope;
  origin?: RuleOrigin;
  status?: RuleLifecycleStatus;
}

const defaultFilters = { page: 1, pageSize: 100 } as const;

export const ruleDefinitionStaleTimeMs = 1000 * 60 * 5;

export const ruleDefinitionQueryKeys = {
  all: ['rule-definitions'] as const,
  list: (filters: RuleDefinitionFilters = defaultFilters) =>
    [...ruleDefinitionQueryKeys.all, 'list', filters] as const,
  detail: (definitionKey: string) =>
    [...ruleDefinitionQueryKeys.all, 'detail', definitionKey] as const,
  contextSchemas: () => [...ruleDefinitionQueryKeys.all, 'context-schemas'] as const,
  expressionLanguage: () => [...ruleDefinitionQueryKeys.all, 'expression-language'] as const,
};

export function ruleDefinitionsListQueryOptions(filters: RuleDefinitionFilters = defaultFilters) {
  return queryOptions({
    queryKey: ruleDefinitionQueryKeys.list(filters),
    queryFn: () => listRuleDefinitions(filters),
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
): Promise<RuleDefinitionsPage> {
  const search = new URLSearchParams({
    page: String(filters.page ?? defaultFilters.page),
    pageSize: String(filters.pageSize ?? defaultFilters.pageSize),
  });
  if (filters.scope) search.set('scope', filters.scope);
  if (filters.origin) search.set('origin', filters.origin);
  if (filters.status) search.set('status', filters.status);
  return fetchApi<RuleDefinitionsPage>(`/rules?${search.toString()}`);
}

export async function listRuleContextSchemas(): Promise<RuleContextSchema[]> {
  return fetchApi<RuleContextSchema[]>('/rules/context-schemas');
}

export async function getRuleExpressionLanguage(): Promise<RuleExpressionLanguage> {
  return fetchApi<RuleExpressionLanguage>('/rules/expression-language');
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
