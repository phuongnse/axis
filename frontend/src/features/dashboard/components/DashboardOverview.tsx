import { useQuery } from '@tanstack/react-query';
import { AlertCircle, CheckCircle2, RefreshCw, UserRound } from 'lucide-react';

import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import {
  type CurrentUserProfile,
  dashboardQueryKeys,
  getCurrentUserProfile,
} from '@/features/dashboard/api';

function LoadingDashboard() {
  return (
    <div className="max-w-4xl space-y-6">
      <Card className="p-6 sm:p-8">
        <Skeleton className="h-4 w-32" />
        <Skeleton className="mt-8 h-9 w-80 max-w-full" />
        <Skeleton className="mt-4 h-4 w-[28rem] max-w-full" />
      </Card>
    </div>
  );
}

function ErrorDashboard({ onRetry }: { onRetry: () => void }) {
  return (
    <Card className="max-w-3xl border-destructive/30 p-6">
      <div className="flex items-start gap-3">
        <span className="inline-flex size-9 shrink-0 items-center justify-center rounded-md border border-destructive/25 bg-destructive/10 text-destructive">
          <AlertCircle className="size-4" aria-hidden />
        </span>
        <div className="min-w-0">
          <h1 className="text-xl font-semibold text-foreground">Unable to load account</h1>
          <p className="mt-2 text-sm leading-6 text-muted-foreground">
            Refresh the session and try again.
          </p>
          <Button type="button" variant="outline" className="mt-5" onClick={onRetry}>
            <RefreshCw className="size-4" aria-hidden />
            Retry
          </Button>
        </div>
      </div>
    </Card>
  );
}

function ProfileSummary({ profile }: { profile: CurrentUserProfile }) {
  const currentWorkspace = profile.workspaces?.find((workspace) => workspace.isCurrent);

  return (
    <Card className="p-5">
      <h2 className="text-sm font-semibold text-foreground">Account details</h2>
      <dl className="mt-4 grid gap-4 text-sm sm:grid-cols-2">
        <div>
          <dt className="text-xs text-muted-foreground">Email</dt>
          <dd className="mt-1 font-medium text-foreground">{profile.email}</dd>
        </div>
        <div>
          <dt className="text-xs text-muted-foreground">Status</dt>
          <dd className="mt-1 font-medium text-foreground">
            {profile.isActive ? 'Active' : 'Inactive'}
          </dd>
        </div>
        <div>
          <dt className="text-xs text-muted-foreground">Workspace</dt>
          <dd className="mt-1 font-medium text-foreground">
            {currentWorkspace?.name ?? 'Personal workspace'}
          </dd>
        </div>
        <div>
          <dt className="text-xs text-muted-foreground">Workspace type</dt>
          <dd className="mt-1 font-medium text-foreground">
            {currentWorkspace?.type ?? 'Personal'}
          </dd>
        </div>
      </dl>
    </Card>
  );
}

export function DashboardOverview() {
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
      <Card className="overflow-hidden p-0">
        <div className="grid gap-0 lg:grid-cols-[minmax(0,1fr)_18rem]">
          <div className="p-6 sm:p-8">
            <div className="inline-flex items-center gap-2 rounded-md border border-primary/20 bg-primary/10 px-2.5 py-1 text-xs font-medium text-primary">
              <CheckCircle2 className="size-3.5" aria-hidden />
              Account ready
            </div>

            <div className="mt-8 max-w-3xl space-y-3">
              <h1 className="text-2xl font-semibold tracking-tight text-foreground sm:text-3xl">
                {profile.fullName || profile.email}
              </h1>
              <p className="max-w-2xl text-sm leading-6 text-muted-foreground">
                Your email is verified and your Axis account is ready to use.
              </p>
            </div>
          </div>

          <div className="border-t border-border bg-muted/30 p-6 sm:p-8 lg:border-l lg:border-t-0">
            <p className="text-xs font-medium uppercase tracking-[0.18em] text-muted-foreground">
              Session
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
