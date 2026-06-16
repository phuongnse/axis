import type { LucideIcon } from 'lucide-react';
import {
  AlertCircle,
  Building2,
  CheckCircle2,
  RefreshCw,
  ShieldCheck,
  Users,
  Workflow,
  Zap,
} from 'lucide-react';
import { useTranslation } from 'react-i18next';

import { Button } from '@/components/ui/button';
import { type UsageStats, useWorkspaceStart, type WorkspaceSettings } from '@/features/workspace';
import { cn } from '@/lib/utils';

interface UsageCardProps {
  label: string;
  value: string;
  limit: string;
  percent: number | null;
  icon: LucideIcon;
}

function formatNumber(value: number | null | undefined, locale: string): string {
  return typeof value === 'number' ? new Intl.NumberFormat(locale).format(value) : '-';
}

function formatLimit(
  limit: number | null | undefined,
  unlimitedLabel: string,
  locale: string,
): string {
  return typeof limit === 'number' ? new Intl.NumberFormat(locale).format(limit) : unlimitedLabel;
}

function usagePercent(used: number | null | undefined, limit: number | null | undefined) {
  if (typeof used !== 'number' || typeof limit !== 'number' || limit <= 0) return null;
  return Math.min(100, Math.round((used / limit) * 100));
}

function usageCards(
  usage: UsageStats | undefined,
  unlimitedLabel: string,
  locale: string,
): UsageCardProps[] {
  return [
    {
      label: 'dashboard.usage.users',
      value: formatNumber(usage?.usersUsed, locale),
      limit: formatLimit(usage?.usersLimit, unlimitedLabel, locale),
      percent: usagePercent(usage?.usersUsed, usage?.usersLimit),
      icon: Users,
    },
    {
      label: 'dashboard.usage.workflows',
      value: formatNumber(usage?.workflowsUsed, locale),
      limit: formatLimit(usage?.workflowsLimit, unlimitedLabel, locale),
      percent: usagePercent(usage?.workflowsUsed, usage?.workflowsLimit),
      icon: Workflow,
    },
    {
      label: 'dashboard.usage.executions',
      value: formatNumber(usage?.executionsUsedThisMonth, locale),
      limit: formatLimit(usage?.executionsPerMonthLimit, unlimitedLabel, locale),
      percent: usagePercent(usage?.executionsUsedThisMonth, usage?.executionsPerMonthLimit),
      icon: Zap,
    },
  ];
}

function UsageCard({ label, value, limit, percent, icon: Icon }: UsageCardProps) {
  const { t } = useTranslation();

  return (
    <div className="rounded-lg border border-border bg-background/80 p-4 shadow-sm">
      <div className="flex items-center justify-between gap-3">
        <p className="text-sm font-medium text-foreground">{t(label)}</p>
        <span className="inline-flex size-8 items-center justify-center rounded-md border border-primary/20 bg-primary/10 text-primary">
          <Icon className="size-4" aria-hidden />
        </span>
      </div>
      <p className="mt-4 text-2xl font-semibold text-foreground">{value}</p>
      <p className="mt-1 text-xs text-muted-foreground">{t('dashboard.usage.limit', { limit })}</p>
      <progress
        className={cn(
          'mt-4 h-1.5 w-full overflow-hidden rounded-full bg-muted accent-primary [&::-moz-progress-bar]:rounded-full [&::-moz-progress-bar]:bg-primary [&::-webkit-progress-bar]:bg-muted [&::-webkit-progress-bar]:rounded-full [&::-webkit-progress-value]:rounded-full [&::-webkit-progress-value]:bg-primary',
          percent === null && 'opacity-40',
        )}
        value={percent ?? 100}
        max={100}
        aria-label={t('dashboard.usage.progress', { label: t(label) })}
      />
    </div>
  );
}

