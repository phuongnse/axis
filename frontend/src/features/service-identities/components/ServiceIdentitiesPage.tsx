import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { KeyRound, Plus, ShieldX } from 'lucide-react';
import { type FormEvent, useState } from 'react';
import { useTranslation } from 'react-i18next';
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
  serviceIdentitiesQueryOptions,
  serviceIdentityQueryKeys,
} from '../api';

type Feedback = { tone: StatusNoticeTone; title: string; body: string };

export function ServiceIdentitiesPage() {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const identitiesQuery = useQuery(serviceIdentitiesQueryOptions());
  const [clientId, setClientId] = useState('');
  const [selectedId, setSelectedId] = useState('');
  const [publicJwk, setPublicJwk] = useState('');
  const [jwkError, setJwkError] = useState<string | null>(null);
  const [feedback, setFeedback] = useState<Feedback | null>(null);
  const identities = identitiesQuery.data ?? [];
  const selected = identities.find((identity) => identity.id === selectedId) ?? identities[0];

  async function acceptCanonical(result: ServiceIdentityDto, message: Feedback) {
    setFeedback(message);
    if (result.id) setSelectedId(result.id);
    await queryClient.invalidateQueries({ queryKey: serviceIdentityQueryKeys.all });
  }

  const createMutation = useMutation({
    mutationFn: createServiceIdentity,
    onSuccess: async (result) => {
      setClientId('');
      await acceptCanonical(result, {
        tone: 'success',
        title: t('serviceIdentities.created'),
        body: t('serviceIdentities.createdDescription'),
      });
    },
    onError: (error) => setFeedback(identityProblemFeedback(error, t)),
  });
  const addKeyMutation = useMutation({
    mutationFn: ({ id, revision, jwk }: { id: string; revision: number; jwk: string }) =>
      addServiceIdentityKey(id, { expectedRevision: revision, publicJwk: jwk }),
    onSuccess: async (result) => {
      setPublicJwk('');
      setJwkError(null);
      await acceptCanonical(result, {
        tone: 'success',
        title: t('serviceIdentities.keyAdded'),
        body: t('serviceIdentities.keyAddedDescription'),
      });
    },
    onError: (error) => setFeedback(identityProblemFeedback(error, t)),
  });
  const revokeKeyMutation = useMutation({
    mutationFn: ({ id, keyId, revision }: { id: string; keyId: string; revision: number }) =>
      revokeServiceIdentityKey(id, keyId, revision),
    onSuccess: (result) =>
      acceptCanonical(result, {
        tone: 'success',
        title: t('serviceIdentities.keyRevoked'),
        body: t('serviceIdentities.keyRevokedDescription'),
      }),
    onError: (error) => setFeedback(identityProblemFeedback(error, t)),
  });
  const revokeIdentityMutation = useMutation({
    mutationFn: ({ id, revision }: { id: string; revision: number }) =>
      revokeServiceIdentity(id, revision),
    onSuccess: (result) =>
      acceptCanonical(result, {
        tone: 'success',
        title: t('serviceIdentities.revoked'),
        body: t('serviceIdentities.revokedDescription'),
      }),
    onError: (error) => setFeedback(identityProblemFeedback(error, t)),
  });
  const pending =
    createMutation.isPending ||
    addKeyMutation.isPending ||
    revokeKeyMutation.isPending ||
    revokeIdentityMutation.isPending;

  function submitCreate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const normalized = clientId.trim();
    if (normalized) createMutation.mutate({ clientId: normalized });
  }

  function submitKey(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!selected?.id || selected.revision === undefined) return;
    const validation = validatePublicJwk(publicJwk);
    if (validation) {
      setJwkError(t(validation));
      return;
    }
    setJwkError(null);
    addKeyMutation.mutate({ id: selected.id, revision: selected.revision, jwk: publicJwk.trim() });
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

  return (
    <section className="flex h-full min-h-0 w-full min-w-0 flex-col gap-5 overflow-auto p-4 sm:p-6">
      <header>
        <h1 className="text-2xl font-semibold tracking-tight">{t('serviceIdentities.title')}</h1>
        <p className="mt-1 max-w-3xl text-sm text-muted-foreground">
          {t('serviceIdentities.description')}
        </p>
      </header>
      {feedback ? (
        <div aria-live="polite">
          <StatusNotice tone={feedback.tone} title={feedback.title}>
            {feedback.body}
          </StatusNotice>
        </div>
      ) : null}
      <form
        className="grid gap-4 border-b border-border pb-5 sm:grid-cols-2 sm:items-end"
        onSubmit={submitCreate}
      >
        <Field>
          <FieldLabel htmlFor="service-client-id">{t('serviceIdentities.clientId')}</FieldLabel>
          <Input
            id="service-client-id"
            value={clientId}
            disabled={pending}
            onChange={(event) => setClientId(event.target.value)}
          />
          <FieldDescription>{t('serviceIdentities.clientIdHelp')}</FieldDescription>
        </Field>
        <Button type="submit" disabled={pending || !clientId.trim()}>
          <Plus aria-hidden />
          {createMutation.isPending
            ? t('serviceIdentities.creating')
            : t('serviceIdentities.create')}
        </Button>
      </form>
      {identitiesQuery.isLoading ? (
        <p className="text-sm text-muted-foreground" aria-live="polite">
          {t('serviceIdentities.loading')}
        </p>
      ) : identitiesQuery.isError ? (
        <StatusNotice tone="destructive" title={t('serviceIdentities.loadFailed')}>
          <Button
            type="button"
            size="sm"
            variant="outline"
            onClick={() => void identitiesQuery.refetch()}
          >
            {t('app.retry')}
          </Button>
        </StatusNotice>
      ) : identities.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t('serviceIdentities.empty')}</p>
      ) : (
        <div className="grid min-h-0 gap-5 lg:grid-cols-2">
          <section aria-labelledby="identity-list-title">
            <h2 id="identity-list-title" className="mb-3 text-lg font-medium">
              {t('serviceIdentities.listTitle')}
            </h2>
            <ul className="grid gap-2">
              {identities.map((identity) => (
                <li key={identity.id}>
                  <Button
                    type="button"
                    variant={identity.id === selected?.id ? 'secondary' : 'ghost'}
                    className="h-auto w-full justify-start py-2 text-left"
                    onClick={() => setSelectedId(identity.id ?? '')}
                  >
                    <span className="min-w-0">
                      <span className="block truncate">
                        {identity.clientId ?? t('serviceIdentities.notAvailable')}
                      </span>
                      <span className="block truncate text-xs text-muted-foreground">
                        {identity.id}
                      </span>
                    </span>
                  </Button>
                </li>
              ))}
            </ul>
          </section>
          {selected ? (
            <IdentityDetail
              identity={selected}
              publicJwk={publicJwk}
              jwkError={jwkError}
              pending={pending}
              onPublicJwkChange={changePublicJwk}
              onAddKey={submitKey}
              onRevokeKey={(keyId) => {
                if (selected.id && selected.revision !== undefined)
                  revokeKeyMutation.mutate({ id: selected.id, keyId, revision: selected.revision });
              }}
              onRevokeIdentity={() => {
                if (selected.id && selected.revision !== undefined)
                  revokeIdentityMutation.mutate({ id: selected.id, revision: selected.revision });
              }}
            />
          ) : null}
        </div>
      )}
    </section>
  );
}

