import type { ComponentProps } from 'react';
import { useTranslation } from 'react-i18next';
import { StatusBadge, type StatusBadgeState } from '@/components/shared/StatusBadge';
import type { RuleOrigin } from '../api';

const originPresentation = {
  BuiltIn: { labelKey: 'rules.builtIn', state: 'informative' },
  Workspace: { labelKey: 'rules.originWorkspace', state: 'neutral' },
} as const satisfies Record<
  RuleOrigin,
  { labelKey: 'rules.builtIn' | 'rules.originWorkspace'; state: StatusBadgeState }
>;

interface RuleOriginBadgeProps
  extends Omit<ComponentProps<typeof StatusBadge>, 'children' | 'state'> {
  origin: RuleOrigin;
}

export function RuleOriginBadge({ origin, ...props }: RuleOriginBadgeProps) {
  const { t } = useTranslation();
  const presentation = originPresentation[origin];

  return (
    <StatusBadge {...props} state={presentation.state}>
      {t(presentation.labelKey)}
    </StatusBadge>
  );
}
