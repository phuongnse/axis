import { useQuery } from '@tanstack/react-query';
import { ArrowRight, Building2, Plus, UserRound } from 'lucide-react';
import type { FormEvent } from 'react';
import { useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { AsyncButton } from '@/components/shared/AsyncButton';
import { AsyncContent } from '@/components/shared/AsyncContent';
import {
  persistentItemHighlight,
  transientItemHighlight,
} from '@/components/shared/interactionStates';
import { OptionItemContent } from '@/components/shared/OptionList';
import { StatusNotice } from '@/components/shared/StatusNotice';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { ApiError } from '@/lib/api';
import { cn } from '@/lib/utils';
import {
  type CreatedOrganizationWorkspace,
  createOrganizationIdempotencyKey,
  createOrganizationWorkspace,
  type EligibleWorkspace,
  listEligibleWorkspaces,
  workspaceKeys,
} from './api';

export type WorkspaceChangeResult = 'entered' | 'not-entered' | 'unknown';
export type WorkspaceContextPhase = 'idle' | 'switching' | 'refreshing' | 'failed';
export type WorkspaceContextFailure = 'switch-failed' | 'outcome-unknown' | 'refresh-failed';

export interface WorkspaceContextState {
  failure: WorkspaceContextFailure | null;
  phase: WorkspaceContextPhase;
  targetWorkspaceId: string | null;
}

interface WorkspaceControlProps {
  contextState: WorkspaceContextState;
  onRetryContext: () => Promise<void>;
  onWorkspaceChange: (
    target: EligibleWorkspace | CreatedOrganizationWorkspace,
  ) => Promise<WorkspaceChangeResult>;
}

interface RetryIdentity {
  idempotencyKey: string;
  normalizedName: string;
}

export function WorkspaceControl({
  contextState,
  onRetryContext,
  onWorkspaceChange,
}: WorkspaceControlProps) {
  const { t } = useTranslation();
  const [createOpen, setCreateOpen] = useState(false);
  const eligibleQuery = useQuery({
    queryKey: workspaceKeys.eligible,
    queryFn: listEligibleWorkspaces,
    staleTime: 0,
  });
  const workspaces = eligibleQuery.data ?? [];
  const switching = contextState.phase === 'switching' || contextState.phase === 'refreshing';
  const switchError = contextState.failure !== null;
  const switchOutcomeUnknown = contextState.failure === 'outcome-unknown';

  async function switchWorkspace(target: EligibleWorkspace | CreatedOrganizationWorkspace) {
    if (switching) return;
    const result = await onWorkspaceChange(target);
    if (result === 'entered') setCreateOpen(false);
  }

  return (
    <>
      <section
        className="grid gap-3"
        aria-label={t('workspace.choose')}
        aria-busy={switching || undefined}
      >
        <div className="grid gap-0.5 px-1">
          <div className="flex items-center gap-2 text-xs font-medium text-muted-foreground">
            <Building2 className="size-3.5" aria-hidden />
            {t('workspace.choose')}
          </div>
          <p className="text-xs text-muted-foreground">{t('workspace.chooseDescription')}</p>
        </div>

        <AsyncContent
          className="min-h-10"
          pending={eligibleQuery.isPending}
          error={eligibleQuery.isError}
          pendingLabel={t('workspace.loading')}
        >
          {eligibleQuery.isError ? (
            <StatusNotice tone="destructive" title={t('workspace.unavailable')}>
              <Button
                type="button"
                size="sm"
                variant="outline"
                onClick={() => eligibleQuery.refetch()}
              >
                {t('app.retry')}
              </Button>
            </StatusNotice>
          ) : eligibleQuery.data ? (
            <WorkspaceGroups
              workspaces={workspaces}
              switchingWorkspaceId={contextState.targetWorkspaceId}
              switching={switching}
              onSelect={switchWorkspace}
            />
          ) : null}
        </AsyncContent>

        <Button
          type="button"
          size="sm"
          variant="outline"
          className="w-full"
          disabled={switching}
          onClick={() => {
            setCreateOpen(true);
          }}
        >
          <Plus className="size-3.5" aria-hidden />
          {t('workspace.createOrganization')}
        </Button>

        <div aria-live="polite" className="grid gap-2">
          {switching ? <span className="sr-only">{t('workspace.switching')}</span> : null}
          {switchOutcomeUnknown ? (
            <StatusNotice tone="warning">
              <span>{t('workspace.switchOutcomeUnknown')}</span>{' '}
              <Button type="button" variant="link" onClick={() => void onRetryContext()}>
                {t('workspace.retryRefresh')}
              </Button>
            </StatusNotice>
          ) : contextState.failure === 'refresh-failed' ? (
            <StatusNotice tone="destructive">
              <span>{t('workspace.refreshFailedDescription')}</span>{' '}
              <Button type="button" variant="link" onClick={() => void onRetryContext()}>
                {t('workspace.retryRefresh')}
              </Button>
            </StatusNotice>
          ) : switchError ? (
            <StatusNotice tone="destructive">{t('workspace.switchFailed')}</StatusNotice>
          ) : null}
        </div>
      </section>

      <CreateOrganizationDialog
        open={createOpen}
        switchError={switchError}
        switchOutcomeUnknown={switchOutcomeUnknown}
        switching={switching}
        onOpenChange={setCreateOpen}
        onEnter={switchWorkspace}
      />
    </>
  );
}

function WorkspaceGroups({
  workspaces,
  switchingWorkspaceId,
  switching,
  onSelect,
}: {
  workspaces: EligibleWorkspace[];
  switchingWorkspaceId: string | null;
  switching: boolean;
  onSelect: (workspace: EligibleWorkspace) => void;
}) {
  const { t } = useTranslation();
  const personal = workspaces.filter((workspace) => workspace.type === 'Personal');
  const organizations = workspaces.filter((workspace) => workspace.type === 'Organization');

  return (
    <section className="grid max-h-72 gap-3 overflow-y-auto" aria-label={t('workspace.eligible')}>
      <WorkspaceGroup
        label={t('workspace.personal')}
        workspaces={personal}
        switchingWorkspaceId={switchingWorkspaceId}
        switching={switching}
        onSelect={onSelect}
      />
      {organizations.length > 0 ? (
        <WorkspaceGroup
          label={t('workspace.organizations')}
          workspaces={organizations}
          switchingWorkspaceId={switchingWorkspaceId}
          switching={switching}
          onSelect={onSelect}
        />
      ) : null}
    </section>
  );
}

function WorkspaceGroup({
  label,
  workspaces,
  switchingWorkspaceId,
  switching,
  onSelect,
}: {
  label: string;
  workspaces: EligibleWorkspace[];
  switchingWorkspaceId: string | null;
  switching: boolean;
  onSelect: (workspace: EligibleWorkspace) => void;
}) {
  if (workspaces.length === 0) return null;

  return (
    <section className="grid gap-1" aria-label={label}>
      <h3 className="px-2 text-xs font-medium text-muted-foreground">{label}</h3>
      {workspaces.map((workspace) => {
        const WorkspaceIcon = workspace.type === 'Personal' ? UserRound : Building2;
        const switchingThisWorkspace = switching && switchingWorkspaceId === workspace.workspaceId;

        return (
          <Button
            key={workspace.workspaceId}
            type="button"
            variant="ghost"
            size="sm"
            className={cn(
              'w-full justify-start',
              workspace.isCurrent ? persistentItemHighlight : transientItemHighlight,
              workspace.isCurrent && 'disabled:opacity-100',
            )}
            disabled={workspace.isCurrent || switching}
            aria-current={workspace.isCurrent ? 'page' : undefined}
            onClick={() => onSelect(workspace)}
          >
            <OptionItemContent
              icon={<WorkspaceIcon className="size-3.5" />}
              pending={switchingThisWorkspace}
            >
              {workspace.name}
            </OptionItemContent>
          </Button>
        );
      })}
    </section>
  );
}

function CreateOrganizationDialog({
  open,
  switchError,
  switchOutcomeUnknown,
  switching,
  onOpenChange,
  onEnter,
}: {
  open: boolean;
  switchError: boolean;
  switchOutcomeUnknown: boolean;
  switching: boolean;
  onOpenChange: (open: boolean) => void;
  onEnter: (workspace: CreatedOrganizationWorkspace) => void;
}) {
  const { t } = useTranslation();
  const [name, setName] = useState('');
  const [pending, setPending] = useState(false);
  const [fieldError, setFieldError] = useState<string | null>(null);
  const [submitError, setSubmitError] = useState(false);
  const [created, setCreated] = useState<CreatedOrganizationWorkspace | null>(null);
  const retryIdentity = useRef<RetryIdentity | null>(null);

  function reset() {
    setName('');
    setPending(false);
    setFieldError(null);
    setSubmitError(false);
    setCreated(null);
    retryIdentity.current = null;
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const normalizedName = name.trim().normalize();
    if (normalizedName.length < 2 || normalizedName.length > 100) {
      setFieldError(t('workspace.organizationNameLength'));
      return;
    }

    const retry = retryIdentity.current;
    const idempotencyKey =
      retry?.normalizedName === normalizedName
        ? retry.idempotencyKey
        : createOrganizationIdempotencyKey();
    retryIdentity.current = { idempotencyKey, normalizedName };
    setPending(true);
    setFieldError(null);
    setSubmitError(false);
    try {
      setCreated(await createOrganizationWorkspace({ name: normalizedName }, idempotencyKey));
      retryIdentity.current = null;
    } catch (error) {
      const nameError = organizationNameFieldError(error);
      if (nameError === 'required') setFieldError(t('workspace.organizationNameRequired'));
      else if (nameError === 'length') setFieldError(t('workspace.organizationNameLength'));
      else setSubmitError(true);
    } finally {
      setPending(false);
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(nextOpen) => {
        if (pending || switching) return;
        if (!nextOpen) reset();
        onOpenChange(nextOpen);
      }}
    >
      <DialogContent showCloseButton={!pending && !switching}>
        <DialogHeader>
          <DialogTitle>
            {created ? t('workspace.organizationCreated') : t('workspace.createOrganization')}
          </DialogTitle>
          <DialogDescription>
            {created
              ? t('workspace.organizationCreatedDescription')
              : t('workspace.createOrganizationDescription')}
          </DialogDescription>
        </DialogHeader>

        {created ? (
          <div className="grid gap-3" aria-live="polite">
            <StatusNotice tone="success" title={created.organizationName}>
              {t('workspace.initialWorkspace', { name: created.workspaceName })}
            </StatusNotice>
            <p className="text-sm text-muted-foreground">{t('workspace.enterIsSeparate')}</p>
            {switchOutcomeUnknown ? (
              <StatusNotice tone="warning">{t('workspace.switchOutcomeUnknown')}</StatusNotice>
            ) : switchError ? (
              <StatusNotice tone="destructive">{t('workspace.switchFailed')}</StatusNotice>
            ) : null}
          </div>
        ) : (
          <form id="create-organization-form" className="grid gap-4" onSubmit={handleSubmit}>
            <Field data-invalid={Boolean(fieldError)}>
              <FieldLabel htmlFor="organization-name">{t('workspace.organizationName')}</FieldLabel>
              <Input
                id="organization-name"
                value={name}
                maxLength={100}
                disabled={pending}
                aria-invalid={Boolean(fieldError)}
                aria-describedby="organization-name-help organization-name-error"
                onChange={(event) => {
                  setName(event.target.value);
                  setFieldError(null);
                  setSubmitError(false);
                  retryIdentity.current = null;
                }}
              />
              <FieldDescription id="organization-name-help">
                {t('workspace.organizationNameHelp')}
              </FieldDescription>
              {fieldError ? (
                <FieldError id="organization-name-error">{fieldError}</FieldError>
              ) : null}
            </Field>
            <div aria-live="polite">
              {submitError ? (
                <StatusNotice tone="destructive">{t('workspace.createFailed')}</StatusNotice>
              ) : null}
            </div>
          </form>
        )}

        <DialogFooter>
          <Button
            type="button"
            variant="outline"
            disabled={pending || switching}
            onClick={() => {
              reset();
              onOpenChange(false);
            }}
          >
            {created ? t('app.close') : t('app.cancel')}
          </Button>
          {created ? (
            <AsyncButton
              type="button"
              icon={<ArrowRight />}
              pending={switching}
              pendingLabel={t('workspace.switching')}
              onClick={() => onEnter(created)}
            >
              {t('workspace.enterWorkspace')}
            </AsyncButton>
          ) : (
            <AsyncButton
              type="submit"
              form="create-organization-form"
              disabled={!name.trim()}
              icon={<Plus />}
              pending={pending}
              pendingLabel={t('workspace.creating')}
            >
              {submitError ? t('app.retry') : t('workspace.create')}
            </AsyncButton>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function organizationNameFieldError(error: unknown): 'required' | 'length' | null {
  if (!(error instanceof ApiError) || typeof error.data !== 'object' || error.data === null) {
    return null;
  }

  const errorCodes = (error.data as { errorCodes?: Record<string, string[]> }).errorCodes;
  const code = errorCodes?.name?.[0];
  if (code === 'identity.createOrganization.nameRequired') return 'required';
  if (code === 'identity.createOrganization.nameLength') return 'length';
  return null;
}
