import { useQuery } from '@tanstack/react-query';
import { ShieldCheck } from 'lucide-react';
import { useMemo } from 'react';
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
import { PageAction, PageHeader, PageLayout } from '@/components/shared/PageLayout';
import { StatusBadge } from '@/components/shared/StatusBadge';
import type {
  AssignableSubjectDto,
  ProductRoleAssignmentDto,
  ProductRoleOptionDto,
} from '@/lib/api-generated';
import { productRoleManagementQueryOptions } from '../api';
import {
  productRoleAssignmentWindowDescriptor,
  productRoleAssignWindowDescriptor,
} from '../managed-windows';

export function ProductRoleAssignmentsPage() {
  const { t, i18n } = useTranslation();
  const { openWindow } = useManagedWindowActions();
  const language = i18n.resolvedLanguage ?? i18n.language;
  const managementQuery = useQuery(productRoleManagementQueryOptions(language));
  const data = managementQuery.data;
  const subjects = data?.subjects ?? [];
  const roles = data?.roles ?? [];
  const assignments = (data?.assignments ?? []).filter((assignment) => assignment.isActive);
  const tableQuery = useMemo<DataTableQueryState>(
    () => ({
      globalFilter: '',
      filterExpression: createEmptyFilterExpression(),
      sorting: [],
      grouping: [],
    }),
    [],
  );
  const tableDefinition = useMemo<DataTableDefinition<ProductRoleAssignmentDto>>(() => {
    const columns: DataTableColumnDef<ProductRoleAssignmentDto>[] = [
      {
        id: 'subject',
        size: 280,
        minSize: 220,
        enableSorting: false,
        meta: { label: t('productRoles.subject') },
        cell: ({ row }) => {
          const subject = findSubject(subjects, row.original);
          const title = subject?.displayName ?? t('productRoles.unknownSubject');
          const role = findRole(roles, row.original);
          return (
            <div className="min-w-0">
              <PageAction
                type="button"
                variant="link"
                onClick={() =>
                  openWindow(
                    productRoleAssignmentWindowDescriptor(row.original, subject, role, title),
                  )
                }
              >
                {title}
              </PageAction>
              {subject?.secondaryLabel ? (
                <p className="truncate text-xs text-muted-foreground">{subject.secondaryLabel}</p>
              ) : null}
            </div>
          );
        },
      },
      {
        id: 'kind',
        accessorFn: (assignment) => assignment.subject?.kind,
        size: 130,
        minSize: 120,
        enableSorting: false,
        meta: { label: t('productRoles.kind') },
      },
      {
        id: 'role',
        size: 280,
        minSize: 220,
        enableSorting: false,
        meta: { label: t('productRoles.role') },
        cell: ({ row }) => {
          const role = findRole(roles, row.original);
          return (
            <div className="min-w-0">
              <p className="truncate font-medium">{role?.displayName ?? row.original.roleKey}</p>
              {role?.description ? (
                <p className="truncate text-xs text-muted-foreground">{role.description}</p>
              ) : null}
            </div>
          );
        },
      },
      {
        id: 'policy',
        size: 220,
        minSize: 190,
        enableSorting: false,
        meta: { label: t('productRoles.policy') },
        cell: ({ row }) => {
          const role = findRole(roles, row.original);
          return role?.policyKey ?? row.original.policyVersionId ?? t('productRoles.notAvailable');
        },
      },
      {
        id: 'status',
        size: 130,
        minSize: 120,
        enableSorting: false,
        meta: { label: t('productRoles.status') },
        cell: () => <StatusBadge tone="success">{t('productRoles.active')}</StatusBadge>,
      },
    ];

    const emptyDescription =
      subjects.length === 0
        ? t('productRoles.noSubjects')
        : roles.length === 0
          ? t('productRoles.noRoles')
          : t('productRoles.currentDescription');

    return {
      ariaLabel: t('productRoles.listLabel'),
      source: {
        mode: 'client',
        data: assignments,
        pagination: { pageSize: 20, pageSizeOptions: [20, 50, 100] },
      },
      columns,
      messages: createDataTableMessages(t, {
        searchLabel: t('productRoles.listLabel'),
        searchPlaceholder: t('productRoles.listLabel'),
        emptyTitle: t('productRoles.empty'),
        emptyDescription,
        errorTitle: t('productRoles.loadFailed'),
        errorDescription: t('productRoles.actionFailedDescription'),
      }),
      getRowId: assignmentKey,
      queryState: tableQuery,
      onQueryStateChange: () => undefined,
      columnControls: true,
      grouping: false,
      renderToolbarActions:
        managementQuery.isSuccess && subjects.length > 0 && roles.length > 0
          ? () => (
              <PageAction
                type="button"
                size="sm"
                onClick={() =>
                  openWindow(
                    productRoleAssignWindowDescriptor(
                      { subjects, roles, assignments: data?.assignments ?? [] },
                      t('productRoles.assignTitle'),
                    ),
                  )
                }
              >
                <ShieldCheck aria-hidden />
                {t('productRoles.assign')}
              </PageAction>
            )
          : undefined,
      loading: managementQuery.isPending,
      error: managementQuery.isError,
      onRetry: () => void managementQuery.refetch(),
    };
  }, [
    assignments,
    data?.assignments,
    managementQuery.isError,
    managementQuery.isPending,
    managementQuery.isSuccess,
    managementQuery.refetch,
    openWindow,
    roles,
    subjects,
    t,
    tableQuery,
  ]);

  return (
    <PageLayout scrollMode="contained">
      <PageHeader title={t('productRoles.title')} description={t('productRoles.description')} />
      <div className="min-h-0 flex-1">
        <DataTable definition={tableDefinition} />
      </div>
    </PageLayout>
  );
}

function findSubject(
  subjects: AssignableSubjectDto[],
  assignment: ProductRoleAssignmentDto,
): AssignableSubjectDto | undefined {
  return subjects.find(
    (subject) =>
      subject.subject?.kind === assignment.subject?.kind &&
      subject.subject?.subjectId === assignment.subject?.subjectId,
  );
}

function findRole(
  roles: ProductRoleOptionDto[],
  assignment: ProductRoleAssignmentDto,
): ProductRoleOptionDto | undefined {
  return roles.find(
    (role) =>
      role.policyVersionId === assignment.policyVersionId && role.roleKey === assignment.roleKey,
  );
}

function assignmentKey(assignment: ProductRoleAssignmentDto): string {
  return `${assignment.subject?.kind}-${assignment.subject?.subjectId}-${assignment.policyVersionId}-${assignment.roleKey}`;
}
