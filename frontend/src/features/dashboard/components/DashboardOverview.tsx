import { Activity, AlertTriangle, CheckCircle2, Database, Plus, Workflow, Zap } from 'lucide-react';
import { useTranslation } from 'react-i18next';

import { Button } from '@/components/ui/button';
import { FlowTrace, type FlowTraceStep } from '@/components/visual/FlowTrace';
import { cn } from '@/lib/utils';

const stats = [
  {
    labelKey: 'dashboard.stats.activeWorkflows',
    value: '12',
    signalKey: 'dashboard.stats.activeWorkflowsSignal',
    tone: 'primary',
  },
  {
    labelKey: 'dashboard.stats.runningExecutions',
    value: '3',
    signalKey: 'dashboard.stats.runningExecutionsSignal',
    tone: 'cyan',
  },
  {
    labelKey: 'dashboard.stats.pendingFormTasks',
    value: '5',
    signalKey: 'dashboard.stats.pendingFormTasksSignal',
    tone: 'amber',
  },
  {
    labelKey: 'dashboard.stats.recordsCreated',
    value: '248',
    signalKey: 'dashboard.stats.recordsCreatedSignal',
    tone: 'coral',
  },
];

const traceItems = [
  {
    labelKey: 'dashboard.trace.customerIntake',
    metaKey: 'dashboard.trace.customerIntakeMeta',
    state: 'complete',
    icon: Database,
  },
  {
    labelKey: 'dashboard.trace.eligibilityModel',
    metaKey: 'dashboard.trace.eligibilityModelMeta',
    state: 'complete',
    icon: Workflow,
  },
  {
    labelKey: 'dashboard.trace.orderProcessing',
    metaKey: 'dashboard.trace.orderProcessingMeta',
    state: 'active',
    icon: Zap,
  },
  {
    labelKey: 'dashboard.trace.fulfillmentSync',
    metaKey: 'dashboard.trace.fulfillmentSyncMeta',
    state: 'pending',
    icon: Activity,
  },
] as const;

const modules = [
  {
    nameKey: 'dashboard.modules.identity',
    stateKey: 'dashboard.modules.healthy',
    valueKey: 'dashboard.modules.uptime',
    tone: 'ok',
    icon: CheckCircle2,
  },
  {
    nameKey: 'dashboard.modules.dataModeling',
    stateKey: 'dashboard.modules.syncing',
    valueKey: 'dashboard.modules.modelCount',
    tone: 'ok',
    icon: Database,
  },
  {
    nameKey: 'dashboard.modules.workflowEngine',
    stateKey: 'dashboard.modules.running',
    valueKey: 'dashboard.modules.activeCount',
    tone: 'ok',
    icon: Zap,
  },
  {
    nameKey: 'dashboard.modules.formBuilder',
    stateKey: 'dashboard.modules.attention',
    valueKey: 'dashboard.modules.pendingCount',
    tone: 'attention',
    icon: AlertTriangle,
  },
];

const events = [
  {
    titleKey: 'dashboard.events.completed',
    metaKey: 'dashboard.events.twoMinutesAgo',
    kind: 'complete',
  },
  {
    titleKey: 'dashboard.events.modelUpdated',
    metaKey: 'dashboard.events.nineteenMinutesAgo',
    kind: 'model',
  },
  {
    titleKey: 'dashboard.events.taskAssigned',
    metaKey: 'dashboard.events.oneHourAgo',
    kind: 'task',
  },
  {
    titleKey: 'dashboard.events.retryScheduled',
    metaKey: 'dashboard.events.twoHoursAgo',
    kind: 'retry',
  },
];

const toneClass: Record<string, string> = {
  primary: 'bg-primary text-primary-foreground',
  cyan: 'bg-[hsl(202_53%_43%)] text-white',
  amber: 'bg-accent text-accent-foreground',
  coral: 'bg-destructive text-destructive-foreground',
};