function WorkspaceSummary({
  settings,
  canReadSettings,
  hasWorkspace,
}: {
  settings: WorkspaceSettings | undefined;
  canReadSettings: boolean;
  hasWorkspace: boolean;
}) {
  const { t } = useTranslation();

  const rows = [
    {
      label: t('dashboard.summary.workspace'),
      value:
        settings?.name ??
        (hasWorkspace ? t('dashboard.summary.workspaceLinked') : t('dashboard.summary.none')),
      icon: Building2,
    },
    {
      label: t('dashboard.summary.status'),
      value:
        settings?.status ??
        (hasWorkspace ? t('dashboard.summary.limited') : t('dashboard.summary.notLinked')),
      icon: CheckCircle2,
    },
    {
      label: t('dashboard.summary.plan'),
      value:
        settings?.planName ??
        (canReadSettings ? t('dashboard.summary.loading') : t('dashboard.summary.unavailable')),
      icon: ShieldCheck,
    },
  ];

  return (
    <section className="rounded-lg border border-border bg-card p-5 shadow-sm">
      <h2 className="text-sm font-semibold text-foreground">{t('dashboard.summary.title')}</h2>
      <div className="mt-4 space-y-3">
        {rows.map((row) => {
          const Icon = row.icon;
          return (
            <div key={row.label} className="flex items-start gap-3">
              <span className="inline-flex size-8 shrink-0 items-center justify-center rounded-md border border-border bg-muted text-muted-foreground">
                <Icon className="size-4" aria-hidden />
              </span>
              <div className="min-w-0">
                <p className="text-xs text-muted-foreground">{row.label}</p>
                <p className="truncate text-sm font-medium text-foreground">{row.value}</p>
              </div>
            </div>
          );
        })}
      </div>
    </section>
  );
}

function LoadingDashboard() {
  return (
    <div className="max-w-6xl space-y-6">
      <div className="rounded-lg border border-border bg-card p-6 shadow-sm sm:p-8">
        <div className="h-4 w-32 animate-pulse rounded bg-muted" />
        <div className="mt-8 h-9 w-80 max-w-full animate-pulse rounded bg-muted" />
        <div className="mt-4 h-4 w-[28rem] max-w-full animate-pulse rounded bg-muted" />
        <div className="mt-8 grid gap-3 sm:grid-cols-3">
          <div className="h-36 animate-pulse rounded-lg bg-muted" />
          <div className="h-36 animate-pulse rounded-lg bg-muted" />
          <div className="h-36 animate-pulse rounded-lg bg-muted" />
        </div>
      </div>
    </div>
  );
}

function ErrorDashboard({ onRetry }: { onRetry: () => void }) {
  const { t } = useTranslation();

  return (
    <div className="max-w-3xl rounded-lg border border-destructive/30 bg-card p-6 shadow-sm">
      <div className="flex items-start gap-3">
        <span className="inline-flex size-9 shrink-0 items-center justify-center rounded-md border border-destructive/25 bg-destructive/10 text-destructive">
          <AlertCircle className="size-4" aria-hidden />
        </span>
        <div className="min-w-0">
          <h1 className="text-xl font-semibold text-foreground">{t('dashboard.errorTitle')}</h1>
          <p className="mt-2 text-sm leading-6 text-muted-foreground">{t('dashboard.errorBody')}</p>
          <Button type="button" variant="outline" className="mt-5" onClick={onRetry}>
            <RefreshCw className="size-4" aria-hidden />
            {t('dashboard.retry')}
          </Button>
        </div>
      </div>
    </div>
  );
}

