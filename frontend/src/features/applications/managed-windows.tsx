import type {
  ManagedWindowDescriptor,
  ManagedWindowRendererProps,
  ManagedWindowRendererRegistry,
} from '@/components/shared/ManagedWindowManager';
import { useCurrentManagedWindow } from '@/components/shared/ManagedWindowManager';
import { ApplicationRecordDialog } from './components/ApplicationRecordDialog';

const APPLICATION_RECORD_KIND = 'applications.record';

export function applicationRecordWindowDescriptor({
  recordId,
  title,
}: {
  recordId: string;
  title: string;
}): ManagedWindowDescriptor {
  return {
    id: `applications:${recordId}`,
    kind: APPLICATION_RECORD_KIND,
    resourceKey: recordId,
    title,
    initialSize: 'windowed',
    payload: { recordId },
  };
}

export const applicationsManagedWindowRenderers: ManagedWindowRendererRegistry = {
  [APPLICATION_RECORD_KIND]: ApplicationRecordWindowRenderer,
};

function ApplicationRecordWindowRenderer({ descriptor }: ManagedWindowRendererProps) {
  const { windowId, closeWindow } = useCurrentManagedWindow();
  const recordId =
    typeof descriptor.payload === 'object' &&
    descriptor.payload !== null &&
    'recordId' in descriptor.payload
      ? typeof descriptor.payload.recordId === 'string'
        ? descriptor.payload.recordId
        : descriptor.resourceKey
      : descriptor.resourceKey;

  return (
    <ApplicationRecordDialog
      recordId={recordId}
      title={descriptor.title}
      onClose={() => closeWindow(windowId)}
    />
  );
}