export function DashboardOverview() {
  const { t } = useTranslation();
  const trace: FlowTraceStep[] = traceItems.map((step) => ({
    id: step.labelKey,
    label: t(step.labelKey),
    meta: t(step.metaKey),
    state: step.state,
    icon: step.icon,
  }));

  return (
    <div className="max-w-7xl space-y-6">
      <section className="overflow-hidden rounded-lg border border-border bg-card/95 shadow-sm">
        <div className="grid min-h-[320px] xl:grid-cols-[1.05fr_0.95fr]">
          <div className="flex flex-col justify-between gap-8 p-6">
            <div className="space-y-5">
              <div className="flex flex-wrap items-center gap-2">
                <span className="border border-primary/20 bg-primary/10 px-2 py-1 text-xs font-medium text-primary">
                  {t('dashboard.environment')}
                </span>
                <span className="border border-border bg-muted px-2 py-1 text-xs text-muted-foreground">
                  {t('shell.organizationName')}
                </span>
              </div>
              <div className="space-y-2">
                <h1 className="text-2xl font-semibold tracking-tight text-foreground">
                  {t('dashboard.title')}
                </h1>
                <p className="max-w-2xl text-sm leading-6 text-muted-foreground">
                  {t('dashboard.body')}
                </p>
              </div>
            </div>

            <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
              {stats.map((stat) => (
                <div
                  key={stat.labelKey}
                  className="rounded-md border border-border bg-background/85 p-4"
                >
                  <div className="flex items-center justify-between gap-3">
                    <p className="text-xs text-muted-foreground">{t(stat.labelKey)}</p>
                    <span className={cn('size-2.5 rounded-sm', toneClass[stat.tone])} />
                  </div>
                  <p className="mt-3 text-2xl font-semibold text-foreground">{stat.value}</p>
                  <p className="mt-1 text-xs text-muted-foreground">{t(stat.signalKey)}</p>
                </div>
              ))}
            </div>
          </div>

          <div className="axis-grid-strong border-t border-border bg-background/80 p-6 xl:border-l xl:border-t-0">
            <div className="mb-5 flex items-center justify-between">
              <div>
                <p className="text-xs font-medium uppercase tracking-[0.18em] text-muted-foreground">
                  {t('dashboard.workflowTrace')}
                </p>
                <p className="mt-1 text-sm font-medium text-foreground">
                  {t('dashboard.workflowName')}
                </p>
              </div>
              <Button variant="cta" size="sm" disabled>
                <Plus className="size-3.5" aria-hidden />
                {t('dashboard.newWorkflow')}
              </Button>
            </div>
            <FlowTrace steps={trace} />
          </div>
        </div>
      </section>

      <div className="grid gap-6 xl:grid-cols-[0.9fr_1.1fr]">
        <section className="overflow-hidden rounded-lg border border-border bg-card shadow-sm">
          <div className="border-b border-border bg-muted/40 px-5 py-3">
            <h2 className="text-sm font-medium text-foreground">{t('dashboard.moduleHealth')}</h2>
          </div>
          <div className="divide-y divide-border">
            {modules.map((module) => {
              const Icon = module.icon;
              const attention = module.tone === 'attention';
              return (
                <div key={module.nameKey} className="flex items-center gap-3 px-5 py-4">
                  <span
                    className={cn(
                      'inline-flex size-8 items-center justify-center rounded-md border',
                      attention
                        ? 'border-destructive/25 bg-destructive/10 text-destructive'
                        : 'border-primary/20 bg-primary/10 text-primary',
                    )}
                  >
                    <Icon className="size-4" aria-hidden />
                  </span>
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm font-medium text-foreground">
                      {t(module.nameKey)}
                    </p>
                    <p className="text-xs text-muted-foreground">{t(module.stateKey)}</p>
                  </div>
                  <span className="text-xs font-medium text-muted-foreground">
                    {t(module.valueKey)}
                  </span>
                </div>
              );
            })}
          </div>
        </section>

        <section className="overflow-hidden rounded-lg border border-border bg-card shadow-sm">
          <div className="border-b border-border bg-muted/40 px-5 py-3">
            <h2 className="text-sm font-medium text-foreground">{t('dashboard.eventStream')}</h2>
          </div>
          <ul className="divide-y divide-border">
            {events.map((event) => (
              <li
                key={event.titleKey}
                className="grid grid-cols-[12px_1fr_auto] items-center gap-3 px-5 py-4"
              >
                <span
                  className={cn(
                    'size-2.5 rounded-sm',
                    event.kind === 'complete' && 'bg-primary',
                    event.kind === 'model' && 'bg-[hsl(202_53%_43%)]',
                    event.kind === 'task' && 'bg-accent',
                    event.kind === 'retry' && 'bg-destructive',
                  )}
                  aria-hidden
                />
                <span className="min-w-0 truncate text-sm text-foreground/90">
                  {t(event.titleKey)}
                </span>
                <span className="text-xs text-muted-foreground">{t(event.metaKey)}</span>
              </li>
            ))}
          </ul>
        </section>
      </div>
    </div>
  );
}
