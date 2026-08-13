import type { SolutionInstallationStatusDto, SolutionVersionSummaryDto } from '@/lib/api-generated';

export interface ExistingSolutionInstallation {
  installation: SolutionInstallationStatusDto;
  installedVersion: SolutionVersionSummaryDto;
  isExactVersion: boolean;
}

export function findExistingSolutionInstallation(
  targetVersion: SolutionVersionSummaryDto,
  versions: readonly SolutionVersionSummaryDto[],
  installations: readonly SolutionInstallationStatusDto[],
): ExistingSolutionInstallation | undefined {
  if (!targetVersion.solutionKey) return undefined;

  const versionsById = new Map(
    versions.flatMap((version) => (version.id ? ([[version.id, version]] as const) : [])),
  );

  for (const installation of installations) {
    const installedVersion = installation.solutionVersionId
      ? versionsById.get(installation.solutionVersionId)
      : undefined;
    if (installedVersion?.solutionKey !== targetVersion.solutionKey) continue;

    return {
      installation,
      installedVersion,
      isExactVersion: installedVersion.id === targetVersion.id,
    };
  }

  return undefined;
}