export function DashboardOverview() {
  const { t, i18n } = useTranslation();
  const { profileQuery, workspaceSettingsQuery, canReadSettings, hasWorkspace } =
    useWorkspaceStart();

  if (profileQuery.isLoading) {
    return <LoadingDashboard />;
  }

  if (profileQuery.isError) {
    return <ErrorDashboard onRetry={() => void profileQuery.refetch()} />;
  }

  const profile = profileQuery.data;
  const settings = workspaceSettingsQuery.data;
  const showUsage = hasWorkspace && canReadSettings && settings;
  const title = settings?.name ?? t('dashboard.title');
  const body = !hasWorkspace
    ? t('dashboard.noWorkspaceBody', { email: profile?.email ?? t('shell.userFallback') })
    : canReadSettings
      ? t('dashboard.bodyWithSettings')
      : t('dashboard.bodyLimited');

  return (
    <div className="max-w-6xl space-y-6">
      <section className="overflow-hidden rounded-lg border border-border bg-card shadow-sm">
        <div className="grid gap-0 lg:grid-cols-[minmax(0,1fr)_22rem]">
          <div className="p-6 sm:p-8">
            <div className="flex flex-wrap items-center gap-2">
              <span className="rounded-md border border-primary/20 bg-primary/10 px-2 py-1 text-xs font-medium text-primary">
                {hasWorkspace ? t('dashboard.workspaceLinked') : t('dashboard.accountReady')}
              </span>
              {settings?.planName ? (
                <span className="rounded-md border border-border bg-muted px-2 py-1 text-xs text-muted-foreground">
                  {settings.planName}
                </span>
              ) : null}
            </div>

            <div className="mt-8 max-w-3xl space-y-3">
              <h1 className="text-2xl font-semibold tracking-tight text-foreground sm:text-3xl">
                {title}
              </h1>
              <p className="max-w-2xl text-sm leading-6 text-muted-foreground">{body}</p>
            </div>
          </div>

          <div className="border-t border-border bg-muted/30 p-6 sm:p-8 lg:border-l lg:border-t-0">
            <p className="text-xs font-medium uppercase tracking-[0.18em] text-muted-foreground">
              {t('dashboard.session.title')}
            </p>
            <div className="mt-6 space-y-4">
              <div>
                <p className="text-sm font-medium text-foreground">
                  {profile?.fullName || profile?.email || t('shell.userFallback')}
                </p>
                {profile?.email ? (
                  <p className="mt-1 text-xs text-muted-foreground">{profile.email}</p>
                ) : null}
              </div>
              <div className="rounded-lg border border-border bg-background/75 p-4">
                <div className="flex items-center gap-2 text-sm font-medium text-foreground">
                  <CheckCircle2 className="size-4 text-primary" aria-hidden />
                  {profile?.isActive
                    ? t('dashboard.session.active')
                    : t('dashboard.session.inactive')}
                </div>
                <p className="mt-2 text-xs leading-5 text-muted-foreground">
                  {hasWorkspace
                    ? t('dashboard.session.workspaceLinked')
                    : t('dashboard.session.noWorkspace')}
                </p>
              </div>
            </div>
          </div>
        </div>
      </section>

      {workspaceSettingsQuery.isError ? (
        <section className="rounded-lg border border-destructive/30 bg-card p-5 shadow-sm">
          <div className="flex items-start gap-3">
            <AlertCircle className="mt-0.5 size-4 shrink-0 text-destructive" aria-hidden />
            <div>
              <h2 className="text-sm font-semibold text-foreground">
                {t('dashboard.settingsErrorTitle')}
              </h2>
              <p className="mt-1 text-sm text-muted-foreground">
                {t('dashboard.settingsErrorBody')}
              </p>
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="mt-4"
                onClick={() => void workspaceSettingsQuery.refetch()}
              >
                <RefreshCw className="size-3.5" aria-hidden />
                {t('dashboard.retry')}
              </Button>
            </div>
          </div>
        </section>
      ) : null}

      {showUsage ? (
        <section className="space-y-3">
          <div>
            <h2 className="text-base font-semibold text-foreground">
              {t('dashboard.usage.title')}
            </h2>
            <p className="mt-1 text-sm text-muted-foreground">{t('dashboard.usage.body')}</p>
          </div>
          <div className="grid gap-3 sm:grid-cols-3">
            {usageCards(settings.usage, t('dashboard.usage.unlimited'), i18n.language).map(
              (card) => (
                <UsageCard key={card.label} {...card} />
              ),
            )}
          </div>
        </section>
      ) : (
        <WorkspaceSummary
          settings={settings}
          canReadSettings={canReadSettings}
          hasWorkspace={hasWorkspace}
        />
      )}
    </div>
  );
}
