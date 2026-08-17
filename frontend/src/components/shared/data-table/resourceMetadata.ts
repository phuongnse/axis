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
  modifiedBy: string;
  modifiedAt: string;
}

export function createResourceMetadataColumns<TData extends ResourceMetadataRow>(
  labels: ResourceMetadataColumnLabels,
  options: { includeRevision?: boolean; locale?: string } = {},
): DataTableColumnDef<TData>[] {
  const actorCollator = new Intl.Collator(options.locale, {
    numeric: true,
    sensitivity: 'base',
  });
  const compareActors = (left: unknown, right: unknown) => {
    const leftName = actorDisplayName(left);
    const rightName = actorDisplayName(right);
    const localized = actorCollator.compare(leftName, rightName);
    return localized !== 0 ? localized : ordinalCompare(leftName, rightName);
  };
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
      id: 'modifiedBy',
      accessorFn: (row) => actorValue(row.metadata?.modifiedBy),
      size: 190,
      minSize: 170,
      sortUndefined: 'last',
      sortingFn: (left, right, columnId) =>
        compareActors(left.getValue(columnId), right.getValue(columnId)),
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

function actorValue(
  actor: ResourceMetadataActorValue | null | undefined,
): ResourceMetadataActorValue | undefined {
  return actorDisplayName(actor) ? (actor ?? undefined) : undefined;
}

function actorDisplayName(actor: unknown): string {
  if (typeof actor !== 'object' || actor === null || !('displayName' in actor)) return '';
  return typeof actor.displayName === 'string' ? actor.displayName.trim() : '';
}

function ordinalCompare(left: string, right: string): number {
  return left < right ? -1 : left > right ? 1 : 0;
}
