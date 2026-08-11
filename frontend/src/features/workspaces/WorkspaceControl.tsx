import { useQuery } from '@tanstack/react-query';
import { ArrowRight, Plus } from 'lucide-react';
import type { FormEvent, ReactNode } from 'react';
import { useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { AccountWorkspaceModel } from '@/components/shared/AccountSurface';
import { AsyncButton } from '@/components/shared/AsyncButton';
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

export interface WorkspaceControlProps {
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

export interface WorkspaceControlModel {
  overlay: ReactNode;
  workspace: AccountWorkspaceModel;
}

export function useWorkspaceControl({
  contextState,
  onRetryContext,
  onWorkspaceChange,
}: WorkspaceControlProps): WorkspaceControlModel {
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

  const orderedWorkspaces = [
    ...workspaces.filter((workspace) => workspace.type === 'Personal'),
    ...workspaces.filter((workspace) => workspace.type === 'Organization'),
  ];

  return {
    workspace: {
      busy: switching,
      feedback: contextState.failure,
      loadState: eligibleQuery.isError
        ? 'error'
        : eligibleQuery.data !== undefined
          ? 'ready'
          : 'loading',
      onCreate: () => setCreateOpen(true),
      onRetryContext: () => void onRetryContext(),
      onRetryLoad: () => void eligibleQuery.refetch(),
      onSelect: (workspaceId) => {
        const target = workspaces.find((workspace) => workspace.workspaceId === workspaceId);
        if (target) void switchWorkspace(target);
      },
      options: orderedWorkspaces.map((workspace) => ({
        current: workspace.isCurrent,
        id: workspace.workspaceId,
        kind: workspace.type === 'Personal' ? 'person' : 'organization',
        label: workspace.name,
        pending: switching && contextState.targetWorkspaceId === workspace.workspaceId,
      })),
    },
    overlay: (
      <CreateOrganizationDialog
        open={createOpen}
        switchError={switchError}
        switchOutcomeUnknown={switchOutcomeUnknown}
        switching={switching}
        onOpenChange={setCreateOpen}
        onEnter={switchWorkspace}
      />
    ),
  };
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
              key="enter-workspace"
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
              key="create-organization"
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
