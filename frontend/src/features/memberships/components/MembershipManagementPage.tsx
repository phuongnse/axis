import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { MailPlus, RefreshCw, UserMinus } from 'lucide-react';
import { type FormEvent, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { StatusBadge, type StatusBadgeTone } from '@/components/shared/StatusBadge';
import { StatusNotice } from '@/components/shared/StatusNotice';
import { Button } from '@/components/ui/button';
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { ApiError } from '@/lib/api';
import type { WorkspaceInvitationLifecycleDto } from '@/lib/api-generated';
import {
  inviteWorkspaceMember,
  resendWorkspaceInvitation,
  revokeWorkspaceInvitation,
  workspaceInvitationKeys,
  workspaceInvitationsQueryOptions,
} from '../api';

type WorkspaceRole = 'Administrator' | 'Member';
type Feedback = { tone: 'success' | 'destructive' | 'warning'; title: string; body: string };

export function MembershipManagementPage() {
  const { t, i18n } = useTranslation();
  const queryClient = useQueryClient();
  const [email, setEmail] = useState('');
  const [role, setRole] = useState<WorkspaceRole>('Member');
  const [emailError, setEmailError] = useState<string | null>(null);
  const [feedback, setFeedback] = useState<Feedback | null>(null);
  const invitationsQuery = useQuery(workspaceInvitationsQueryOptions());
  const invitations = invitationsQuery.data?.items ?? [];
  const dateFormatter = useMemo(
    () => new Intl.DateTimeFormat(i18n.language, { dateStyle: 'medium', timeStyle: 'short' }),
    [i18n.language],
  );

  const inviteMutation = useMutation({
    mutationFn: inviteWorkspaceMember,
    onSuccess: async (result) => {
      setEmail('');
      setEmailError(null);
      setFeedback({
        tone: 'success',
        title: t('memberships.inviteSucceeded'),
        body:
          result.outcome === 'ExistingMember'
            ? t('memberships.existingMember')
            : result.outcome === 'CanonicalPending'
              ? t('memberships.canonicalPending')
              : t('memberships.deliveryQueued'),
      });
      await queryClient.invalidateQueries({ queryKey: workspaceInvitationKeys.all });
    },
    onError: (error) => {
      if (invitationEmailCode(error)) {
        setEmailError(t('memberships.emailInvalid'));
        return;
      }
      setFeedback(problemFeedback(error, t));
    },
  });

  const resendMutation = useMutation({
    mutationFn: ({ invitationId, revision }: { invitationId: string; revision: number }) =>
      resendWorkspaceInvitation(invitationId, { expectedRevision: revision }),
    onSuccess: async () => {
      setFeedback({
        tone: 'success',
        title: t('memberships.resendSucceeded'),
        body: t('memberships.resendSucceededDescription'),
      });
      await queryClient.invalidateQueries({ queryKey: workspaceInvitationKeys.all });
    },
    onError: (error) => setFeedback(problemFeedback(error, t)),
  });

  const revokeMutation = useMutation({
    mutationFn: ({ invitationId, revision }: { invitationId: string; revision: number }) =>
      revokeWorkspaceInvitation(invitationId, { expectedRevision: revision }),
    onSuccess: async () => {
      setFeedback({
        tone: 'success',
        title: t('memberships.revokeSucceeded'),
        body: t('memberships.revokeSucceededDescription'),
      });
      await queryClient.invalidateQueries({ queryKey: workspaceInvitationKeys.all });
    },
    onError: (error) => setFeedback(problemFeedback(error, t)),
  });

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setFeedback(null);
    const normalized = email.trim();
    if (!normalized.includes('@')) {
      setEmailError(t('memberships.emailInvalid'));
      return;
    }
    setEmailError(null);
    inviteMutation.mutate({ email: normalized, requestedRole: role });
  }

  const mutationPending =
    inviteMutation.isPending || resendMutation.isPending || revokeMutation.isPending;

  return (
    <section className="flex h-full min-h-0 w-full min-w-0 flex-col gap-5 overflow-hidden p-4 sm:p-6">
      <header className="shrink-0">
        <h1 className="text-2xl font-semibold tracking-tight">{t('memberships.title')}</h1>
        <p className="mt-1 max-w-3xl text-sm text-muted-foreground">
          {t('memberships.description')}
        </p>
      </header>

      <form
        className="grid shrink-0 gap-4 border-b border-border pb-5 lg:grid-cols-[minmax(16rem,1fr)_16rem_auto] lg:items-end"
        onSubmit={submit}
        noValidate
      >
        <Field data-invalid={Boolean(emailError)}>
          <FieldLabel htmlFor="invitation-email">{t('memberships.email')}</FieldLabel>
          <Input
            id="invitation-email"
            type="email"
            autoComplete="email"
            value={email}
            aria-invalid={Boolean(emailError)}
            aria-describedby={emailError ? 'invitation-email-error' : 'invitation-email-help'}
            disabled={mutationPending}
            onChange={(event) => {
              setEmail(event.target.value);
              setEmailError(null);
            }}
          />
          {emailError ? (
            <FieldError id="invitation-email-error">{emailError}</FieldError>
          ) : (
            <FieldDescription id="invitation-email-help">
              {t('memberships.emailHelp')}
            </FieldDescription>
          )}
        </Field>
        <Field>
          <FieldLabel htmlFor="invitation-role">{t('memberships.role')}</FieldLabel>
          <Select
            value={role}
            onValueChange={(value) => setRole(value as WorkspaceRole)}
            disabled={mutationPending}
          >
            <SelectTrigger id="invitation-role" className="w-full">
              <SelectValue>{t(`memberships.role${role}`)}</SelectValue>
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="Member">{t('memberships.roleMember')}</SelectItem>
              <SelectItem value="Administrator">{t('memberships.roleAdministrator')}</SelectItem>
            </SelectContent>
          </Select>
          <FieldDescription>{t('memberships.roleHelp')}</FieldDescription>
        </Field>
        <Button type="submit" disabled={mutationPending || !email.trim()}>
          <MailPlus aria-hidden />
          {inviteMutation.isPending ? t('memberships.inviting') : t('memberships.invite')}
        </Button>
      </form>

      {feedback ? (
        <div className="shrink-0" aria-live="polite">
          <StatusNotice tone={feedback.tone} title={feedback.title}>
            {feedback.body}
          </StatusNotice>
        </div>
      ) : null}

      <div className="min-h-0 flex-1 overflow-auto">
        {invitationsQuery.isLoading ? (
          <p className="text-sm text-muted-foreground" aria-live="polite">
            {t('memberships.loading')}
          </p>
        ) : invitationsQuery.isError ? (
          <StatusNotice tone="destructive" title={t('memberships.loadFailed')}>
            <span>{t('memberships.loadFailedDescription')}</span>{' '}
            <Button
              type="button"
              size="sm"
              variant="outline"
              onClick={() => void invitationsQuery.refetch()}
            >
              {t('app.retry')}
            </Button>
          </StatusNotice>
        ) : invitations.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('memberships.empty')}</p>
        ) : (
          <Table aria-label={t('memberships.tableLabel')}>
            <TableHeader>
              <TableRow>
                <TableHead>{t('memberships.email')}</TableHead>
                <TableHead>{t('memberships.role')}</TableHead>
                <TableHead>{t('memberships.status')}</TableHead>
                <TableHead>{t('memberships.delivery')}</TableHead>
                <TableHead>{t('memberships.expires')}</TableHead>
                <TableHead>{t('memberships.actions')}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {invitations.map((invitation) => (
                <InvitationRow
                  key={invitation.invitationId}
                  invitation={invitation}
                  dateFormatter={dateFormatter}
                  disabled={mutationPending}
                  onResend={(invitationId, revision) =>
                    resendMutation.mutate({ invitationId, revision })
                  }
                  onRevoke={(invitationId, revision) =>
                    revokeMutation.mutate({ invitationId, revision })
                  }
                />
              ))}
            </TableBody>
          </Table>
        )}
      </div>
    </section>
  );
}

