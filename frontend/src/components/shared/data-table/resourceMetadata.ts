import type { DataTableColumnDef } from './types';

export interface ResourceMetadataActorValue {
  displayName?: string | null;
}

export interface ResourceMetadataValue {
  revision?: number | null;
  createdBy?: ResourceMetadataActorValue | null;
  createdAt?: string | null;
  modifiedBy?: ResourceMetadataActorValue | null;
  modifiedAt?: string | null;
}

export interface ResourceMetadataRow {
  metadata?: ResourceMetadataValue | null;
}

export interface ResourceMetadataColumnLabels {
  revision: string;
  createdBy: string;
  createdAt: string;
  modifiedBy: string;
  modifiedAt: string;
}

export function createResourceMetadataColumns<TData extends ResourceMetadataRow>(
  labels: ResourceMetadataColumnLabels,
  options: { includeRevision?: boolean } = {},
): DataTableColumnDef<TData>[] {
  const columns: DataTableColumnDef<TData>[] = [];
  if (options.includeRevision !== false) {
    columns.push({
      id: 'revision',
      accessorFn: (row) => row.metadata?.revision,
      size: 120,
      minSize: 110,
      enableGrouping: false,
      meta: { label: labels.revision, cell: { kind: 'revision' }, searchable: false },
    });
  }

  columns.push(
    {
      id: 'createdBy',
      accessorFn: (row) => row.metadata?.createdBy,
      size: 190,
      minSize: 170,
      enableGrouping: false,
      meta: { label: labels.createdBy, cell: { kind: 'actor' }, searchable: false },
    },
    {
      id: 'createdAt',
      accessorFn: (row) => row.metadata?.createdAt,
      size: 190,
      minSize: 180,
      enableGrouping: false,
      meta: { label: labels.createdAt, cell: { kind: 'dateTime' }, searchable: false },
    },
    {
      id: 'modifiedBy',
      accessorFn: (row) => row.metadata?.modifiedBy,
      size: 190,
      minSize: 170,
      enableGrouping: false,
      meta: { label: labels.modifiedBy, cell: { kind: 'actor' }, searchable: false },
    },
    {
      id: 'modifiedAt',
      accessorFn: (row) => row.metadata?.modifiedAt,
      size: 190,
      minSize: 180,
      enableGrouping: false,
      meta: { label: labels.modifiedAt, cell: { kind: 'dateTime' }, searchable: false },
    },
  );

  return columns;
}
