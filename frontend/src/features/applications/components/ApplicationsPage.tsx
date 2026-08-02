import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { getRouteApi } from '@tanstack/react-router';
import { Plus, WandSparkles } from 'lucide-react';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  createDataTableMessages,
  createEmptyFilterExpression,
  DataTable,
  type DataTableColumnDef,
  type DataTableDefinition,
  type DataTableQueryState,
} from '@/components/shared/data-table';
import { useManagedWindowActions } from '@/components/shared/ManagedWindowManager';
import { StatusBadge } from '@/components/shared/StatusBadge';
import { StatusNotice } from '@/components/shared/StatusNotice';
import { Button } from '@/components/ui/button';
import { ApiError } from '@/lib/api';
import {
  applicationQueryKeys,
  applicationRecordsQueryOptions,
  type BusinessObjectRecordListItem,
  findSampleApplicationDefinition,
  provisionSampleApplication,
  sampleApplicationObjectKey,
} from '../api';
import { applicationRecordWindowDescriptor } from '../managed-windows';

const route = getRouteApi('/_authenticated/applications');

export function ApplicationsPage() {
  const { t, i18n } = useTranslation();
  const queryClient = useQueryClient();
  const { openWindow } = useManagedWindowActions();
  const navigate = route.useNavigate();
  const search = route.useSearch();
  const [requestError, setRequestError] = useState<string | null>(null);
  const definitionQuery = useQuery({
    queryKey: [...applicationQueryKeys.definitions(), i18n.language],
    queryFn: findSampleApplicationDefinition,
  });
  const definition = definitionQuery.data;
  const recordsQuery = useQuery(applicationRecordsQueryOptions(1, 20));
  const records = recordsQuery.data?.items ?? [];
  const setupMutation = useMutation({
    mutationFn: provisionSampleApplication,
    onSuccess: async () => {
      setRequestError(null);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: applicationQueryKeys.definitions() }),
        queryClient.invalidateQueries({ queryKey: applicationQueryKeys.lists() }),
      ]);
    },
    onError: (error) => setRequestError(readApiError(error, t('applications.requestError'))),
  });
  const createMutation = useMutation({
    mutationFn: () =>
      import('../api').then(({ createBusinessObjectRecord }) =>
        createBusinessObjectRecord(sampleApplicationObjectKey, {
          idempotencyKey: createIdempotencyKey(),
          values: {},
        }),
      ),
    onSuccess: async (record) => {
      setRequestError(null);
      await queryClient.invalidateQueries({ queryKey: applicationQueryKeys.lists() });
      if (record.id) {
        openWindow(
          applicationRecordWindowDescriptor({
            recordId: record.id,
            title: t('applications.editorTitle'),
          }),
        );
      }
    },
    onError: (error) => setRequestError(readApiError(error, t('applications.requestError'))),
  });

  const openRecord = useCallback(
    (record: BusinessObjectRecordListItem) => {
      if (!record.id) return;
      openWindow(
        applicationRecordWindowDescriptor({
          recordId: record.id,
          title: t('applications.editorTitle'),
        }),
      );
    },
    [openWindow, t],
  );

  useEffect(() => {
    if (!search.recordId) return;
    openWindow(
      applicationRecordWindowDescriptor({
        recordId: search.recordId,
        title: t('applications.editorTitle'),
      }),
    );
    void navigate({ replace: true, search: {} });
  }, [navigate, openWindow, search.recordId, t]);

  const tableDefinition = useMemo<DataTableDefinition<BusinessObjectRecordListItem>>(() => {
    const dateFormatter = new Intl.DateTimeFormat(i18n.language, {
      dateStyle: 'medium',
      timeStyle: 'short',
    });
    const columns: DataTableColumnDef<BusinessObjectRecordListItem>[] = [
      {
        id: 'record',
        accessorKey: 'id',
        size: 320,
        minSize: 220,
        enableSorting: false,
        meta: { label: t('applications.record') },
        cell: ({ row }) => (
          <Button type="button" variant="link" onClick={() => openRecord(row.original)}>
            {shortId(row.original.id)}
          </Button>
        ),
      },
      {
        id: 'status',
        accessorKey: 'status',
        size: 150,
        minSize: 130,
        enableSorting: false,
        meta: { label: t('applications.status'), searchable: false },
        cell: ({ row }) =>
          row.original.status === 'Submitted' ? (
            <StatusBadge tone="success">{t('applications.submitted')}</StatusBadge>
          ) : (
            <StatusBadge tone="neutral">{t('applications.draft')}</StatusBadge>
          ),
      },
      {
        id: 'version',
        accessorKey: 'definitionVersion',
        size: 120,
        minSize: 100,
        enableSorting: false,
        meta: { label: t('applications.version'), searchable: false },
        cell: ({ row }) =>
          t('applications.versionValue', { version: row.original.definitionVersion ?? 0 }),
      },
      {
        id: 'updated',
        accessorKey: 'updatedAt',
        size: 220,
        minSize: 180,
        enableSorting: false,
        meta: { label: t('applications.updated'), searchable: false },
        cell: ({ row }) =>
          row.original.updatedAt
            ? dateFormatter.format(new Date(row.original.updatedAt))
            : t('applications.notAvailable'),
      },
    ];

    const tableQuery: DataTableQueryState = {
      globalFilter: '',
      filterExpression: createEmptyFilterExpression(),
      sorting: [],
      grouping: [],
    };
    return {
      ariaLabel: t('applications.listTitle'),
      source: {
        mode: 'page',
        data: records,
        pagination: { pageIndex: 0, pageSize: recordsQuery.data?.pageSize ?? 20 },
        rowCount: recordsQuery.data?.totalCount ?? records.length,
        onPaginationChange: () => undefined,
      },
      columns,
      messages: createDataTableMessages(t, {
        searchLabel: t('applications.searchLabel'),
        searchPlaceholder: t('applications.searchPlaceholder'),
        emptyTitle: t('applications.emptyTitle'),
        emptyDescription: t('applications.emptyDescription'),
        errorTitle: t('applications.loadError'),
        errorDescription: t('applications.loadErrorDescription'),
      }),
      getRowId: (record) => record.id ?? 'record',
      queryState: tableQuery,
      onQueryStateChange: () => undefined,
      globalSearch: false,
      grouping: false,
      columnControls: true,
      enableColumnResizing: true,
      renderToolbarActions: () =>
        definition?.status === 'Published' ? (
          <Button
            type="button"
            size="sm"
            disabled={createMutation.isPending}
            onClick={() => createMutation.mutate()}
          >
            <Plus aria-hidden />
            {createMutation.isPending ? t('applications.creating') : t('applications.new')}
          </Button>
        ) : null,
      loading: recordsQuery.isFetching,
      error: recordsQuery.isError,
      onRetry: () => void recordsQuery.refetch(),
    };
  }, [
    createMutation,
    definition?.status,
    i18n.language,
    openRecord,
    records,
    recordsQuery.data?.pageSize,
    recordsQuery.data?.totalCount,
    recordsQuery.isError,
    recordsQuery.isFetching,
    recordsQuery.refetch,
    t,
  ]);

  return (
    <div className="flex h-full min-h-0 w-full min-w-0 flex-col gap-4 overflow-hidden p-4 sm:p-6 lg:p-8">
      <header className="min-w-0 shrink-0">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <h1 className="font-heading text-2xl font-semibold text-foreground">
              {t('applications.title')}
            </h1>
            <p className="mt-1 max-w-3xl text-sm leading-6 text-muted-foreground">
              {t('applications.pageDescription')}
            </p>
          </div>
          {definition?.status === 'Published' ? (
            <StatusBadge tone="info">{t('applications.workflowReady')}</StatusBadge>
          ) : null}
        </div>
      </header>

      {requestError ? (
        <StatusNotice tone="destructive" title={t('applications.requestErrorTitle')}>
          {requestError}
        </StatusNotice>
      ) : null}

      {definitionQuery.isError ? (
        <StatusNotice tone="destructive" title={t('applications.loadError')}>
          {t('applications.loadErrorDescription')}
        </StatusNotice>
      ) : null}

      {!definitionQuery.isLoading &&
      !definitionQuery.isError &&
      definition?.status !== 'Published' ? (
        <section className="flex shrink-0 flex-col gap-4 rounded-xl border border-dashed border-primary/30 bg-primary/5 p-5 sm:flex-row sm:items-center sm:justify-between">
          <div className="flex items-start gap-3">
            <WandSparkles className="mt-0.5 size-5 shrink-0 text-primary" aria-hidden />
            <div>
              <h2 className="font-medium text-foreground">{t('applications.setupTitle')}</h2>
              <p className="mt-1 max-w-2xl text-sm leading-6 text-muted-foreground">
                {t('applications.setupDescription')}
              </p>
            </div>
          </div>
          <Button
            type="button"
            className="shrink-0"
            disabled={setupMutation.isPending}
            onClick={() => setupMutation.mutate()}
          >
            <WandSparkles aria-hidden />
            {setupMutation.isPending ? t('applications.settingUp') : t('applications.setup')}
          </Button>
        </section>
      ) : null}

      <div className="min-h-0 flex-1">
        <DataTable definition={tableDefinition} />
      </div>
    </div>
  );
}

function shortId(id?: string) {
  return id ? `${id.slice(0, 8)}…${id.slice(-4)}` : '—';
}

function createIdempotencyKey() {
  return typeof crypto !== 'undefined' && 'randomUUID' in crypto
    ? crypto.randomUUID()
    : `${Date.now()}-${Math.random().toString(36).slice(2)}`;
}

function readApiError(error: unknown, fallback: string): string {
  if (!(error instanceof ApiError) || typeof error.data !== 'object' || error.data === null) {
    return fallback;
  }
  const detail = (error.data as { detail?: unknown }).detail;
  return typeof detail === 'string' && detail ? detail : fallback;
}