function InvitationRow({
  invitation,
  dateFormatter,
  disabled,
  onResend,
  onRevoke,
}: {
  invitation: WorkspaceInvitationLifecycleDto;
  dateFormatter: Intl.DateTimeFormat;
  disabled: boolean;
  onResend: (id: string, revision: number) => void;
  onRevoke: (id: string, revision: number) => void;
}) {
  const { t } = useTranslation();
  const pending = invitation.status === 'Pending';
  const invitationId = invitation.invitationId;
  const revision = invitation.revision;
  const actionable = pending && invitationId !== undefined && revision !== undefined;
  return (
    <TableRow>
      <TableCell>{invitation.recipientEmail ?? t('memberships.recipientRemoved')}</TableCell>
      <TableCell>{t(`memberships.role${invitation.requestedRole ?? 'Member'}`)}</TableCell>
      <TableCell>
        <StatusBadge tone={statusTone(invitation.status)}>
          {t(`memberships.status${invitation.status ?? 'Pending'}`)}
        </StatusBadge>
      </TableCell>
      <TableCell>
        <StatusBadge tone={deliveryTone(invitation.deliveryStatus)}>
          {t(`memberships.delivery${invitation.deliveryStatus ?? 'Pending'}`)}
        </StatusBadge>
      </TableCell>
      <TableCell>
        {invitation.expiresAt
          ? dateFormatter.format(new Date(invitation.expiresAt))
          : t('memberships.notAvailable')}
      </TableCell>
      <TableCell>
        {actionable ? (
          <div className="flex gap-2">
            <Button
              type="button"
              size="sm"
              variant="outline"
              disabled={disabled}
              onClick={() => onResend(invitationId, revision)}
            >
              <RefreshCw aria-hidden />
              {t('memberships.resend')}
            </Button>
            <Button
              type="button"
              size="sm"
              variant="destructive"
              disabled={disabled}
              onClick={() => onRevoke(invitationId, revision)}
            >
              <UserMinus aria-hidden />
              {t('memberships.revoke')}
            </Button>
          </div>
        ) : (
          t('memberships.noActions')
        )}
      </TableCell>
    </TableRow>
  );
}

