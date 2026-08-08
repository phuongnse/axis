import { queryOptions } from '@tanstack/react-query';
import { fetchApi } from '@/lib/api';
import type * as ApiTypes from '@/lib/api-generated';

export type BusinessObjectDefinitionDetail = ApiTypes.BusinessObjectDefinitionDetailDto;
export type BusinessObjectDefinitionCollectionActions =
  ApiTypes.BusinessObjectDefinitionCollectionActionsDto;
export type BusinessObjectDefinitionListItem = ApiTypes.BusinessObjectDefinitionListItemDto;
export type BusinessObjectDefinitionPage = ApiTypes.BusinessObjectDefinitionListItemDtoPagedResult;
export type CreateBusinessObjectDefinitionRequest = ApiTypes.CreateBusinessObjectDefinitionRequest;
export type SaveUnpublishedBusinessObjectDefinitionRequest =
  ApiTypes.SaveUnpublishedBusinessObjectDefinitionRequest;
export type PublishBusinessObjectDefinitionRequest =
  ApiTypes.PublishBusinessObjectDefinitionRequest;
export type BusinessObjectFieldDefinitionInput = ApiTypes.BusinessObjectFieldDefinitionInput;
export type BusinessObjectFieldType = ApiTypes.BusinessObjectFieldType;
export type BusinessObjectChoiceSelectionMode = ApiTypes.BusinessObjectChoiceSelectionMode;
export type BusinessObjectChoiceFieldConfigurationInput =
  ApiTypes.BusinessObjectChoiceFieldConfigurationInput;
export type BusinessObjectFieldRuleDto = ApiTypes.BusinessObjectFieldRuleDto;
export type BusinessObjectFieldRuleInput = ApiTypes.BusinessObjectFieldRuleInput;

export const businessObjectDefinitionsDefaultPageSize = 20;
export const businessObjectDefinitionStaleTimeMs = 1000 * 60 * 5;

export const businessObjectDefinitionQueryKeys = {
  all: ['business-object-definitions'] as const,
  actions: () => [...businessObjectDefinitionQueryKeys.all, 'actions'] as const,
  lists: () => [...businessObjectDefinitionQueryKeys.all, 'list'] as const,
  list: (page: number, pageSize: number, query: string, language: string) =>
    [...businessObjectDefinitionQueryKeys.lists(), page, pageSize, query, language] as const,
  details: () => [...businessObjectDefinitionQueryKeys.all, 'detail'] as const,
  detail: (id: string) => [...businessObjectDefinitionQueryKeys.all, 'detail', id] as const,
};

export function businessObjectDefinitionCollectionActionsQueryOptions() {
  return queryOptions({
    queryKey: businessObjectDefinitionQueryKeys.actions(),
    queryFn: getBusinessObjectDefinitionCollectionActions,
    staleTime: businessObjectDefinitionStaleTimeMs,
  });
}

export function businessObjectDefinitionsListQueryOptions(
  page = 1,
  pageSize = businessObjectDefinitionsDefaultPageSize,
  query = '',
  language = 'en',
) {
  return queryOptions({
    queryKey: businessObjectDefinitionQueryKeys.list(page, pageSize, query, language),
    queryFn: ({ signal }) => listBusinessObjectDefinitions(page, pageSize, query, language, signal),
    staleTime: businessObjectDefinitionStaleTimeMs,
  });
}

export function businessObjectDefinitionDetailQueryOptions(id: string) {
  return queryOptions({
    queryKey: businessObjectDefinitionQueryKeys.detail(id),
    queryFn: () => getBusinessObjectDefinition(id),
    staleTime: businessObjectDefinitionStaleTimeMs,
  });
}

export async function listBusinessObjectDefinitions(
  page: number,
  pageSize: number,
  query = '',
  language = 'en',
  signal?: AbortSignal,
) {
  const search = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (query.trim()) search.set('query', query.trim());
  search.set('language', language);
  return fetchApi<BusinessObjectDefinitionPage>(
    `/business-object-definitions?${search.toString()}`,
    { signal },
  );
}

export async function getBusinessObjectDefinitionCollectionActions(): Promise<BusinessObjectDefinitionCollectionActions> {
  return fetchApi<BusinessObjectDefinitionCollectionActions>(
    '/business-object-definitions/actions',
  );
}

export async function createBusinessObjectDefinition(
  request: CreateBusinessObjectDefinitionRequest,
): Promise<BusinessObjectDefinitionDetail> {
  return fetchApi<BusinessObjectDefinitionDetail>('/business-object-definitions', {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export async function getBusinessObjectDefinition(
  id: string,
): Promise<BusinessObjectDefinitionDetail> {
  return fetchApi<BusinessObjectDefinitionDetail>(`/business-object-definitions/${id}`);
}

export async function saveUnpublishedBusinessObjectDefinition(
  id: string,
  request: SaveUnpublishedBusinessObjectDefinitionRequest,
): Promise<BusinessObjectDefinitionDetail> {
  return fetchApi<BusinessObjectDefinitionDetail>(
    `/business-object-definitions/${id}/unpublished`,
    {
      method: 'PUT',
      body: JSON.stringify(request),
    },
  );
}

export async function publishBusinessObjectDefinition(
  id: string,
  request: PublishBusinessObjectDefinitionRequest,
): Promise<BusinessObjectDefinitionDetail> {
  return fetchApi<BusinessObjectDefinitionDetail>(`/business-object-definitions/${id}/publish`, {
    method: 'POST',
    body: JSON.stringify(request),
  });
}
