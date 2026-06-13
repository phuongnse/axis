import { Building2, KeyRound, ShieldCheck } from 'lucide-react';
import { useTranslation } from 'react-i18next';

import { FlowTrace, type FlowTraceStep } from '@/components/visual/FlowTrace';

type AccessPathTraceSurface = 'default' | 'dark' | 'adaptive';
type AccessPathTraceSize = 'md' | 'lg';

interface AccessPathTraceProps {
  surface?: AccessPathTraceSurface;
  size?: AccessPathTraceSize;
  className?: string;
}

const accessPathSteps = [
  {
    labelKey: 'landing.signInStep',
    metaKey: 'landing.signInStepMeta',
    state: 'active',
    icon: KeyRound,
  },
  {
    labelKey: 'landing.verifyAccess',
    metaKey: 'landing.verifyAccessMeta',
    state: 'pending',
    icon: ShieldCheck,
  },
  {
    labelKey: 'landing.openWorkspace',
    metaKey: 'landing.openWorkspaceMeta',
    state: 'pending',
    icon: Building2,
  },
] as const;

export function AccessPathTrace({
  surface = 'default',
  size = 'lg',
  className,
}: AccessPathTraceProps) {
  const { t } = useTranslation();
  const steps: FlowTraceStep[] = accessPathSteps.map((step) => ({
    id: step.labelKey,
    label: t(step.labelKey),
    meta: t(step.metaKey),
    state: step.state,
    icon: step.icon,
  }));

  if (surface === 'dark') {
    return (
      <FlowTrace
        steps={steps}
        size={size}
        iconMode="provided"
        className={className}
        markerClassName="border-white/15 bg-white/[0.06]"
        connectorClassName="bg-white/30"
        titleClassName="text-white"
        metaClassName="mt-1 leading-5 text-white/50"
        stateClassName={{
          active: 'border-accent bg-accent text-accent-foreground',
          pending: 'border-white/15 bg-white/[0.06] text-white/55',
        }}
      />
    );
  }

  if (surface === 'adaptive') {
    return (
      <FlowTrace
        steps={steps}
        size={size}
        iconMode="provided"
        className={className}
        markerClassName="border-border bg-background/70 dark:border-white/15 dark:bg-white/[0.06]"
        connectorClassName="bg-border dark:bg-white/30"
        titleClassName="text-foreground dark:text-white"
        metaClassName="mt-1 leading-5 text-muted-foreground dark:text-white/50"
        stateClassName={{
          active: 'border-accent bg-accent text-accent-foreground',
          pending:
            'border-border bg-background/70 text-muted-foreground dark:border-white/15 dark:bg-white/[0.06] dark:text-white/55',
        }}
      />
    );
  }

  return (
    <FlowTrace
      steps={steps}
      size={size}
      iconMode="provided"
      className={className}
      connectorClassName="bg-border"
    />
  );
}
