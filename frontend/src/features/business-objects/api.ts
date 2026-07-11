import { queryOptions } from '@tanstack/react-query';
import { fetchApi } from '@/lib/api';
import type { components } from '@/lib/api-types';

export type BusinessObjectDefinitionDetail =
  components['schemas']['BusinessObjectDefinitionDetailDto'];
export type BusinessObjectDefinitionListItem =
  components['schemas']['BusinessObjectDefinitionListItemDto'];
export type BusinessObjectDefinitionPage =
  components['schemas']['BusinessObjectDefinitionListItemDtoPagedResult'];
export type CreateBusinessObjectDefinitionRequest =
  components['schemas']['CreateBusinessObjectDefinitionRequest'];
export type SaveUnpublishedBusinessObjectDefinitionRequest =
  components['schemas']['SaveUnpublishedBusinessObjectDefinitionRequest'];
export type PublishBusinessObjectDefinitionRequest =
  components['schemas']['PublishBusinessObjectDefinitionRequest'];
export type BusinessObjectFieldDefinitionInput =
  components['schemas']['BusinessObjectFieldDefinitionInput'];
export type BusinessObjectFieldType = components['schemas']['BusinessObjectFieldType'];
export type BusinessObjectChoiceSelectionMode =
  components['schemas']['BusinessObjectChoiceSelectionMode'];
export type BusinessObjectChoiceFieldConfigurationInput =
  components['schemas']['BusinessObjectChoiceFieldConfigurationInput'];
export type BusinessObjectFieldRuleDto = components['schemas']['BusinessObjectFieldRuleDto'];
export type BusinessObjectFieldRuleInput = components['schemas']['BusinessObjectFieldRuleInput'];

export const businessObjectDefinitionsDefaultPageSize = 20;
export const businessObjectDefinitionStaleTimeMs = 1000 * 60 * 5;

export const businessObjectDefinitionQueryKeys = {
  all: ['business-object-definitions'] as const,
  lists: () => [...businessObjectDefinitionQueryKeys.all, 'list'] as const,
  list: (page: number, pageSize: number) =>
    [...businessObjectDefinitionQueryKeys.lists(), page, pageSize] as const,
  details: () => [...businessObjectDefinitionQueryKeys.all, 'detail'] as const,
  detail: (id: string) => [...businessObjectDefinitionQueryKeys.all, 'detail', id] as const,
};

export function businessObjectDefinitionsListQueryOptions(
  page = 1,
  pageSize = businessObjectDefinitionsDefaultPageSize,
) {
  return queryOptions({
    queryKey: businessObjectDefinitionQueryKeys.list(page, pageSize),
    queryFn: () => listBusinessObjectDefinitions(page, pageSize),
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

export async function listBusinessObjectDefinitions(page: number, pageSize: number) {
  const search = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  return fetchApi<BusinessObjectDefinitionPage>(
    `/business-object-definitions?${search.toString()}`,
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
