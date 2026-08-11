import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate, useRouter, useRouterState } from '@tanstack/react-router';
import type { ReactNode } from 'react';
import { useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';

import { AppFooter } from '@/components/shared/AppFooter';
import { AppHeader } from '@/components/shared/AppHeader';
import { AuthenticatedFrame } from '@/components/shared/AuthenticatedFrame';
import { ManagedWindowHost } from '@/components/shared/ManagedWindowHost';
import {
  ManagedWindowProvider,
  type ManagedWindowRendererRegistry,
  useManagedWindowActions,
} from '@/components/shared/ManagedWindowManager';
import { ModuleNavigation } from '@/components/shared/ModuleNavigation';
import { PendingIndicator } from '@/components/shared/PendingIndicator';
import { Toaster } from '@/components/ui/sonner';
import { restoreBrowserSession, signOutUser } from '@/features/auth/api';
import { useAuthStore } from '@/features/auth/auth-store';
import { type CurrentUserProfile, dashboardQueryKeys } from '@/features/dashboard/api';
import { PreferencesProfileSync } from '@/features/preferences';
import {
  beginWorkspaceTransition,
  type CreatedOrganizationWorkspace,
  confirmWorkspaceTransition,
  type EligibleWorkspace,
  recoverWorkspaceTransition,
  type WorkspaceContextTransition,
  workspaceKeys,
} from '@/features/workspaces/api';
import type {
  WorkspaceChangeResult,
  WorkspaceContextState,
} from '@/features/workspaces/WorkspaceControl';
import { usePendingVisibility } from '@/hooks/usePendingVisibility';
import { invalidateClientRequestSession } from '@/lib/api';
import { managedWindowRenderers } from '@/lib/managed-window-registry';
import type { ModuleNavigationContribution } from '@/lib/module-navigation';
import { visibleModuleNavigationContributions } from '@/lib/module-navigation';
import {
  moduleNavigationAvailabilityKeys,
  moduleNavigationAvailabilityQueryOptions,
} from '@/lib/module-navigation-api';
import { moduleNavigationContributions } from '@/lib/module-navigation-registry';
import type { SurfaceIdFor } from '@/lib/ui-foundation';

export interface AppShellProps {
  children: ReactNode;
  navigationContributions?: readonly ModuleNavigationContribution[];
  surfaceId: SurfaceIdFor<'authenticated-frame'>;
  windowRenderers?: ManagedWindowRendererRegistry;
}

export function AppShell({
  children,
  navigationContributions = moduleNavigationContributions,
  surfaceId,
  windowRenderers = managedWindowRenderers,
}: AppShellProps) {
  return (
    <ManagedWindowProvider renderers={windowRenderers}>
      <AppShellContent navigationContributions={navigationContributions} surfaceId={surfaceId}>
        {children}
      </AppShellContent>
    </ManagedWindowProvider>
  );
}

function AppShellContent({
  children,
  navigationContributions,
  surfaceId,
}: {
  children: ReactNode;
  navigationContributions: readonly ModuleNavigationContribution[];
  surfaceId: SurfaceIdFor<'authenticated-frame'>;
}) {
  const navigate = useNavigate();
  const router = useRouter();
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const { clearWindows } = useManagedWindowActions();
  const pathname = useRouterState({ select: (s) => s.location.pathname });
  const markBrowserSessionGuest = useAuthStore((s) => s.markBrowserSessionGuest);
  const signOutPendingRef = useRef(false);
  const workspaceTransitionPendingRef = useRef(false);
  const [signingOut, setSigningOut] = useState(false);
  const [signOutError, setSignOutError] = useState(false);
  const [workspaceContext, setWorkspaceContext] = useState<WorkspaceContextState>({
    failure: null,
    phase: 'idle',
    targetWorkspaceId: null,
  });
  const workspaceContentBlocked =
    workspaceContext.phase === 'refreshing' ||
    (workspaceContext.phase === 'failed' &&
      (workspaceContext.failure === 'outcome-unknown' ||
        workspaceContext.failure === 'refresh-failed'));
  const showWorkspaceTransition = usePendingVisibility(
    workspaceContentBlocked && workspaceContext.phase === 'refreshing',
    'context-transition',
  );
  const requiresServerAvailability = navigationContributions.some(
    (contribution) => contribution.requiresServerAvailability,
  );
  const navigationAvailabilityQuery = useQuery({
    ...moduleNavigationAvailabilityQueryOptions(),
    enabled: requiresServerAvailability,
  });
  const navigationContext = {
    pathname,
    availableContributionIds: new Set(
      navigationAvailabilityQuery.data?.availableContributionIds ?? [],
    ),
  };
  const visibleNavigationItems = visibleModuleNavigationContributions(
    navigationContributions,
    navigationContext,
  );

  async function handleSignOut() {
    if (signOutPendingRef.current) return;

    signOutPendingRef.current = true;
    setSigningOut(true);
    setSignOutError(false);

    try {
      await signOutUser();
    } catch {
      signOutPendingRef.current = false;
      setSigningOut(false);
      setSignOutError(true);
      return;
    }

    invalidateClientRequestSession();
    clearWindows();
    markBrowserSessionGuest();
    queryClient.clear();
    void navigate({ to: '/sign-in', replace: true });
  }

  function setCachedWorkspace(workspaceId: string) {
    queryClient.setQueryData<CurrentUserProfile>(dashboardQueryKeys.currentUser(), (profile) =>
      profile
        ? {
            ...profile,
            workspaceId,
            workspaces: profile.workspaces?.map((workspace) => ({
              ...workspace,
              isCurrent: workspace.id === workspaceId,
            })),
          }
        : profile,
    );
    queryClient.setQueryData<EligibleWorkspace[]>(workspaceKeys.eligible, (workspaces) =>
      workspaces?.map((workspace) => ({
        ...workspace,
        isCurrent: workspace.workspaceId === workspaceId,
      })),
    );
  }

  async function synchronizeWorkspaceSession(
    preferredWorkspaceId?: string | null,
  ): Promise<string> {
    setWorkspaceContext((current) => ({ ...current, failure: null, phase: 'refreshing' }));
    invalidateClientRequestSession();
    clearWindows();
    if (preferredWorkspaceId) setCachedWorkspace(preferredWorkspaceId);

    if (!(await restoreBrowserSession({ force: true }))) {
      throw new Error('The authoritative browser session is unavailable.');
    }
    const sessionWorkspaceId = useAuthStore.getState().user?.workspaceId;
    const authoritativeWorkspaceId = sessionWorkspaceId ?? preferredWorkspaceId;
    if (!authoritativeWorkspaceId) {
      throw new Error('The authoritative Workspace is unavailable.');
    }

    queryClient.removeQueries({
      predicate: ({ queryKey }) =>
        !sameQueryKey(queryKey, dashboardQueryKeys.currentUser()) &&
        !sameQueryKey(queryKey, workspaceKeys.eligible) &&
        !sameQueryKey(queryKey, moduleNavigationAvailabilityKeys.all),
    });
    queryClient.getMutationCache().clear();
    setCachedWorkspace(authoritativeWorkspaceId);

    const [, , navigationResult] = await Promise.all([
      queryClient.invalidateQueries({
        queryKey: dashboardQueryKeys.currentUser(),
        exact: true,
      }),
      queryClient.invalidateQueries({ queryKey: workspaceKeys.eligible, exact: true }),
      requiresServerAvailability ? navigationAvailabilityQuery.refetch() : null,
    ]);
    if (navigationResult?.isError) {
      throw new Error('The target Workspace navigation is unavailable.');
    }
    await navigate({ to: '/dashboard', replace: true });
    await router.invalidate();
    return authoritativeWorkspaceId;
  }

  function finishWorkspaceChange(
    authoritativeWorkspaceId: string,
    targetWorkspaceId: string,
  ): WorkspaceChangeResult {
    if (authoritativeWorkspaceId === targetWorkspaceId) {
      setWorkspaceContext({ failure: null, phase: 'idle', targetWorkspaceId: null });
      return 'entered';
    }
    setWorkspaceContext({
      failure: 'switch-failed',
      phase: 'failed',
      targetWorkspaceId,
    });
    return 'not-entered';
  }

  async function handleWorkspaceChange(
    target: EligibleWorkspace | CreatedOrganizationWorkspace,
  ): Promise<WorkspaceChangeResult> {
    if (workspaceTransitionPendingRef.current) return 'unknown';
    workspaceTransitionPendingRef.current = true;
    setWorkspaceContext({
      failure: null,
      phase: 'switching',
      targetWorkspaceId: target.workspaceId,
    });

    try {
      invalidateClientRequestSession();
      await beginWorkspaceTransition(target.workspaceId);
    } catch {
      setWorkspaceContext({
        failure: 'switch-failed',
        phase: 'failed',
        targetWorkspaceId: target.workspaceId,
      });
      workspaceTransitionPendingRef.current = false;
      return 'not-entered';
    }

    let transition: WorkspaceContextTransition;
    try {
      try {
        transition = await confirmWorkspaceTransition();
      } catch {
        transition = await recoverWorkspaceTransition();
      }
    } catch {
      try {
        const authoritativeWorkspaceId = await synchronizeWorkspaceSession();
        return finishWorkspaceChange(authoritativeWorkspaceId, target.workspaceId);
      } catch {
        setWorkspaceContext({
          failure: 'outcome-unknown',
          phase: 'failed',
          targetWorkspaceId: target.workspaceId,
        });
        return 'unknown';
      } finally {
        workspaceTransitionPendingRef.current = false;
      }
    }

    try {
      const authoritativeWorkspaceId = await synchronizeWorkspaceSession(
        transition.authoritativeWorkspaceId,
      );
      return finishWorkspaceChange(authoritativeWorkspaceId, target.workspaceId);
    } catch {
      setWorkspaceContext({
        failure: 'refresh-failed',
        phase: 'failed',
        targetWorkspaceId: target.workspaceId,
      });
      return 'unknown';
    } finally {
      workspaceTransitionPendingRef.current = false;
    }
  }

  async function retryWorkspaceContext() {
    if (workspaceTransitionPendingRef.current) return;
    workspaceTransitionPendingRef.current = true;
    try {
      await synchronizeWorkspaceSession();
      setWorkspaceContext({ failure: null, phase: 'idle', targetWorkspaceId: null });
    } catch {
      setWorkspaceContext((current) => ({
        ...current,
        failure: 'refresh-failed',
        phase: 'failed',
      }));
    } finally {
      workspaceTransitionPendingRef.current = false;
    }
  }

  const workspaceSurfaceVisible =
    showWorkspaceTransition || (workspaceContext.phase === 'failed' && workspaceContentBlocked);
  const workspaceInteractionBlocked = workspaceContentBlocked || showWorkspaceTransition;

  return (
    <>
      <PreferencesProfileSync />
      <AuthenticatedFrame
        surfaceId={surfaceId}
        contentBlocked={workspaceInteractionBlocked}
        contentBusy={workspaceContext.phase === 'refreshing'}
        contentObscured={workspaceSurfaceVisible}
        contextSurfaceVisible={workspaceSurfaceVisible}
        contextSurface={
          showWorkspaceTransition ? (
            <PendingIndicator>{t('workspace.refreshingTitle')}</PendingIndicator>
          ) : undefined
        }
        header={
          <AppHeader
            onSignOut={handleSignOut}
            onRetryWorkspaceContext={retryWorkspaceContext}
            onWorkspaceChange={handleWorkspaceChange}
            signOutError={signOutError}
            signingOut={signingOut}
            workspaceContext={workspaceContext}
          />
        }
        navigation={<ModuleNavigation context={navigationContext} items={visibleNavigationItems} />}
        managedWindows={<ManagedWindowHost />}
        notifications={<Toaster />}
        footer={<AppFooter />}
      >
        {children}
      </AuthenticatedFrame>
    </>
  );
}

function sameQueryKey(queryKey: readonly unknown[], expected: readonly unknown[]): boolean {
  return (
    queryKey.length === expected.length &&
    queryKey.every((segment, index) => Object.is(segment, expected[index]))
  );
}
