import { useQuery } from '@tanstack/react-query';
import { AlertCircle, CheckCircle2, RefreshCw, UserRound } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import {
  type CurrentUserProfile,
  dashboardQueryKeys,
  getCurrentUserProfile,
} from '@/features/dashboard/api';

function LoadingDashboard() {
  return (
    <div className="max-w-4xl space-y-6">
      <Card size="lg">
        <CardContent>
          <Skeleton className="h-4 w-32" />
          <Skeleton className="mt-8 h-9 w-80 max-w-full" />
          <Skeleton className="mt-4 h-4 w-[28rem] max-w-full" />
        </CardContent>
      </Card>
    </div>
  );
}

function ErrorDashboard({ onRetry }: { onRetry: () => void }) {
  const { t } = useTranslation();

  return (
    <Card size="lg" variant="destructive" className="max-w-3xl">
      <CardContent>
        <div className="flex items-start gap-3">
          <span className="inline-flex size-9 shrink-0 items-center justify-center rounded-md border border-destructive/25 bg-destructive/10 text-destructive">
            <AlertCircle className="size-4" aria-hidden />
          </span>
          <div className="min-w-0">
            <h1 className="text-xl font-semibold text-foreground">{t('dashboard.unableTitle')}</h1>
            <p className="mt-2 text-sm leading-6 text-muted-foreground">
              {t('dashboard.unableBody')}
            </p>
            <Button type="button" variant="outline" className="mt-5" onClick={onRetry}>
              <RefreshCw className="size-4" aria-hidden />
              {t('dashboard.retry')}
            </Button>
          </div>
        </div>
      </CardContent>
    </Card>
  );
}

function ProfileSummary({ profile }: { profile: CurrentUserProfile }) {
  const { t } = useTranslation();
  const currentWorkspace = profile.workspaces?.find((workspace) => workspace.isCurrent);

  return (
    <Card>
      <CardContent>
        <h2 className="text-sm font-semibold text-foreground">{t('dashboard.accountDetails')}</h2>
        <dl className="mt-4 grid gap-4 text-sm sm:grid-cols-2">
          <div>
            <dt className="text-xs text-muted-foreground">{t('dashboard.email')}</dt>
            <dd className="mt-1 font-medium text-foreground">{profile.email}</dd>
          </div>
          <div>
            <dt className="text-xs text-muted-foreground">{t('dashboard.status')}</dt>
            <dd className="mt-1 font-medium text-foreground">
              {profile.isActive ? t('dashboard.statusActive') : t('dashboard.inactive')}
            </dd>
          </div>
          <div>
            <dt className="text-xs text-muted-foreground">{t('dashboard.workspace')}</dt>
            <dd className="mt-1 font-medium text-foreground">
              {currentWorkspace?.name ?? t('dashboard.personalWorkspace')}
            </dd>
          </div>
          <div>
            <dt className="text-xs text-muted-foreground">{t('dashboard.workspaceType')}</dt>
            <dd className="mt-1 font-medium text-foreground">
              {currentWorkspace?.type ?? t('dashboard.personal')}
            </dd>
          </div>
        </dl>
      </CardContent>
    </Card>
  );
}

export function DashboardOverview() {
  const { t } = useTranslation();
  const profileQuery = useQuery({
    queryKey: dashboardQueryKeys.currentUser(),
    queryFn: getCurrentUserProfile,
  });

  if (profileQuery.isLoading) {
    return <LoadingDashboard />;
  }

  if (profileQuery.isError || !profileQuery.data) {
    return <ErrorDashboard onRetry={() => void profileQuery.refetch()} />;
  }

  const profile = profileQuery.data;

  return (
    <div className="max-w-4xl space-y-6">
      <Card size="flush">
        <div className="grid gap-0 lg:grid-cols-[minmax(0,1fr)_18rem]">
          <div className="p-6 sm:p-8">
            <Badge variant="primaryOutline">
              <CheckCircle2 className="size-3.5" aria-hidden />
              {t('dashboard.accountReady')}
            </Badge>

            <div className="mt-8 max-w-3xl space-y-3">
              <h1 className="text-2xl font-semibold tracking-tight text-foreground sm:text-3xl">
                {profile.fullName || profile.email}
              </h1>
              <p className="max-w-2xl text-sm leading-6 text-muted-foreground">
                {t('dashboard.readyBody')}
              </p>
            </div>
          </div>

          <div className="border-t border-border bg-muted/30 p-6 sm:p-8 lg:border-l lg:border-t-0">
            <p className="text-xs font-medium uppercase tracking-[0.18em] text-muted-foreground">
              {t('dashboard.session')}
            </p>
            <div className="mt-6 flex items-center gap-3">
              <span className="inline-flex size-9 shrink-0 items-center justify-center rounded-md border border-border bg-background text-muted-foreground">
                <UserRound className="size-4" aria-hidden />
              </span>
              <div className="min-w-0">
                <p className="truncate text-sm font-medium text-foreground">
                  {profile.fullName || profile.email}
                </p>
                <p className="mt-1 truncate text-xs text-muted-foreground">{profile.email}</p>
              </div>
            </div>
          </div>
        </div>
      </Card>

      <ProfileSummary profile={profile} />
    </div>
  );
}
