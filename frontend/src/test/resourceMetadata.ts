import type { ResourceMetadataDto } from '@/lib/api-generated';

const actor = {
  kind: 'User' as const,
  subjectId: '11111111-1111-1111-1111-111111111111',
  displayName: 'Test Administrator',
};

export function resourceMetadata(revision: number | null): ResourceMetadataDto {
  return {
    revision,
    createdBy: actor,
    createdAt: '2026-08-06T10:00:00Z',
    modifiedBy: actor,
    modifiedAt: '2026-08-06T11:00:00Z',
  };
}
