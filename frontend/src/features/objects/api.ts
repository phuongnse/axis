import { queryOptions } from '@tanstack/react-query';
import { fetchApi } from '@/lib/api';
import type { components } from '@/lib/api-types';

export type ObjectDefinitionDetail = components['schemas']['ObjectDefinitionDetailDto'];
export type ObjectDefinitionListItem = components['schemas']['ObjectDefinitionListItemDto'];
export type ObjectDefinitionPage = components['schemas']['ObjectDefinitionListItemDtoPagedResult'];
export type CreateObjectDefinitionRequest = components['schemas']['CreateObjectDefinitionRequest'];
export type SaveUnpublishedObjectDefinitionRequest =
  components['schemas']['SaveUnpublishedObjectDefinitionRequest'];
export type PublishObjectDefinitionRequest =
  components['schemas']['PublishObjectDefinitionRequest'];
export type ObjectFieldDefinitionInput = components['schemas']['ObjectFieldDefinitionInput'];
export type ObjectFieldType = components['schemas']['ObjectFieldType'];
export type ObjectFieldRuleDto = components['schemas']['ObjectFieldRuleDto'];
export type ObjectFieldRuleInput = components['schemas']['ObjectFieldRuleInput'];

export const objectDefinitionsDefaultPageSize = 20;
export const objectDefinitionStaleTimeMs = 1000 * 60 * 5;

export const objectDefinitionQueryKeys = {
  all: ['object-definitions'] as const,
  lists: () => [...objectDefinitionQueryKeys.all, 'list'] as const,
  list: (page: number, pageSize: number) =>
    [...objectDefinitionQueryKeys.lists(), page, pageSize] as const,
  details: () => [...objectDefinitionQueryKeys.all, 'detail'] as const,
  detail: (id: string) => [...objectDefinitionQueryKeys.all, 'detail', id] as const,
};

export function objectDefinitionsListQueryOptions(
  page = 1,
  pageSize = objectDefinitionsDefaultPageSize,
) {
  return queryOptions({
    queryKey: objectDefinitionQueryKeys.list(page, pageSize),
    queryFn: () => listObjectDefinitions(page, pageSize),
    staleTime: objectDefinitionStaleTimeMs,
  });
}

export function objectDefinitionDetailQueryOptions(id: string) {
  return queryOptions({
    queryKey: objectDefinitionQueryKeys.detail(id),
    queryFn: () => getObjectDefinition(id),
    staleTime: objectDefinitionStaleTimeMs,
  });
}

export async function listObjectDefinitions(page: number, pageSize: number) {
  const search = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  return fetchApi<ObjectDefinitionPage>(`/object-definitions?${search.toString()}`);
}

export async function createObjectDefinition(
  request: CreateObjectDefinitionRequest,
): Promise<ObjectDefinitionDetail> {
  return fetchApi<ObjectDefinitionDetail>('/object-definitions', {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export async function getObjectDefinition(id: string): Promise<ObjectDefinitionDetail> {
  return fetchApi<ObjectDefinitionDetail>(`/object-definitions/${id}`);
}

export async function saveUnpublishedObjectDefinition(
  id: string,
  request: SaveUnpublishedObjectDefinitionRequest,
): Promise<ObjectDefinitionDetail> {
  return fetchApi<ObjectDefinitionDetail>(`/object-definitions/${id}/unpublished`, {
    method: 'PUT',
    body: JSON.stringify(request),
  });
}

export async function publishObjectDefinition(
  id: string,
  request: PublishObjectDefinitionRequest,
): Promise<ObjectDefinitionDetail> {
  return fetchApi<ObjectDefinitionDetail>(`/object-definitions/${id}/publish`, {
    method: 'POST',
    body: JSON.stringify(request),
  });
}
