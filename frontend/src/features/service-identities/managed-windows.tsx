import { useMutation, useQueryClient } from '@tanstack/react-query';
import { KeyRound, Plus, ShieldX } from 'lucide-react';
import { type FormEvent, useId, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { AsyncButton } from '@/components/shared/AsyncButton';
import { ManagedDialog, ManagedDialogBody } from '@/components/shared/ManagedDialog';
import {
  type ManagedWindowDescriptor,
  type ManagedWindowRendererProps,
  type ManagedWindowRendererRegistry,
  useCurrentManagedWindow,
} from '@/components/shared/ManagedWindowManager';
import { StatusBadge, type StatusBadgeTone } from '@/components/shared/StatusBadge';
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
import { Button } from '@/components/ui/button';
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { ApiError } from '@/lib/api';
import type { ServiceIdentityDto, ServiceIdentityKeyDto } from '@/lib/api-generated';
import {
  addServiceIdentityKey,
  createServiceIdentity,
  revokeServiceIdentity,
  revokeServiceIdentityKey,
  serviceIdentityQueryKeys,
} from './api';

const SERVICE_IDENTITY_CREATE_KIND = 'service-identities.create';
const SERVICE_IDENTITY_KIND = 'service-identities.identity';
type Feedback = { tone: StatusNoticeTone; title: string; body: string };

export function serviceIdentityCreateWindowDescriptor(title: string): ManagedWindowDescriptor {
  return {
    id: 'service-identities:create',
    kind: SERVICE_IDENTITY_CREATE_KIND,
    resourceKey: 'create',
    title,
  };
}

export function serviceIdentityWindowDescriptor(
  identity: ServiceIdentityDto,
  title: string,
): ManagedWindowDescriptor {
  return {
    id: `service-identities:${identity.id}`,
    kind: SERVICE_IDENTITY_KIND,
    resourceKey: identity.id ?? title,
    title,
    payload: identity,
  };
}

export const serviceIdentitiesManagedWindowRenderers: ManagedWindowRendererRegistry = {
  [SERVICE_IDENTITY_CREATE_KIND]: ServiceIdentityCreateWindowRenderer,
  [SERVICE_IDENTITY_KIND]: ServiceIdentityWindowRenderer,
};

function ServiceIdentityCreateWindowRenderer() {
  const { windowId, closeWindow } = useCurrentManagedWindow();
  return <ServiceIdentityCreateDialog onClose={() => closeWindow(windowId)} />;
}

function ServiceIdentityWindowRenderer({ descriptor }: ManagedWindowRendererProps) {
  const { windowId, closeWindow } = useCurrentManagedWindow();
  const identity = readIdentity(descriptor);
  if (!identity) {
    return <UnavailableDialog title={descriptor.title} onClose={() => closeWindow(windowId)} />;
  }
  return <ServiceIdentityDialog initialIdentity={identity} onClose={() => closeWindow(windowId)} />;
}

function ServiceIdentityCreateDialog({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const baseId = useId();
  const formId = `${baseId}-form`;
  const clientId = `${baseId}-client-id`;
  const [value, setValue] = useState('');
  const [feedback, setFeedback] = useState<Feedback | null>(null);
  const [discardOpen, setDiscardOpen] = useState(false);
  const dirty = Boolean(value.trim());
  const mutation = useMutation({
    mutationFn: createServiceIdentity,
    onSuccess: async () => {
      setValue('');
      setFeedback({
        tone: 'success',
        title: t('serviceIdentities.created'),
        body: t('serviceIdentities.createdDescription'),
      });
      await queryClient.invalidateQueries({ queryKey: serviceIdentityQueryKeys.all });
    },
    onError: (error) => setFeedback(identityProblemFeedback(error, t)),
  });

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const normalized = value.trim();
    if (normalized) mutation.mutate({ clientId: normalized });
  }

  function requestClose() {
    if (dirty) setDiscardOpen(true);
    else onClose();
  }

  return (
    <>
      <ManagedDialog
        open
        title={t('serviceIdentities.create')}
        description={t('serviceIdentities.description')}
        dirty={dirty}
        closeDisabled={mutation.isPending}
        onOpenChange={(open) => {
          if (!open) requestClose();
        }}
        footer={
          <>
            <Button
              type="button"
              variant="outline"
              disabled={mutation.isPending}
              onClick={requestClose}
            >
              {t('app.cancel')}
            </Button>
            <AsyncButton
              type="submit"
              form={formId}
              disabled={mutation.isPending || !value.trim()}
              icon={<Plus aria-hidden />}
              pending={mutation.isPending}
              pendingLabel={t('serviceIdentities.creating')}
            >
              {t('serviceIdentities.create')}
            </AsyncButton>
          </>
        }
      >
        <form id={formId} className="contents" onSubmit={submit}>
          <ManagedDialogBody className="space-y-4">
            {feedback ? (
              <div aria-live="polite">
                <StatusNotice tone={feedback.tone} title={feedback.title}>
                  {feedback.body}
                </StatusNotice>
              </div>
            ) : null}
            <Field>
              <FieldLabel htmlFor={clientId}>{t('serviceIdentities.clientId')}</FieldLabel>
              <Input
                id={clientId}
                value={value}
                disabled={mutation.isPending}
                onChange={(event) => setValue(event.target.value)}
              />
              <FieldDescription>{t('serviceIdentities.clientIdHelp')}</FieldDescription>
            </Field>
          </ManagedDialogBody>
        </form>
      </ManagedDialog>
      <DiscardDialog
        open={discardOpen}
        title={t('serviceIdentities.discardCreateTitle')}
        description={t('serviceIdentities.discardCreateDescription')}
        action={t('serviceIdentities.discardCreate')}
        onOpenChange={setDiscardOpen}
        onDiscard={onClose}
      />
    </>
  );
}

function ServiceIdentityDialog({
  initialIdentity,
  onClose,
}: {
  initialIdentity: ServiceIdentityDto;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const baseId = useId();
  const formId = `${baseId}-key-form`;
  const publicJwkId = `${baseId}-public-jwk`;
  const [identity, setIdentity] = useState(initialIdentity);
  const [publicJwk, setPublicJwk] = useState('');
  const [jwkError, setJwkError] = useState<string | null>(null);
  const [feedback, setFeedback] = useState<Feedback | null>(null);
  const [discardOpen, setDiscardOpen] = useState(false);
  const active = identity.status === 'Active';
  const dirty = Boolean(publicJwk.trim());
  const identityReady = identity.id !== undefined && identity.revision !== undefined;
  const addKeyMutation = useMutation({
    mutationFn: ({ id, revision, jwk }: { id: string; revision: number; jwk: string }) =>
      addServiceIdentityKey(id, { expectedRevision: revision, publicJwk: jwk }),
    onSuccess: async (result) => {
      setIdentity(result);
      setPublicJwk('');
      setJwkError(null);
      setFeedback({
        tone: 'success',
        title: t('serviceIdentities.keyAdded'),
        body: t('serviceIdentities.keyAddedDescription'),
      });
      await queryClient.invalidateQueries({ queryKey: serviceIdentityQueryKeys.all });
    },
    onError: (error) => setFeedback(identityProblemFeedback(error, t)),
  });
  const revokeKeyMutation = useMutation({
    mutationFn: ({ id, keyId, revision }: { id: string; keyId: string; revision: number }) =>
      revokeServiceIdentityKey(id, keyId, revision),
    onSuccess: async (result) => {
      setIdentity(result);
      setFeedback({
        tone: 'success',
        title: t('serviceIdentities.keyRevoked'),
        body: t('serviceIdentities.keyRevokedDescription'),
      });
      await queryClient.invalidateQueries({ queryKey: serviceIdentityQueryKeys.all });
    },
    onError: (error) => setFeedback(identityProblemFeedback(error, t)),
  });
  const revokeIdentityMutation = useMutation({
    mutationFn: ({ id, revision }: { id: string; revision: number }) =>
      revokeServiceIdentity(id, revision),
    onSuccess: async (result) => {
      setIdentity(result);
      setFeedback({
        tone: 'success',
        title: t('serviceIdentities.revoked'),
        body: t('serviceIdentities.revokedDescription'),
      });
      await queryClient.invalidateQueries({ queryKey: serviceIdentityQueryKeys.all });
    },
    onError: (error) => setFeedback(identityProblemFeedback(error, t)),
  });
  const busy =
    addKeyMutation.isPending || revokeKeyMutation.isPending || revokeIdentityMutation.isPending;

  function submitKey(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!identity.id || identity.revision === undefined) return;
    const validation = validatePublicJwk(publicJwk);
    if (validation) {
      setJwkError(t(validation));
      return;
    }
    setJwkError(null);
    addKeyMutation.mutate({
      id: identity.id,
      revision: identity.revision,
      jwk: publicJwk.trim(),
    });
  }

  function changePublicJwk(value: string) {
    if (validatePublicJwk(value) === 'serviceIdentities.jwkPrivate') {
      setPublicJwk('');
      setJwkError(t('serviceIdentities.jwkPrivate'));
      return;
    }
    setPublicJwk(value);
    setJwkError(null);
  }

  function requestClose() {
    if (dirty) setDiscardOpen(true);
    else onClose();
  }

  return (
    <>
      <ManagedDialog
        open
        title={identity.clientId ?? t('serviceIdentities.notAvailable')}
        description={identity.id}
        titleAccessory={
          <StatusBadge tone={active ? 'success' : 'muted'}>
            {active ? t('serviceIdentities.active') : t('serviceIdentities.inactive')}
          </StatusBadge>
        }
        dirty={dirty}
        closeDisabled={busy}
        onOpenChange={(open) => {
          if (!open) requestClose();
        }}
        footer={
          <>
            <Button type="button" variant="outline" disabled={busy} onClick={requestClose}>
              {t('app.close')}
            </Button>
            {active && identityReady ? (
              <AlertDialog>
                <AlertDialogTrigger
                  render={
                    <AsyncButton
                      type="button"
                      variant="destructive"
                      disabled={busy}
                      icon={<ShieldX aria-hidden />}
                      pending={revokeIdentityMutation.isPending}
                      pendingLabel={t('serviceIdentities.revokingIdentity')}
                    >
                      {t('serviceIdentities.revokeIdentity')}
                    </AsyncButton>
                  }
                />
                <AlertDialogContent>
                  <AlertDialogHeader>
                    <AlertDialogTitle>
                      {t('serviceIdentities.revokeIdentityTitle')}
                    </AlertDialogTitle>
                    <AlertDialogDescription>
                      {t('serviceIdentities.revokeIdentityDescription')}
                    </AlertDialogDescription>
                  </AlertDialogHeader>
                  <AlertDialogFooter>
                    <AlertDialogCancel>{t('app.cancel')}</AlertDialogCancel>
                    <AlertDialogAction
                      variant="destructive"
                      onClick={() =>
                        identity.id &&
                        identity.revision !== undefined &&
                        revokeIdentityMutation.mutate({
                          id: identity.id,
                          revision: identity.revision,
                        })
                      }
                    >
                      {t('serviceIdentities.revokeIdentity')}
                    </AlertDialogAction>
                  </AlertDialogFooter>
                </AlertDialogContent>
              </AlertDialog>
            ) : null}
          </>
        }
      >
        <ManagedDialogBody className="space-y-5">
          {feedback ? (
            <div aria-live="polite">
              <StatusNotice tone={feedback.tone} title={feedback.title}>
                {feedback.body}
              </StatusNotice>
            </div>
          ) : null}
          <dl className="grid gap-3 text-sm sm:grid-cols-2">
            <Fact label={t('serviceIdentities.workspace')} value={identity.workspaceId} />
            <Fact
              label={t('serviceIdentities.grantStatus')}
              value={identity.workspaceGrantStatus}
            />
            <Fact label={t('serviceIdentities.revision')} value={identity.revision?.toString()} />
            <Fact label={t('serviceIdentities.subject')} value={identity.subject?.subjectId} />
          </dl>
          <section className="grid gap-3" aria-label={t('serviceIdentities.keysTitle')}>
            <h3 className="font-medium">{t('serviceIdentities.keysTitle')}</h3>
            {(identity.keys ?? []).length === 0 ? (
              <p className="text-sm text-muted-foreground">{t('serviceIdentities.keysEmpty')}</p>
            ) : (
              <ul className="grid gap-3">
                {(identity.keys ?? []).map((key) => (
                  <ServiceKey
                    key={key.id}
                    serviceKey={key}
                    disabled={busy || !active || !identityReady}
                    pending={
                      revokeKeyMutation.isPending && revokeKeyMutation.variables?.keyId === key.id
                    }
                    onRevoke={() =>
                      identity.id &&
                      key.id &&
                      identity.revision !== undefined &&
                      revokeKeyMutation.mutate({
                        id: identity.id,
                        keyId: key.id,
                        revision: identity.revision,
                      })
                    }
                  />
                ))}
              </ul>
            )}
          </section>
          {active ? (
            <form
              id={formId}
              className="grid gap-3 border-t border-border pt-4"
              onSubmit={submitKey}
            >
              <Field data-invalid={Boolean(jwkError)}>
                <FieldLabel htmlFor={publicJwkId}>{t('serviceIdentities.publicJwk')}</FieldLabel>
                <Textarea
                  id={publicJwkId}
                  value={publicJwk}
                  disabled={busy}
                  aria-invalid={Boolean(jwkError)}
                  onChange={(event) => changePublicJwk(event.target.value)}
                />
                {jwkError ? (
                  <FieldError>{jwkError}</FieldError>
                ) : (
                  <FieldDescription>{t('serviceIdentities.publicJwkHelp')}</FieldDescription>
                )}
              </Field>
              <AsyncButton
                type="submit"
                className="w-fit"
                disabled={busy || !publicJwk.trim()}
                icon={<KeyRound aria-hidden />}
                pending={addKeyMutation.isPending}
                pendingLabel={t('serviceIdentities.addingKey')}
              >
                {t('serviceIdentities.addKey')}
              </AsyncButton>
            </form>
          ) : null}
        </ManagedDialogBody>
      </ManagedDialog>
      <DiscardDialog
        open={discardOpen}
        title={t('serviceIdentities.discardKeyTitle')}
        description={t('serviceIdentities.discardKeyDescription')}
        action={t('serviceIdentities.discardKey')}
        onOpenChange={setDiscardOpen}
        onDiscard={onClose}
      />
    </>
  );
}

function ServiceKey({
  serviceKey,
  disabled,
  pending,
  onRevoke,
}: {
  serviceKey: ServiceIdentityKeyDto;
  disabled: boolean;
  pending: boolean;
  onRevoke: () => void;
}) {
  const { t } = useTranslation();
  const active = serviceKey.status === 'Active';
  return (
    <li className="grid min-w-0 gap-2 border-b border-border pb-3 last:border-0">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="break-all font-medium">{serviceKey.kid}</p>
          <p className="break-all text-xs text-muted-foreground">{serviceKey.thumbprint}</p>
        </div>
        <StatusBadge tone={keyTone(serviceKey.status)}>
          {active ? t('serviceIdentities.active') : t('serviceIdentities.revokedStatus')}
        </StatusBadge>
      </div>
      {active ? (
        <AlertDialog>
          <AlertDialogTrigger
            render={
              <AsyncButton
                type="button"
                size="sm"
                variant="destructive"
                className="w-fit"
                disabled={disabled}
                icon={<ShieldX aria-hidden />}
                pending={pending}
                pendingLabel={t('serviceIdentities.revokingKey')}
              >
                {t('serviceIdentities.revokeKey')}
              </AsyncButton>
            }
          />
          <AlertDialogContent>
            <AlertDialogHeader>
              <AlertDialogTitle>{t('serviceIdentities.revokeKeyTitle')}</AlertDialogTitle>
              <AlertDialogDescription>
                {t('serviceIdentities.revokeKeyDescription', { kid: serviceKey.kid ?? '' })}
              </AlertDialogDescription>
            </AlertDialogHeader>
            <AlertDialogFooter>
              <AlertDialogCancel>{t('app.cancel')}</AlertDialogCancel>
              <AlertDialogAction variant="destructive" onClick={onRevoke}>
                {t('serviceIdentities.revokeKey')}
              </AlertDialogAction>
            </AlertDialogFooter>
          </AlertDialogContent>
        </AlertDialog>
      ) : null}
    </li>
  );
}

function DiscardDialog({
  open,
  title,
  description,
  action,
  onOpenChange,
  onDiscard,
}: {
  open: boolean;
  title: string;
  description: string;
  action: string;
  onOpenChange: (open: boolean) => void;
  onDiscard: () => void;
}) {
  const { t } = useTranslation();
  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{title}</AlertDialogTitle>
          <AlertDialogDescription>{description}</AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>{t('serviceIdentities.keepEditing')}</AlertDialogCancel>
          <AlertDialogAction
            variant="destructive"
            onClick={() => {
              onOpenChange(false);
              onDiscard();
            }}
          >
            {action}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}

function UnavailableDialog({ title, onClose }: { title: string; onClose: () => void }) {
  const { t } = useTranslation();
  return (
    <ManagedDialog
      open
      title={title}
      onOpenChange={(open) => {
        if (!open) onClose();
      }}
      footer={
        <Button type="button" variant="outline" onClick={onClose}>
          {t('app.close')}
        </Button>
      }
    >
      <ManagedDialogBody>
        <p role="alert">{t('dialog.unavailable')}</p>
      </ManagedDialogBody>
    </ManagedDialog>
  );
}

function Fact({ label, value }: { label: string; value: string | undefined }) {
  const { t } = useTranslation();
  return (
    <div className="min-w-0">
      <dt className="font-medium text-muted-foreground">{label}</dt>
      <dd className="break-all">{value ?? t('serviceIdentities.notAvailable')}</dd>
    </div>
  );
}

function readIdentity(descriptor: ManagedWindowDescriptor): ServiceIdentityDto | null {
  const identity = descriptor.payload as ServiceIdentityDto | undefined;
  return identity?.id ? identity : null;
}

function keyTone(status: string | undefined): StatusBadgeTone {
  return status === 'Active' ? 'success' : 'muted';
}

function validatePublicJwk(
  value: string,
): 'serviceIdentities.jwkInvalid' | 'serviceIdentities.jwkPrivate' | null {
  let parsed: unknown;
  try {
    parsed = JSON.parse(value);
  } catch {
    return 'serviceIdentities.jwkInvalid';
  }
  if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed))
    return 'serviceIdentities.jwkInvalid';
  const jwk = parsed as Record<string, unknown>;
  if (['d', 'k', 'p', 'q', 'dp', 'dq', 'qi', 'oth'].some((name) => name in jwk))
    return 'serviceIdentities.jwkPrivate';
  if (
    jwk.kty !== 'EC' ||
    jwk.crv !== 'P-256' ||
    typeof jwk.x !== 'string' ||
    typeof jwk.y !== 'string' ||
    typeof jwk.kid !== 'string'
  )
    return 'serviceIdentities.jwkInvalid';
  return null;
}

function identityProblemFeedback(error: unknown, t: (key: string) => string): Feedback {
  if (error instanceof ApiError && error.status === 409)
    return {
      tone: 'warning',
      title: t('serviceIdentities.conflict'),
      body: t('serviceIdentities.conflictDescription'),
    };
  if (error instanceof ApiError && (error.status === 403 || error.status === 404))
    return {
      tone: 'destructive',
      title: t('serviceIdentities.forbidden'),
      body: t('serviceIdentities.forbiddenDescription'),
    };
  if (error instanceof ApiError && error.status === 503)
    return {
      tone: 'warning',
      title: t('serviceIdentities.unavailable'),
      body: t('serviceIdentities.unavailableDescription'),
    };
  return {
    tone: 'destructive',
    title: t('serviceIdentities.actionFailed'),
    body: t('serviceIdentities.actionFailedDescription'),
  };
}
