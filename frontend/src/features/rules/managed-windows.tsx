import { useQuery } from '@tanstack/react-query';
import type {
  ManagedWindowDescriptor,
  ManagedWindowRendererProps,
  ManagedWindowRendererRegistry,
} from '@/components/shared/ManagedWindowManager';
import { useCurrentManagedWindow } from '@/components/shared/ManagedWindowManager';
import type { RuleDefinitionSummary } from './api';
import { ruleDefinitionDetailQueryOptions } from './api';
import { RuleEditorDialog } from './components/RuleEditorDialog';
import { SystemRuleDetailsDialog } from './components/SystemRuleDetailsDialog';

const RULE_CREATE_KIND = 'rules.create';
const RULE_EDITOR_KIND = 'rules.editor';
const RULE_SYSTEM_DETAILS_KIND = 'rules.system-details';

export function ruleCreateWindowDescriptor(title: string): ManagedWindowDescriptor {
  return {
    id: 'rules:create',
    kind: RULE_CREATE_KIND,
    resourceKey: 'create',
    title,
    initialSize: 'auto',
  };
}

export function ruleDefinitionWindowDescriptor(
  definition: Pick<RuleDefinitionSummary, 'definitionKey' | 'origin'>,
  title: string,
): ManagedWindowDescriptor | null {
  if (!definition.definitionKey) return null;
  return {
    id: `rules:${definition.definitionKey}`,
    kind: definition.origin === 'System' ? RULE_SYSTEM_DETAILS_KIND : RULE_EDITOR_KIND,
    resourceKey: definition.definitionKey,
    title,
    payload: { definitionKey: definition.definitionKey },
    initialSize: 'auto',
  };
}

export const rulesManagedWindowRenderers: ManagedWindowRendererRegistry = {
  [RULE_CREATE_KIND]: RuleEditorWindowRenderer,
  [RULE_EDITOR_KIND]: RuleEditorWindowRenderer,
  [RULE_SYSTEM_DETAILS_KIND]: SystemRuleWindowRenderer,
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

function SystemRuleWindowRenderer({ descriptor }: ManagedWindowRendererProps) {
  const { windowId, closeWindow } = useCurrentManagedWindow();
  const definitionKey = readDefinitionKey(descriptor);
  const definitionQuery = useQuery(ruleDefinitionDetailQueryOptions(definitionKey));
  return (
    <SystemRuleDetailsDialog
      definition={definitionQuery.data ?? null}
      fallbackTitle={descriptor.title}
      loading={definitionQuery.isLoading}
      unavailable={definitionQuery.isError || (!definitionQuery.isLoading && !definitionQuery.data)}
      open
      onOpenChange={(open) => {
        if (!open) closeWindow(windowId);
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
