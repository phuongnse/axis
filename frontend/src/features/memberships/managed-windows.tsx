import { useMutation, useQueryClient } from '@tanstack/react-query';
import { MailPlus, RefreshCw, ShieldCheck, ShieldMinus, UserMinus } from 'lucide-react';
import { type FormEvent, useEffect, useId, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  ManagedDialog,
  ManagedDialogAction,
  ManagedDialogAsyncAction,
  ManagedDialogBody,
} from '@/components/shared/ManagedDialog';
import type {
  ManagedWindowDescriptor,
  ManagedWindowRendererProps,
  ManagedWindowRendererRegistry,
} from '@/components/shared/ManagedWindowManager';
import { useCurrentManagedWindow } from '@/components/shared/ManagedWindowManager';
import { StatusBadge } from '@/components/shared/StatusBadge';
import { StatusNotice, type StatusNoticeTone } from '@/components/shared/StatusNotice';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from '@/components/ui/alert-dialog';
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { ApiError } from '@/lib/api';
import type {
  WorkspaceInvitationLifecycleDto,
  WorkspaceProductBuilderDto,
} from '@/lib/api-generated';
import {
  grantWorkspaceProductBuilder,
  inviteWorkspaceMember,
  resendWorkspaceInvitation,
  revokeWorkspaceInvitation,
  revokeWorkspaceProductBuilder,
  workspaceInvitationKeys,
  workspaceProductBuilderKeys,
  workspaceProductBuildersQueryOptions,
} from './api';

const MEMBERSHIP_INVITE_KIND = 'memberships.invite';
const MEMBERSHIP_INVITATION_KIND = 'memberships.invitation';
const MEMBERSHIP_PRODUCT_BUILDER_KIND = 'memberships.product-builder';
type WorkspaceRole = 'Administrator' | 'Member';
type Feedback = { tone: StatusNoticeTone; title: string; body: string };

export function membershipInviteWindowDescriptor(title: string): ManagedWindowDescriptor {
  return {
    id: 'memberships:invite',
    kind: MEMBERSHIP_INVITE_KIND,
    resourceKey: 'invite',
    title,
  };
}

export function membershipInvitationWindowDescriptor(
  invitation: WorkspaceInvitationLifecycleDto,
  title: string,
): ManagedWindowDescriptor {
  return {
    id: `memberships:${invitation.invitationId}`,
    kind: MEMBERSHIP_INVITATION_KIND,
    resourceKey: invitation.invitationId ?? title,
    title,
    payload: invitation,
  };
}

export function membershipProductBuilderWindowDescriptor(
  member: WorkspaceProductBuilderDto,
  title: string,
): ManagedWindowDescriptor {
  return {
    id: `memberships:product-builder:${member.userId}`,
    kind: MEMBERSHIP_PRODUCT_BUILDER_KIND,
    resourceKey: member.userId ?? title,
    title,
    payload: member,
  };
}

export const membershipsManagedWindowRenderers: ManagedWindowRendererRegistry = {
  [MEMBERSHIP_INVITE_KIND]: MembershipInviteWindowRenderer,
  [MEMBERSHIP_INVITATION_KIND]: MembershipInvitationWindowRenderer,
  [MEMBERSHIP_PRODUCT_BUILDER_KIND]: MembershipProductBuilderWindowRenderer,
};

function MembershipInviteWindowRenderer() {
  const { windowId, closeWindow } = useCurrentManagedWindow();
  return <MembershipInviteDialog onClose={() => closeWindow(windowId)} />;
}

function MembershipInvitationWindowRenderer({ descriptor }: ManagedWindowRendererProps) {
  const { t } = useTranslation();
  const { windowId, closeWindow } = useCurrentManagedWindow();
  const invitation = readInvitation(descriptor);
  if (!invitation) {
    return (
      <ManagedDialog
        surfaceId="membership-windows"
        open
        title={descriptor.title}
        onOpenChange={(open) => {
          if (!open) closeWindow(windowId);
        }}
        footer={
          <ManagedDialogAction
            type="button"
            variant="outline"
            onClick={() => closeWindow(windowId)}
          >
            {t('app.close')}
          </ManagedDialogAction>
        }
      >
        <ManagedDialogBody>
          <p role="alert">{t('dialog.unavailable')}</p>
        </ManagedDialogBody>
      </ManagedDialog>
    );
  }
  return (
    <MembershipInvitationDialog
      initialInvitation={invitation}
      onClose={() => closeWindow(windowId)}
    />
  );
}

