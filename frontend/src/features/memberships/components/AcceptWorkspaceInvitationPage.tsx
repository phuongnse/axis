import { Link, useNavigate } from '@tanstack/react-router';
import { ArrowRight, LogOut, UserPlus } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { AsyncButton } from '@/components/shared/AsyncButton';
import { AsyncContent } from '@/components/shared/AsyncContent';
import { StatusNotice } from '@/components/shared/StatusNotice';
import { Button, buttonVariants } from '@/components/ui/button';
import { restoreBrowserSession, signOutUser } from '@/features/auth/api';
import { AuthCard } from '@/features/auth/components/AuthCard';
import { ApiError } from '@/lib/api';
import {
  acceptWorkspaceInvitation,
  awaitInvitationBootstrap,
  enterAcceptedWorkspace,
  reviewWorkspaceInvitation,
  type WorkspaceInvitationAcceptance,
  type WorkspaceInvitationReview,
} from '../invitation-acceptance-api';

type PageState =
  | { kind: 'loading' }
  | { kind: 'guest' }
  | { kind: 'invalid' }
  | { kind: 'wrong-account' }
  | { kind: 'review'; invitation: WorkspaceInvitationReview }
  | { kind: 'success'; acceptance: WorkspaceInvitationAcceptance }
  | { kind: 'error' };

async function loadPageState(): Promise<PageState> {
  try {
    if (!(await awaitInvitationBootstrap())) return { kind: 'invalid' };
    if (!(await restoreBrowserSession())) return { kind: 'guest' };
    return { kind: 'review', invitation: await reviewWorkspaceInvitation() };
  } catch (error) {
    if (error instanceof ApiError && error.status === 403) return { kind: 'wrong-account' };
    if (error instanceof ApiError && error.status < 500) return { kind: 'invalid' };
    return { kind: 'error' };
  }
}

export function AcceptWorkspaceInvitationPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const [state, setState] = useState<PageState>({ kind: 'loading' });
  const [actionPending, setActionPending] = useState(false);

  useEffect(() => {
    let active = true;
    void loadPageState().then((next) => {
      if (active) setState(next);
    });
    return () => {
      active = false;
    };
  }, []);

  async function accept() {
    setActionPending(true);
    try {
      setState({ kind: 'success', acceptance: await acceptWorkspaceInvitation() });
    } catch (error) {
      if (error instanceof ApiError && error.status === 403) {
        setState({ kind: 'wrong-account' });
      } else if (error instanceof ApiError && error.status < 500) {
        setState({ kind: 'invalid' });
      } else {
        setState({ kind: 'error' });
      }
    } finally {
      setActionPending(false);
    }
  }

  async function switchAccount() {
    setActionPending(true);
    try {
      await signOutUser();
      setState({ kind: 'guest' });
    } catch {
      setState({ kind: 'error' });
    } finally {
      setActionPending(false);
    }
  }

  async function enterWorkspace(workspaceId: string) {
    setActionPending(true);
    try {
      await enterAcceptedWorkspace(workspaceId);
      await navigate({ to: '/dashboard', replace: true });
    } catch {
      setState({ kind: 'error' });
      setActionPending(false);
    }
  }

  if (state.kind === 'loading') {
    return (
      <AuthCard title={t('invitationAccept.loadingTitle')}>
        <AsyncContent pending pendingLabel={t('invitationAccept.loading')}>
          <span />
        </AsyncContent>
      </AuthCard>
    );
  }

  if (state.kind === 'guest') {
    return (
      <AuthCard title={t('invitationAccept.authenticateTitle')}>
        <div className="space-y-4">
          <StatusNotice tone="info">{t('invitationAccept.authenticateBody')}</StatusNotice>
          <Link to="/sign-in" className={buttonVariants({ size: 'lg', className: 'w-full' })}>
            {t('auth.signIn')}
          </Link>
          <Link
            to="/register"
            className={buttonVariants({ variant: 'outline', size: 'lg', className: 'w-full' })}
          >
            <UserPlus aria-hidden />
            {t('auth.createAccount')}
          </Link>
        </div>
      </AuthCard>
    );
  }

  if (state.kind === 'wrong-account') {
    return (
      <AuthCard title={t('invitationAccept.wrongAccountTitle')}>
        <div className="space-y-4">
          <StatusNotice tone="warning">{t('invitationAccept.wrongAccountBody')}</StatusNotice>
          <AsyncButton
            type="button"
            size="lg"
            className="w-full"
            disabled={actionPending}
            onClick={() => void switchAccount()}
            icon={<LogOut aria-hidden />}
            pending={actionPending}
            pendingLabel={t('invitationAccept.switchingAccount')}
          >
            {t('invitationAccept.useAnotherAccount')}
          </AsyncButton>
        </div>
      </AuthCard>
    );
  }

  if (state.kind === 'invalid') {
    return (
      <AuthCard title={t('invitationAccept.invalidTitle')}>
        <StatusNotice tone="warning">{t('invitationAccept.invalidBody')}</StatusNotice>
      </AuthCard>
    );
  }

  if (state.kind === 'error') {
    return (
      <AuthCard title={t('invitationAccept.errorTitle')}>
        <div className="space-y-4">
          <StatusNotice tone="destructive">{t('invitationAccept.errorBody')}</StatusNotice>
          <Button type="button" variant="outline" onClick={() => window.location.reload()}>
            {t('app.retry')}
          </Button>
        </div>
      </AuthCard>
    );
  }

  if (state.kind === 'success') {
    return (
      <AuthCard title={t('invitationAccept.successTitle')}>
        <div className="space-y-4">
          <StatusNotice tone="success">{t('invitationAccept.successBody')}</StatusNotice>
          <AsyncButton
            type="button"
            size="lg"
            className="w-full"
            disabled={actionPending}
            onClick={() => void enterWorkspace(state.acceptance.workspaceId)}
            icon={<ArrowRight aria-hidden />}
            pending={actionPending}
            pendingLabel={t('invitationAccept.enteringWorkspace')}
          >
            {t('invitationAccept.enterWorkspace')}
          </AsyncButton>
        </div>
      </AuthCard>
    );
  }

  const expiresAt = new Intl.DateTimeFormat(i18n.language, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(state.invitation.expiresAt));
  return (
    <AuthCard title={t('invitationAccept.reviewTitle')}>
      <div className="space-y-5">
        <dl className="grid grid-cols-2 gap-x-4 gap-y-3 text-sm">
          <dt className="text-muted-foreground">{t('invitationAccept.organization')}</dt>
          <dd className="font-medium">{state.invitation.organizationName}</dd>
          <dt className="text-muted-foreground">{t('invitationAccept.workspace')}</dt>
          <dd className="font-medium">{state.invitation.workspaceName}</dd>
          <dt className="text-muted-foreground">{t('invitationAccept.inviter')}</dt>
          <dd>{state.invitation.inviterName}</dd>
          <dt className="text-muted-foreground">{t('invitationAccept.role')}</dt>
          <dd>{t(`memberships.role${state.invitation.requestedRole}`)}</dd>
          <dt className="text-muted-foreground">{t('invitationAccept.expires')}</dt>
          <dd>{expiresAt}</dd>
        </dl>
        <AsyncButton
          type="button"
          size="lg"
          className="w-full"
          disabled={actionPending}
          onClick={() => void accept()}
          icon={<UserPlus aria-hidden />}
          pending={actionPending}
          pendingLabel={t('invitationAccept.accepting')}
        >
          {t('invitationAccept.accept')}
        </AsyncButton>
      </div>
    </AuthCard>
  );
}
