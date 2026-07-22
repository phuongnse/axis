import type { ComponentProps } from 'react';
import { useTranslation } from 'react-i18next';
import { StatusBadge, type StatusBadgeTone } from '@/components/shared/StatusBadge';
import type { RuleOrigin } from '../api';

const originPresentation = {
  System: { labelKey: 'rules.builtIn', tone: 'info' },
  Workspace: { labelKey: 'rules.originWorkspace', tone: 'brand' },
} as const satisfies Record<
  RuleOrigin,
  { labelKey: 'rules.builtIn' | 'rules.originWorkspace'; tone: StatusBadgeTone }
>;

interface RuleOriginBadgeProps
  extends Omit<ComponentProps<typeof StatusBadge>, 'children' | 'tone'> {
  origin: RuleOrigin;
}

export function RuleOriginBadge({ origin, ...props }: RuleOriginBadgeProps) {
  const { t } = useTranslation();
  const presentation = originPresentation[origin];

  return (
    <StatusBadge {...props} tone={presentation.tone}>
      {t(presentation.labelKey)}
    </StatusBadge>
  );
}