function IdentityDetail({
  identity,
  publicJwk,
  jwkError,
  pending,
  onPublicJwkChange,
  onAddKey,
  onRevokeKey,
  onRevokeIdentity,
}: {
  identity: ServiceIdentityDto;
  publicJwk: string;
  jwkError: string | null;
  pending: boolean;
  onPublicJwkChange: (value: string) => void;
  onAddKey: (event: FormEvent<HTMLFormElement>) => void;
  onRevokeKey: (keyId: string) => void;
  onRevokeIdentity: () => void;
}) {
  const { t } = useTranslation();
  const active = identity.status === 'Active';
  return (
    <section className="grid min-w-0 gap-5" aria-labelledby="identity-detail-title">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h2 id="identity-detail-title" className="break-all text-lg font-medium">
            {identity.clientId}
          </h2>
          <p className="break-all text-sm text-muted-foreground">{identity.id}</p>
        </div>
        <StatusBadge tone={active ? 'success' : 'muted'}>
          {active ? t('serviceIdentities.active') : t('serviceIdentities.inactive')}
        </StatusBadge>
      </div>
      <dl className="grid gap-3 text-sm sm:grid-cols-2">
        <Fact label={t('serviceIdentities.workspace')} value={identity.workspaceId} />
        <Fact label={t('serviceIdentities.grantStatus')} value={identity.workspaceGrantStatus} />
        <Fact label={t('serviceIdentities.revision')} value={identity.revision?.toString()} />
        <Fact label={t('serviceIdentities.subject')} value={identity.subject?.subjectId} />
      </dl>
      <section className="grid gap-3" aria-labelledby="keys-title">
        <h3 id="keys-title" className="font-medium">
          {t('serviceIdentities.keysTitle')}
        </h3>
        {(identity.keys ?? []).length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('serviceIdentities.keysEmpty')}</p>
        ) : (
          <ul className="grid gap-3">
            {(identity.keys ?? []).map((key) => (
              <ServiceKey
                key={key.id}
                serviceKey={key}
                disabled={pending || !active}
                onRevoke={() => key.id && onRevokeKey(key.id)}
              />
            ))}
          </ul>
        )}
      </section>
      {active ? (
        <form className="grid gap-3 border-t border-border pt-4" onSubmit={onAddKey}>
          <Field data-invalid={Boolean(jwkError)}>
            <FieldLabel htmlFor="public-jwk">{t('serviceIdentities.publicJwk')}</FieldLabel>
            <Textarea
              id="public-jwk"
              value={publicJwk}
              disabled={pending}
              aria-invalid={Boolean(jwkError)}
              onChange={(event) => onPublicJwkChange(event.target.value)}
            />
            {jwkError ? (
              <FieldError>{jwkError}</FieldError>
            ) : (
              <FieldDescription>{t('serviceIdentities.publicJwkHelp')}</FieldDescription>
            )}
          </Field>
          <Button type="submit" className="w-fit" disabled={pending || !publicJwk.trim()}>
            <KeyRound aria-hidden />
            {t('serviceIdentities.addKey')}
          </Button>
        </form>
      ) : null}
      {active ? (
        <AlertDialog>
          <AlertDialogTrigger
            render={
              <Button type="button" variant="destructive" className="w-fit" disabled={pending}>
                <ShieldX aria-hidden />
                {t('serviceIdentities.revokeIdentity')}
              </Button>
            }
          />
          <AlertDialogContent>
            <AlertDialogHeader>
              <AlertDialogTitle>{t('serviceIdentities.revokeIdentityTitle')}</AlertDialogTitle>
              <AlertDialogDescription>
                {t('serviceIdentities.revokeIdentityDescription')}
              </AlertDialogDescription>
            </AlertDialogHeader>
            <AlertDialogFooter>
              <AlertDialogCancel>{t('app.cancel')}</AlertDialogCancel>
              <AlertDialogAction variant="destructive" onClick={onRevokeIdentity}>
                {t('serviceIdentities.revokeIdentity')}
              </AlertDialogAction>
            </AlertDialogFooter>
          </AlertDialogContent>
        </AlertDialog>
      ) : null}
    </section>
  );
}

function ServiceKey({
  serviceKey,
  disabled,
  onRevoke,
}: {
  serviceKey: ServiceIdentityKeyDto;
  disabled: boolean;
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
              <Button
                type="button"
                size="sm"
                variant="destructive"
                className="w-fit"
                disabled={disabled}
              >
                {t('serviceIdentities.revokeKey')}
              </Button>
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

function Fact({ label, value }: { label: string; value: string | undefined }) {
  return (
    <div className="min-w-0">
      <dt className="font-medium text-muted-foreground">{label}</dt>
      <dd className="break-all">{value || '—'}</dd>
    </div>
  );
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
  if (error instanceof ApiError && error.status === 403)
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
