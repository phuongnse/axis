import { queryOptions } from '@tanstack/react-query';
import { fetchApi } from '@/lib/api';
import type * as ApiTypes from '@/lib/api-generated';

export type ProductRoleManagement = ApiTypes.ProductRoleManagementResponse;
export type ProductRoleAssignment = ApiTypes.ProductRoleAssignmentDto;

export const productRoleQueryKeys = {
  all: ['product-role-assignments'] as const,
  management: (language: string) => [...productRoleQueryKeys.all, language] as const,
};

export function productRoleManagementQueryOptions(language: string) {
  return queryOptions({
    queryKey: productRoleQueryKeys.management(language),
    queryFn: () => listProductRoleAssignments(language),
  });
}

export function listProductRoleAssignments(language: string): Promise<ProductRoleManagement> {
  return fetchApi(`/product-role-assignments?language=${encodeURIComponent(language)}`);
}

export function assignProductRole(
  request: ApiTypes.AssignProductRoleBody,
  idempotencyKey: string,
): Promise<ApiTypes.ProductRoleAssignmentResponse> {
  return fetchApi('/product-role-assignments/assign', {
    method: 'POST',
    headers: { 'Idempotency-Key': idempotencyKey },
    body: JSON.stringify(request),
  });
}

export function revokeProductRole(
  request: ApiTypes.RevokeProductRoleBody,
  idempotencyKey: string,
): Promise<ApiTypes.ProductRoleAssignmentResponse> {
  return fetchApi('/product-role-assignments/revoke', {
    method: 'POST',
    headers: { 'Idempotency-Key': idempotencyKey },
    body: JSON.stringify(request),
  });
}
