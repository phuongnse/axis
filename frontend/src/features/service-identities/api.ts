import { queryOptions } from '@tanstack/react-query';
import { fetchApi } from '@/lib/api';
import type * as ApiTypes from '@/lib/api-generated';

export type ServiceIdentity = ApiTypes.ServiceIdentityDto;
export type CreateServiceIdentityRequest = ApiTypes.CreateRequest;
export type AddServiceIdentityKeyRequest = ApiTypes.KeyRequest;

export const serviceIdentityQueryKeys = {
  all: ['service-identities'] as const,
  list: () => [...serviceIdentityQueryKeys.all, 'list'] as const,
};

export function serviceIdentitiesQueryOptions() {
  return queryOptions({
    queryKey: serviceIdentityQueryKeys.list(),
    queryFn: listServiceIdentities,
  });
}

export function listServiceIdentities(): Promise<ServiceIdentity[]> {
  return fetchApi('/service-identities');
}

export function createServiceIdentity(
  request: CreateServiceIdentityRequest,
): Promise<ServiceIdentity> {
  return fetchApi('/service-identities', {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export function addServiceIdentityKey(
  serviceIdentityId: string,
  request: AddServiceIdentityKeyRequest,
): Promise<ServiceIdentity> {
  return fetchApi(`/service-identities/${encodeURIComponent(serviceIdentityId)}/keys`, {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export function revokeServiceIdentityKey(
  serviceIdentityId: string,
  keyId: string,
  expectedRevision: number,
): Promise<ServiceIdentity> {
  return fetchApi(
    `/service-identities/${encodeURIComponent(serviceIdentityId)}/keys/${encodeURIComponent(keyId)}/revoke`,
    {
      method: 'POST',
      body: JSON.stringify({ expectedRevision }),
    },
  );
}

export function revokeServiceIdentity(
  serviceIdentityId: string,
  expectedRevision: number,
): Promise<ServiceIdentity> {
  return fetchApi(`/service-identities/${encodeURIComponent(serviceIdentityId)}/revoke`, {
    method: 'POST',
    body: JSON.stringify({ expectedRevision }),
  });
}
