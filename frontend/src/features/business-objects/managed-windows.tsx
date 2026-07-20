import { useQuery } from '@tanstack/react-query';
import type {
  ManagedWindowDescriptor,
  ManagedWindowRendererProps,
  ManagedWindowRendererRegistry,
} from '@/components/shared/ManagedWindowManager';
import { useCurrentManagedWindow } from '@/components/shared/ManagedWindowManager';
import { ruleDefinitionsListQueryOptions } from '@/features/rules/api';
import { BusinessObjectDefinitionDialog } from './components/BusinessObjectDefinitionDialog';

const BUSINESS_OBJECT_DEFINITION_KIND = 'business-objects.definition';
export type BusinessObjectWindowMode = 'create' | 'edit' | 'view';

export function businessObjectCreateWindowDescriptor(title: string): ManagedWindowDescriptor {
  return {
    id: 'business-objects:create',
    kind: BUSINESS_OBJECT_DEFINITION_KIND,
    resourceKey: 'create',
    title,
    payload: { mode: 'create' satisfies BusinessObjectWindowMode },
    initialSize: 'auto',
  };
}

export function businessObjectDefinitionWindowDescriptor({
  recordId,
  mode,
  title,
}: {
  recordId: string;
  mode: Exclude<BusinessObjectWindowMode, 'create'>;
  title: string;
}): ManagedWindowDescriptor {
  return {
    id: `business-objects:${recordId}`,
    kind: BUSINESS_OBJECT_DEFINITION_KIND,
    resourceKey: recordId,
    title,
    payload: { mode, recordId },
    initialSize: 'auto',
  };
}

export const businessObjectsManagedWindowRenderers: ManagedWindowRendererRegistry = {
  [BUSINESS_OBJECT_DEFINITION_KIND]: BusinessObjectDefinitionWindowRenderer,
};

function BusinessObjectDefinitionWindowRenderer({ descriptor }: ManagedWindowRendererProps) {
  const { windowId, closeWindow, replaceWindow } = useCurrentManagedWindow();
  const payload = readPayload(descriptor);
  const rulesQuery = useQuery(
    ruleDefinitionsListQueryOptions({ page: 1, pageSize: 100, scope: 'Field' }),
  );

  return (
    <BusinessObjectDefinitionDialog
      mode={payload.mode}
      recordId={payload.recordId}
      ruleDefinitions={rulesQuery.data?.items ?? []}
      ruleCatalogLoading={rulesQuery.isLoading}
      ruleCatalogUnavailable={rulesQuery.isError}
      onCreated={(recordId, title) => {
        replaceWindow(
          windowId,
          businessObjectDefinitionWindowDescriptor({ recordId, mode: 'edit', title }),
        );
      }}
      onClose={() => closeWindow(windowId)}
    />
  );
}

function readPayload(descriptor: ManagedWindowDescriptor): {
  mode: BusinessObjectWindowMode;
  recordId?: string;
} {
  if (typeof descriptor.payload !== 'object' || descriptor.payload === null) {
    return { mode: 'view', recordId: descriptor.resourceKey };
  }
  const mode =
    'mode' in descriptor.payload &&
    (descriptor.payload.mode === 'create' ||
      descriptor.payload.mode === 'edit' ||
      descriptor.payload.mode === 'view')
      ? descriptor.payload.mode
      : 'view';
  const recordId =
    'recordId' in descriptor.payload && typeof descriptor.payload.recordId === 'string'
      ? descriptor.payload.recordId
      : mode === 'create'
        ? undefined
        : descriptor.resourceKey;
  return { mode, recordId };
}