function MembershipProductBuilderWindowRenderer({ descriptor }: ManagedWindowRendererProps) {
  const { t } = useTranslation();
  const { windowId, closeWindow } = useCurrentManagedWindow();
  const member = readProductBuilder(descriptor);
  if (!member) {
    return (
      <ManagedDialog
        surfaceId="membership-windows"
        open
        title={descriptor.title}
        onOpenChange={(open) => {
          if (!open) closeWindow(windowId);
        }}
        footer={
          <ManagedDialogAction
            type="button"
            variant="outline"
            onClick={() => closeWindow(windowId)}
          >
            {t('app.close')}
          </ManagedDialogAction>
        }
      >
        <ManagedDialogBody>
          <p role="alert">{t('dialog.unavailable')}</p>
        </ManagedDialogBody>
      </ManagedDialog>
    );
  }
  return (
    <MembershipProductBuilderDialog initialMember={member} onClose={() => closeWindow(windowId)} />
  );
}

function MembershipProductBuilderDialog({
  initialMember,
  onClose,
}: {
  initialMember: WorkspaceProductBuilderDto;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [member, setMember] = useState(initialMember);
  const [feedback, setFeedback] = useState<Feedback | null>(null);
  useEffect(() => {
    if ((initialMember.membershipRevision ?? 0) >= (member.membershipRevision ?? 0)) {
      setMember(initialMember);
    }
  }, [initialMember, member.membershipRevision]);

  const mutation = useMutation({
    mutationFn: ({
      enabled,
      userId,
      revision,
    }: {
      enabled: boolean;
      userId: string;
      revision: number;
    }) =>
      enabled
        ? grantWorkspaceProductBuilder(userId, { expectedRevision: revision })
        : revokeWorkspaceProductBuilder(userId, { expectedRevision: revision }),
    onSuccess: async (result) => {
      setMember(result);
      setFeedback({
        tone: 'success',
        title: result.isProductBuilder
          ? t('memberships.productBuilderGranted')
          : t('memberships.productBuilderRevoked'),
        body: result.isProductBuilder
          ? t('memberships.productBuilderGrantedDescription')
          : t('memberships.productBuilderRevokedDescription'),
      });
      await queryClient.invalidateQueries({ queryKey: workspaceProductBuilderKeys.all });
    },
    onError: async (error, variables) => {
      setFeedback(productBuilderProblemFeedback(error, t));
      if (!(error instanceof ApiError) || error.status !== 409) return;

      try {
        const members = await queryClient.fetchQuery(workspaceProductBuildersQueryOptions());
        const currentMember = members.find((candidate) => candidate.userId === variables.userId);
        if (currentMember) {
          setMember(currentMember);
        }
      } catch {
        setFeedback({
          tone: 'warning',
          title: t('memberships.productBuilderUnavailable'),
          body: t('memberships.productBuilderUnavailableDescription'),
        });
      }
    },
  });

  const userId = member.userId;
  const revision = member.membershipRevision;
  const actionable = member.canChange === true && userId !== undefined && revision !== undefined;
  const enabled = member.isProductBuilder === true;
  const title = member.displayName ?? t('memberships.productBuilderUnknownMember');

  return (
    <ManagedDialog
      surfaceId="membership-windows"
      open
      title={title}
      description={t('memberships.productBuilderDescription')}
      titleAccessory={
        <StatusBadge state={enabled ? 'positive' : 'inactive'}>
          {enabled
            ? t('memberships.productBuilderActive')
            : t('memberships.productBuilderInactive')}
        </StatusBadge>
      }
      onOpenChange={(open) => {
        if (!open) onClose();
      }}
      closeDisabled={mutation.isPending}
      footer={
        <>
          <ManagedDialogAction
            type="button"
            variant="outline"
            disabled={mutation.isPending}
            onClick={onClose}
          >
            {t('app.close')}
          </ManagedDialogAction>
          {actionable && !enabled ? (
            <ManagedDialogAsyncAction
              type="button"
              disabled={mutation.isPending}
              icon={<ShieldCheck aria-hidden />}
              pending={mutation.isPending}
              pendingLabel={t('memberships.productBuilderGranting')}
              onClick={() => mutation.mutate({ enabled: true, userId, revision })}
            >
              {t('memberships.productBuilderGrant')}
            </ManagedDialogAsyncAction>
          ) : null}
          {actionable && enabled ? (
            <AlertDialog>
              <AlertDialogTrigger
                render={
                  <ManagedDialogAsyncAction
                    type="button"
                    variant="destructive"
                    disabled={mutation.isPending}
                    icon={<ShieldMinus aria-hidden />}
                    pending={mutation.isPending}
                    pendingLabel={t('memberships.productBuilderRevoking')}
                  >
                    {t('memberships.productBuilderRevoke')}
                  </ManagedDialogAsyncAction>
                }
              />
              <AlertDialogContent>
                <AlertDialogHeader>
                  <AlertDialogTitle>
                    {t('memberships.productBuilderRevokeConfirmTitle')}
                  </AlertDialogTitle>
                  <AlertDialogDescription>
                    {t('memberships.productBuilderRevokeConfirmDescription')}
                  </AlertDialogDescription>
                </AlertDialogHeader>
                <AlertDialogFooter>
                  <AlertDialogCancel>{t('app.cancel')}</AlertDialogCancel>
                  <AlertDialogAction
                    variant="destructive"
                    onClick={() => mutation.mutate({ enabled: false, userId, revision })}
                  >
                    {t('memberships.productBuilderRevoke')}
                  </AlertDialogAction>
                </AlertDialogFooter>
              </AlertDialogContent>
            </AlertDialog>
          ) : null}
        </>
      }
    >
      <ManagedDialogBody className="space-y-4">
        {feedback ? (
          <div aria-live="polite">
            <StatusNotice tone={feedback.tone} title={feedback.title}>
              {feedback.body}
            </StatusNotice>
          </div>
        ) : null}
        <dl className="grid gap-4 text-sm sm:grid-cols-2">
          <Fact label={t('memberships.productBuilderMember')} value={title} />
          <Fact
            label={t('memberships.email')}
            value={member.email ?? t('memberships.notAvailable')}
          />
          <Fact
            label={t('memberships.role')}
            value={t(`memberships.role${member.workspaceRole ?? 'Member'}`)}
          />
          <Fact
            label={t('memberships.productBuilder')}
            value={
              enabled
                ? t('memberships.productBuilderActive')
                : t('memberships.productBuilderInactive')
            }
          />
        </dl>
        {!actionable ? (
          <StatusNotice tone="info" title={t('memberships.productBuilderProtected')}>
            {t('memberships.productBuilderProtectedDescription')}
          </StatusNotice>
        ) : null}
      </ManagedDialogBody>
    </ManagedDialog>
  );
}

function MembershipInviteDialog({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const baseId = useId();
  const formId = `${baseId}-form`;
  const emailId = `${baseId}-email`;
  const roleId = `${baseId}-role`;
  const [email, setEmail] = useState('');
  const [role, setRole] = useState<WorkspaceRole>('Member');
  const [emailError, setEmailError] = useState<string | null>(null);
  const [feedback, setFeedback] = useState<Feedback | null>(null);
  const [discardOpen, setDiscardOpen] = useState(false);
  const dirty = Boolean(email.trim()) || role !== 'Member';
  const mutation = useMutation({
    mutationFn: inviteWorkspaceMember,
    onSuccess: async (result) => {
      setEmail('');
      setRole('Member');
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

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setFeedback(null);
    const normalized = email.trim();
    if (!normalized.includes('@')) {
      setEmailError(t('memberships.emailInvalid'));
      return;
    }
    setEmailError(null);
    mutation.mutate({ email: normalized, requestedRole: role });
  }

  function requestClose() {
    if (dirty) setDiscardOpen(true);
    else onClose();
  }

  return (
    <>
      <ManagedDialog
        surfaceId="membership-windows"
        open
        title={t('memberships.invite')}
        description={t('memberships.description')}
        onOpenChange={(open) => {
          if (!open) requestClose();
        }}
        closeDisabled={mutation.isPending}
        dirty={dirty}
        footer={
          <>
            <ManagedDialogAction
              type="button"
              variant="outline"
              disabled={mutation.isPending}
              onClick={requestClose}
            >
              {t('app.cancel')}
            </ManagedDialogAction>
            <ManagedDialogAsyncAction
              type="submit"
              form={formId}
              disabled={mutation.isPending || !email.trim()}
              icon={<MailPlus aria-hidden />}
              pending={mutation.isPending}
              pendingLabel={t('memberships.inviting')}
            >
              {t('memberships.invite')}
            </ManagedDialogAsyncAction>
          </>
        }
      >
        <form id={formId} className="contents" onSubmit={submit} noValidate>
          <ManagedDialogBody className="space-y-4">
            {feedback ? (
              <div aria-live="polite">
                <StatusNotice tone={feedback.tone} title={feedback.title}>
                  {feedback.body}
                </StatusNotice>
              </div>
            ) : null}
            <Field data-invalid={Boolean(emailError)}>
              <FieldLabel htmlFor={emailId}>{t('memberships.email')}</FieldLabel>
              <Input
                id={emailId}
                type="email"
                autoComplete="email"
                value={email}
                aria-invalid={Boolean(emailError)}
                disabled={mutation.isPending}
                onChange={(event) => {
                  setEmail(event.target.value);
                  setEmailError(null);
                }}
              />
              {emailError ? (
                <FieldError>{emailError}</FieldError>
              ) : (
                <FieldDescription>{t('memberships.emailHelp')}</FieldDescription>
              )}
            </Field>
            <Field>
              <FieldLabel htmlFor={roleId}>{t('memberships.role')}</FieldLabel>
              <Select
                value={role}
                onValueChange={(value) => setRole(value as WorkspaceRole)}
                disabled={mutation.isPending}
              >
                <SelectTrigger id={roleId} className="w-full">
                  <SelectValue>{t(`memberships.role${role}`)}</SelectValue>
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="Member">{t('memberships.roleMember')}</SelectItem>
                  <SelectItem value="Administrator">
                    {t('memberships.roleAdministrator')}
                  </SelectItem>
                </SelectContent>
              </Select>
              <FieldDescription>{t('memberships.roleHelp')}</FieldDescription>
            </Field>
          </ManagedDialogBody>
        </form>
      </ManagedDialog>
      <AlertDialog open={discardOpen} onOpenChange={setDiscardOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('memberships.discardInviteTitle')}</AlertDialogTitle>
            <AlertDialogDescription>
              {t('memberships.discardInviteDescription')}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>{t('memberships.keepEditing')}</AlertDialogCancel>
            <AlertDialogAction
              variant="destructive"
              onClick={() => {
                setDiscardOpen(false);
                onClose();
              }}
            >
              {t('memberships.discardInvite')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  );
}

function MembershipInvitationDialog({
  initialInvitation,
  onClose,
}: {
  initialInvitation: WorkspaceInvitationLifecycleDto;
  onClose: () => void;
}) {
  const { t, i18n } = useTranslation();
  const queryClient = useQueryClient();
  const [invitation, setInvitation] = useState(initialInvitation);
  const [feedback, setFeedback] = useState<Feedback | null>(null);
  const dateFormatter = new Intl.DateTimeFormat(i18n.language, {
    dateStyle: 'medium',
    timeStyle: 'short',
  });
  useEffect(() => {
    if ((initialInvitation.revision ?? 0) >= (invitation.revision ?? 0)) {
      setInvitation(initialInvitation);
    }
  }, [initialInvitation, invitation.revision]);
  const resendMutation = useMutation({
    mutationFn: ({ invitationId, revision }: { invitationId: string; revision: number }) =>
      resendWorkspaceInvitation(invitationId, { expectedRevision: revision }),
    onSuccess: async (result) => {
      setInvitation(result);
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
    onSuccess: async (result) => {
      setInvitation(result);
      setFeedback({
        tone: 'success',
        title: t('memberships.revokeSucceeded'),
        body: t('memberships.revokeSucceededDescription'),
      });
      await queryClient.invalidateQueries({ queryKey: workspaceInvitationKeys.all });
    },
    onError: (error) => setFeedback(problemFeedback(error, t)),
  });
  const invitationId = invitation.invitationId;
  const revision = invitation.revision;
  const pending = invitation.status === 'Pending';
  const actionable = pending && invitationId !== undefined && revision !== undefined;
  const busy = resendMutation.isPending || revokeMutation.isPending;
  const title = invitation.recipientEmail ?? t('memberships.recipientRemoved');

  return (
    <ManagedDialog
      surfaceId="membership-windows"
      open
      title={title}
      description={t('memberships.description')}
      titleAccessory={
        <StatusBadge state={invitationState(invitation.status)}>
          {t(`memberships.status${invitation.status ?? 'Pending'}`)}
        </StatusBadge>
      }
      onOpenChange={(open) => {
        if (!open) onClose();
      }}
      closeDisabled={busy}
      footer={
        <>
          <ManagedDialogAction type="button" variant="outline" disabled={busy} onClick={onClose}>
            {t('app.close')}
          </ManagedDialogAction>
          {actionable ? (
            <ManagedDialogAsyncAction
              type="button"
              variant="secondary"
              disabled={busy}
              onClick={() => resendMutation.mutate({ invitationId, revision })}
              icon={<RefreshCw aria-hidden />}
              pending={resendMutation.isPending}
              pendingLabel={t('memberships.resending')}
            >
              {t('memberships.resend')}
            </ManagedDialogAsyncAction>
          ) : null}
          {actionable ? (
            <AlertDialog>
              <AlertDialogTrigger
                render={
                  <ManagedDialogAsyncAction
                    type="button"
                    variant="destructive"
                    disabled={busy}
                    icon={<UserMinus aria-hidden />}
                    pending={revokeMutation.isPending}
                    pendingLabel={t('memberships.revoking')}
                  >
                    {t('memberships.revoke')}
                  </ManagedDialogAsyncAction>
                }
              />
              <AlertDialogContent>
                <AlertDialogHeader>
                  <AlertDialogTitle>{t('memberships.revokeConfirmTitle')}</AlertDialogTitle>
                  <AlertDialogDescription>
                    {t('memberships.revokeConfirmDescription')}
                  </AlertDialogDescription>
                </AlertDialogHeader>
                <AlertDialogFooter>
                  <AlertDialogCancel>{t('app.cancel')}</AlertDialogCancel>
                  <AlertDialogAction
                    variant="destructive"
                    onClick={() => revokeMutation.mutate({ invitationId, revision })}
                  >
                    {t('memberships.revoke')}
                  </AlertDialogAction>
                </AlertDialogFooter>
              </AlertDialogContent>
            </AlertDialog>
          ) : null}
        </>
      }
    >
      <ManagedDialogBody className="space-y-4">
        {feedback ? (
          <div aria-live="polite">
            <StatusNotice tone={feedback.tone} title={feedback.title}>
              {feedback.body}
            </StatusNotice>
          </div>
        ) : null}
        <dl className="grid gap-4 text-sm sm:grid-cols-2">
          <Fact label={t('memberships.email')} value={title} />
          <Fact
            label={t('memberships.role')}
            value={t(`memberships.role${invitation.requestedRole ?? 'Member'}`)}
          />
          <Fact
            label={t('memberships.status')}
            value={t(`memberships.status${invitation.status ?? 'Pending'}`)}
          />
          <Fact
            label={t('memberships.delivery')}
            value={t(`memberships.delivery${invitation.deliveryStatus ?? 'Pending'}`)}
          />
          <Fact
            label={t('memberships.expires')}
            value={
              invitation.expiresAt
                ? dateFormatter.format(new Date(invitation.expiresAt))
                : t('memberships.notAvailable')
            }
          />
        </dl>
      </ManagedDialogBody>
    </ManagedDialog>
  );
}

function readInvitation(
  descriptor: ManagedWindowDescriptor,
): WorkspaceInvitationLifecycleDto | null {
  if (typeof descriptor.payload !== 'object' || descriptor.payload === null) return null;
  if (!('invitationId' in descriptor.payload)) return null;
  return descriptor.payload as WorkspaceInvitationLifecycleDto;
}

function readProductBuilder(
  descriptor: ManagedWindowDescriptor,
): WorkspaceProductBuilderDto | null {
  if (typeof descriptor.payload !== 'object' || descriptor.payload === null) return null;
  if (!('userId' in descriptor.payload)) return null;
  return descriptor.payload as WorkspaceProductBuilderDto;
}

function Fact({ label, value }: { label: string; value: string }) {
  return (
    <div className="min-w-0">
      <dt className="font-medium text-muted-foreground">{label}</dt>
      <dd className="break-words text-foreground">{value}</dd>
    </div>
  );
}

function invitationState(status: string | undefined) {
  if (status === 'Accepted') return 'positive' as const;
  if (status === 'Pending') return 'informative' as const;
  if (status === 'Expired') return 'caution' as const;
  if (status === 'Revoked') return 'inactive' as const;
  return 'neutral' as const;
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
  if (error instanceof ApiError && (error.status === 403 || error.status === 404)) {
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

function productBuilderProblemFeedback(error: unknown, t: (key: string) => string): Feedback {
  if (error instanceof ApiError && error.status === 409) {
    return {
      tone: 'warning',
      title: t('memberships.productBuilderChanged'),
      body: t('memberships.productBuilderChangedDescription'),
    };
  }
  if (error instanceof ApiError && (error.status === 403 || error.status === 404)) {
    return {
      tone: 'destructive',
      title: t('memberships.forbidden'),
      body: t('memberships.productBuilderForbiddenDescription'),
    };
  }
  if (error instanceof ApiError && error.status === 503) {
    return {
      tone: 'warning',
      title: t('memberships.productBuilderUnavailable'),
      body: t('memberships.productBuilderUnavailableDescription'),
    };
  }
  return {
    tone: 'destructive',
    title: t('memberships.productBuilderActionFailed'),
    body: t('memberships.actionFailedDescription'),
  };
}
