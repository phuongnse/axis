import { useQuery, useQueryClient } from '@tanstack/react-query';
import { getRouteApi } from '@tanstack/react-router';
import { Upload } from 'lucide-react';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  createDataTableMessages,
  createEmptyFilterExpression,
  createResourceMetadataColumns,
  DataTable,
  type DataTableColumnDef,
  type DataTableDefinition,
  type DataTableQueryState,
  DataTableRecordAction,
} from '@/components/shared/data-table';
import { useManagedWindowActions } from '@/components/shared/ManagedWindowManager';
import { PageAction } from '@/components/shared/PageLayout';
import { ResourceWorkspace } from '@/components/shared/ResourceWorkspace';
import { StatusBadge } from '@/components/shared/StatusBadge';
import type { SolutionInstallationStatusDto, SolutionVersionSummaryDto } from '@/lib/api-generated';
import {
  solutionInstallationsQueryOptions,
  solutionVersionQueryOptions,
  solutionVersionsQueryOptions,
} from '../api';
import { findExistingSolutionInstallation } from '../installation-availability';
import {
  solutionInstallationWindowDescriptor,
  solutionPublishWindowDescriptor,
  solutionReleaseWindowDescriptor,
} from '../managed-windows';
import {
  ComplianceStatusBadge,
  OperationStatusBadge,
  ProvisioningStatusBadge,
  SolutionTrustBadge,
} from './SolutionPresentation';

const route = getRouteApi('/_authenticated/solutions');

type SolutionRow = SolutionVersionSummaryDto & {
  installation?: SolutionInstallationStatusDto;
  otherInstalledVersion?: SolutionVersionSummaryDto;
};