function statusTone(status: string | undefined): StatusBadgeTone {
  if (status === 'Accepted') return 'success';
  if (status === 'Pending') return 'info';
  return 'muted';
}

function deliveryTone(status: string | undefined): StatusBadgeTone {
  if (status === 'Delivered') return 'success';
  if (status === 'Pending') return 'info';
  return 'muted';
}

function invitationEmailCode(error: unknown): boolean {
  if (!(error instanceof ApiError) || typeof error.data !== 'object' || error.data === null)
    return false;
  const codes = (error.data as { errorCodes?: Record<string, string[]> }).errorCodes?.email ?? [];
  return codes.includes('identity.invitation.emailInvalid');
}

function problemFeedback(error: unknown, t: (key: string) => string): Feedback {
  if (error instanceof ApiError && error.status === 429) {
    return {
      tone: 'warning',
      title: t('memberships.rateLimited'),
      body: t('memberships.rateLimitedDescription'),
    };
  }
  if (error instanceof ApiError && error.status === 409) {
    return {
      tone: 'warning',
      title: t('memberships.changed'),
      body: t('memberships.changedDescription'),
    };
  }
  if (error instanceof ApiError && error.status === 403) {
    return {
      tone: 'destructive',
      title: t('memberships.forbidden'),
      body: t('memberships.forbiddenDescription'),
    };
  }
  return {
    tone: 'destructive',
    title: t('memberships.actionFailed'),
    body: t('memberships.actionFailedDescription'),
  };
}
