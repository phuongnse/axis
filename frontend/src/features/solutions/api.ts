import { queryOptions } from '@tanstack/react-query';
import { fetchApi } from '@/lib/api';
import type * as ApiTypes from '@/lib/api-generated';

export type PublishSolutionResult = ApiTypes.PublishSolutionResponse;
export type InstallSolutionResult = ApiTypes.InstallSolutionResponse;
export type SolutionInstallation = ApiTypes.SolutionInstallationStatusDto;
export type SolutionOperation = ApiTypes.SolutionOperationStatusDto;

export const solutionPackageMaxBytes = 10 * 1024 * 1024;

export const solutionQueryKeys = {
  all: ['solutions'] as const,
  versions: () => [...solutionQueryKeys.all, 'versions'] as const,
  version: (versionId: string) => [...solutionQueryKeys.versions(), versionId] as const,
  installations: () => [...solutionQueryKeys.all, 'installations'] as const,
  operation: (operationId: string) => [...solutionQueryKeys.all, 'operation', operationId] as const,
};

export function solutionVersionsQueryOptions() {
  return queryOptions({
    queryKey: solutionQueryKeys.versions(),
    queryFn: listSolutionVersions,
  });
}

export function solutionInstallationsQueryOptions() {
  return queryOptions({
    queryKey: solutionQueryKeys.installations(),
    queryFn: listSolutionInstallations,
  });
}

export function solutionOperationQueryOptions(operationId: string) {
  return queryOptions({
    queryKey: solutionQueryKeys.operation(operationId),
    queryFn: () => getSolutionOperation(operationId),
    enabled: operationId.length > 0,
    refetchInterval: (query) => {
      const status = query.state.data?.status;
      return status === 'Pending' || status === 'Running' ? 2_000 : false;
    },
  });
}

export function publishSolutionVersion(file: File): Promise<PublishSolutionResult> {
  return fetchApi('/solutions/versions', {
    method: 'POST',
    headers: { 'Content-Type': 'application/vnd.dsse.envelope.v1+json' },
    body: file,
    timeout: 60_000,
  });
}

export function installSolutionVersion(
  solutionVersionId: string,
  idempotencyKey: string,
): Promise<InstallSolutionResult> {
  return fetchApi(`/solutions/versions/${encodeURIComponent(solutionVersionId)}/installations`, {
    method: 'POST',
    headers: { 'Idempotency-Key': idempotencyKey },
  });
}

export function listSolutionInstallations(): Promise<SolutionInstallation[]> {
  return fetchApi('/solutions/installations');
}

export function listSolutionVersions(): Promise<ApiTypes.SolutionVersionSummaryDto[]> {
  return fetchApi('/solutions/versions');
}

export function getSolutionVersion(versionId: string): Promise<ApiTypes.SolutionVersionSummaryDto> {
  return fetchApi(`/solutions/versions/${encodeURIComponent(versionId)}`);
}

export function getSolutionOperation(operationId: string): Promise<SolutionOperation> {
  return fetchApi(`/solutions/operations/${encodeURIComponent(operationId)}`);
}

export function resumeSolutionOperation(operationId: string): Promise<SolutionOperation> {
  return fetchApi(`/solutions/operations/${encodeURIComponent(operationId)}/resume`, {
    method: 'POST',
  });
}
