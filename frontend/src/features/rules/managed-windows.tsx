import type {
  ManagedWindowDescriptor,
  ManagedWindowRendererProps,
  ManagedWindowRendererRegistry,
} from '@/components/shared/ManagedWindowManager';
import { useCurrentManagedWindow } from '@/components/shared/ManagedWindowManager';
import type { RuleDefinitionSummary } from './api';
import { RuleEditorDialog } from './components/RuleEditorDialog';

const RULE_CREATE_KIND = 'rules.create';
const RULE_EDITOR_KIND = 'rules.editor';

export function ruleCreateWindowDescriptor(title: string): ManagedWindowDescriptor {
  return {
    id: 'rules:create',
    kind: RULE_CREATE_KIND,
    resourceKey: 'create',
    title,
  };
}

export function ruleDefinitionWindowDescriptor(
  definition: Pick<RuleDefinitionSummary, 'definitionKey' | 'origin'>,
  title: string,
): ManagedWindowDescriptor | null {
  if (!definition.definitionKey) return null;
  return {
    id: `rules:${definition.definitionKey}`,
    kind: RULE_EDITOR_KIND,
    resourceKey: definition.definitionKey,
    title,
    payload: { definitionKey: definition.definitionKey },
  };
}

export const rulesManagedWindowRenderers: ManagedWindowRendererRegistry = {
  [RULE_CREATE_KIND]: RuleEditorWindowRenderer,
  [RULE_EDITOR_KIND]: RuleEditorWindowRenderer,
};

function RuleEditorWindowRenderer({ descriptor }: ManagedWindowRendererProps) {
  const { windowId, closeWindow, replaceWindow } = useCurrentManagedWindow();
  const definitionKey = descriptor.kind === RULE_CREATE_KIND ? null : readDefinitionKey(descriptor);
  return (
    <RuleEditorDialog
      definitionKey={definitionKey}
      open
      onOpenChange={(open) => {
        if (!open) closeWindow(windowId);
      }}
      onCreated={(definition) => {
        const nextDescriptor = ruleDefinitionWindowDescriptor(
          definition,
          definition.name ?? 'Rule',
        );
        if (nextDescriptor) replaceWindow(windowId, nextDescriptor);
        else closeWindow(windowId);
      }}
    />
  );
}

function readDefinitionKey(descriptor: ManagedWindowDescriptor) {
  if (
    typeof descriptor.payload === 'object' &&
    descriptor.payload !== null &&
    'definitionKey' in descriptor.payload &&
    typeof descriptor.payload.definitionKey === 'string'
  )
    return descriptor.payload.definitionKey;
  return descriptor.resourceKey;
}