export function SolutionsPage() {
  const { t, i18n } = useTranslation();
  const queryClient = useQueryClient();
  const { openWindow } = useManagedWindowActions();
  const search = route.useSearch();
  const navigate = route.useNavigate();
  const versionsQuery = useQuery(solutionVersionsQueryOptions());
  const installationsQuery = useQuery(solutionInstallationsQueryOptions());
  const [tableQuery, setTableQuery] = useState<DataTableQueryState>(() => ({
    globalFilter: search.query ?? '',
    filterExpression: createEmptyFilterExpression(),
    sorting: [],
    grouping: [],
  }));
  useEffect(() => {
    setTableQuery((current) =>
      current.globalFilter === (search.query ?? '')
        ? current
        : { ...current, globalFilter: search.query ?? '' },
    );
  }, [search.query]);

  useEffect(() => {
    if (!search.dialog) return;
    if (search.dialog === 'publish') {
      openWindow(solutionPublishWindowDescriptor(t('solutions.publishTitle')));
    } else if (search.dialog === 'release' && search.versionId) {
      openWindow(solutionReleaseWindowDescriptor(search.versionId, t('solutions.releaseTitle')));
    } else if (search.dialog === 'installation' && search.installationId) {
      openWindow(
        solutionInstallationWindowDescriptor(search.installationId, t('solutions.operationTitle')),
      );
    } else {
      return;
    }
    void navigate({
      replace: true,
      search: (current) => ({
        ...current,
        dialog: undefined,
        versionId: undefined,
        installationId: undefined,
      }),
    });
  }, [navigate, openWindow, search.dialog, search.installationId, search.versionId, t]);

  const openVersion = useCallback(
    (version: SolutionVersionSummaryDto) => {
      if (!version.id) return;
      openWindow(solutionReleaseWindowDescriptor(version.id, solutionIdentity(version, t)));
    },
    [openWindow, t],
  );

  const prefetchVersion = useCallback(
    (versionId: string | undefined) => {
      if (!versionId) return;
      void queryClient.prefetchQuery(solutionVersionQueryOptions(versionId));
    },
    [queryClient],
  );

  const openInstallation = useCallback(
    (row: SolutionRow) => {
      if (!row.installation?.id) return;
      openWindow(
        solutionInstallationWindowDescriptor(
          row.installation.id,
          t('solutions.installationWindowTitle', {
            solution: row.solutionKey ?? t('solutions.notAvailable'),
            version: row.solutionVersion ?? '',
          }),
        ),
      );
    },
    [openWindow, t],
  );

  const rows = useMemo(() => {
    const versions = versionsQuery.data ?? [];
    const installations = installationsQuery.data ?? [];
    const joined = versions.map<SolutionRow>((version) => {
      const existing = findExistingSolutionInstallation(version, versions, installations);
      return {
        ...version,
        installation: existing?.isExactVersion ? existing.installation : undefined,
        otherInstalledVersion:
          existing && !existing.isExactVersion ? existing.installedVersion : undefined,
      };
    });
    return filterSolutions(joined, tableQuery.globalFilter);
  }, [installationsQuery.data, tableQuery.globalFilter, versionsQuery.data]);

  const updateTableQuery = useCallback(
    (next: DataTableQueryState) => {
      setTableQuery(next);
      if (next.globalFilter !== (search.query ?? '')) {
        void navigate({
          replace: true,
          search: (current) => ({
            ...current,
            query: next.globalFilter.trim() || undefined,
          }),
        });
      }
    },
    [navigate, search.query],
  );

  const table = useMemo<DataTableDefinition<SolutionRow>>(() => {
    const columns: DataTableColumnDef<SolutionRow>[] = [
      {
        id: 'solution',
        accessorKey: 'solutionKey',
        size: 220,
        minSize: 180,
        meta: {
          label: t('solutions.solutionKey'),
          cell: { kind: 'action' },
          searchable: true,
        },
        cell: ({ row }) => (
          <DataTableRecordAction
            onFocus={() => prefetchVersion(row.original.id)}
            onMouseEnter={() => prefetchVersion(row.original.id)}
            onClick={() => openVersion(row.original)}
          >
            {row.original.solutionKey ?? t('solutions.notAvailable')}
          </DataTableRecordAction>
        ),
      },
      {
        id: 'version',
        accessorKey: 'solutionVersion',
        size: 120,
        minSize: 110,
        meta: {
          label: t('solutions.version'),
          cell: { kind: 'version' },
          searchable: true,
        },
      },
      {
        id: 'trust',
        accessorKey: 'trustStatus',
        size: 130,
        minSize: 120,
        meta: {
          label: t('solutions.trustStatus'),
          cell: { kind: 'status' },
          searchable: false,
          filter: {
            kind: 'singleChoice',
            options: [
              { value: 'Trusted', label: t('solutions.trusted') },
              { value: 'Revoked', label: t('solutions.revoked') },
              { value: 'Unknown', label: t('solutions.trustUnknown') },
            ],
          },
        },
        cell: ({ row }) => <SolutionTrustBadge status={row.original.trustStatus} />,
      },
      {
        id: 'installation',
        accessorFn: (row) =>
          row.installation?.provisioningStatus ??
          (row.otherInstalledVersion ? 'OtherVersionInstalled' : 'NotInstalled'),
        size: 230,
        minSize: 210,
        meta: {
          label: t('solutions.installationStatus'),
          cell: { kind: 'action' },
          searchable: false,
          filter: {
            kind: 'singleChoice',
            options: [
              { value: 'NotInstalled', label: t('solutions.notInstalled') },
              {
                value: 'OtherVersionInstalled',
                label: t('solutions.anotherVersionInstalled'),
              },
              { value: 'Installing', label: t('solutions.installingStatus') },
              { value: 'Installed', label: t('solutions.installedStatus') },
              { value: 'Failed', label: t('solutions.failedStatus') },
            ],
          },
        },
        cell: ({ row }) => {
          const installation = row.original.installation;
          return (
            <div className="flex min-w-0 items-center gap-2">
              {installation ? (
                <ProvisioningStatusBadge status={installation.provisioningStatus} />
              ) : row.original.otherInstalledVersion ? (
                <StatusBadge state="caution">
                  {t('solutions.versionInstalled', {
                    version:
                      row.original.otherInstalledVersion.solutionVersion ??
                      t('solutions.notAvailable'),
                  })}
                </StatusBadge>
              ) : (
                <StatusBadge state="neutral">{t('solutions.notInstalled')}</StatusBadge>
              )}
              {installation?.id ? (
                <DataTableRecordAction
                  aria-label={t('solutions.viewInstallationFor', {
                    solution: row.original.solutionKey ?? t('solutions.notAvailable'),
                    version: row.original.solutionVersion ?? t('solutions.notAvailable'),
                  })}
                  onClick={() => openInstallation(row.original)}
                >
                  {t('solutions.viewInstallation')}
                </DataTableRecordAction>
              ) : null}
            </div>
          );
        },
      },
      {
        id: 'compliance',
        accessorFn: (row) => row.installation?.complianceStatus,
        size: 150,
        minSize: 140,
        meta: {
          label: t('solutions.complianceStatus'),
          cell: { kind: 'status' },
          searchable: false,
          filter: {
            kind: 'singleChoice',
            options: [
              { value: 'Compliant', label: t('solutions.compliant') },
              { value: 'Noncompliant', label: t('solutions.noncompliant') },
            ],
          },
        },
        cell: ({ row }) =>
          row.original.installation?.complianceStatus ? (
            <ComplianceStatusBadge status={row.original.installation.complianceStatus} />
          ) : (
            t('solutions.notAvailable')
          ),
      },
      {
        id: 'operation',
        accessorFn: (row) => row.installation?.operationStatus,
        size: 150,
        minSize: 140,
        meta: {
          label: t('solutions.operationStatus'),
          cell: { kind: 'status' },
          searchable: false,
        },
        cell: ({ row }) =>
          row.original.installation?.operationStatus ? (
            <OperationStatusBadge status={row.original.installation.operationStatus} />
          ) : (
            t('solutions.notAvailable')
          ),
      },
      ...createResourceMetadataColumns<SolutionRow>(
        {
          revision: t('metadata.revision'),
          modifiedBy: t('metadata.modifiedBy'),
          modifiedAt: t('metadata.modifiedAt'),
        },
        { includeRevision: false, locale: i18n.language },
      ),
    ];

    return {
      ariaLabel: t('solutions.tableLabel'),
      locale: i18n.language,
      source: { mode: 'client', data: rows, pagination: { pageSize: 20 } },
      columns,
      messages: createDataTableMessages(t, {
        searchLabel: t('solutions.searchLabel'),
        searchPlaceholder: t('solutions.searchPlaceholder'),
        emptyTitle: t('solutions.versionsEmpty'),
        emptyDescription: t('solutions.versionsEmptyDescription'),
        errorTitle: t('solutions.loadFailed'),
        errorDescription: t('solutions.loadFailedDescription'),
      }),
      getRowId: (version) =>
        version.id ?? `${version.solutionKey}:${version.solutionVersion}:${version.packageSha256}`,
      queryState: tableQuery,
      onQueryStateChange: updateTableQuery,
      globalSearch: true,
      grouping: false,
      columnControls: true,
      enableColumnResizing: true,
      renderToolbarActions: () => (
        <PageAction
          type="button"
          size="sm"
          onClick={() => openWindow(solutionPublishWindowDescriptor(t('solutions.publishTitle')))}
        >
          <Upload aria-hidden />
          {t('solutions.publishAction')}
        </PageAction>
      ),
      loading: versionsQuery.isPending || installationsQuery.isPending,
      error: versionsQuery.isError || installationsQuery.isError,
      onRetry: () => {
        void versionsQuery.refetch();
        void installationsQuery.refetch();
      },
    };
  }, [
    i18n.language,
    installationsQuery.isError,
    installationsQuery.isPending,
    installationsQuery.refetch,
    openInstallation,
    openVersion,
    openWindow,
    prefetchVersion,
    rows,
    t,
    tableQuery,
    updateTableQuery,
    versionsQuery.isError,
    versionsQuery.isPending,
    versionsQuery.refetch,
  ]);

  return (
    <ResourceWorkspace
      surfaceId="solution-delivery"
      title={t('solutions.title')}
      description={t('solutions.description')}
    >
      <DataTable definition={table} />
    </ResourceWorkspace>
  );
}

function filterSolutions(rows: readonly SolutionRow[], query: string): SolutionRow[] {
  const normalized = normalizeSearch(query);
  if (!normalized) return [...rows];
  return rows.filter((row) =>
    normalizeSearch(
      [
        row.solutionKey,
        row.solutionVersion,
        row.publisherId,
        row.publisherKeyId,
        row.packageSha256,
        row.sourceRevision,
        row.installation?.id,
        row.installation?.operationId,
        row.installation?.provisioningStatus,
        row.installation?.complianceStatus,
        row.installation?.operationStatus,
        row.otherInstalledVersion?.solutionVersion,
      ].join(' '),
    ).includes(normalized),
  );
}

function normalizeSearch(value: string): string {
  return value.normalize('NFKD').toLocaleLowerCase().trim();
}

function solutionIdentity(version: SolutionVersionSummaryDto, t: (key: string) => string): string {
  return `${version.solutionKey ?? t('solutions.releaseTitle')} ${version.solutionVersion ?? ''}`.trim();
}
