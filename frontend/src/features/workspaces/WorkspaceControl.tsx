import { useQuery } from '@tanstack/react-query';
import { Building2, Check, LoaderCircle, Plus, UserRound } from 'lucide-react';
import type { FormEvent, ReactNode } from 'react';
import { useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  persistentItemHighlight,
  transientItemHighlight,
} from '@/components/shared/interactionStates';
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
import { ApiError, invalidateClientRequestSession } from '@/lib/api';
import { cn } from '@/lib/utils';
import {
  beginWorkspaceTransition,
  type CreatedOrganizationWorkspace,
  confirmWorkspaceTransition,
  createOrganizationIdempotencyKey,
  createOrganizationWorkspace,
  type EligibleWorkspace,
  listEligibleWorkspaces,
  recoverWorkspaceTransition,
  type WorkspaceContextTransition,
  workspaceKeys,
} from './api';

interface WorkspaceControlProps {
  onWorkspaceChanged: () => Promise<void>;
}

interface RetryIdentity {
  idempotencyKey: string;
  normalizedName: string;
}

export function WorkspaceControl({ onWorkspaceChanged }: WorkspaceControlProps) {
  const { t } = useTranslation();
  const [createOpen, setCreateOpen] = useState(false);
  const [switchingWorkspaceId, setSwitchingWorkspaceId] = useState<string | null>(null);
  const [switchError, setSwitchError] = useState(false);
  const [switchOutcomeUnknown, setSwitchOutcomeUnknown] = useState(false);
  const eligibleQuery = useQuery({
    queryKey: workspaceKeys.eligible,
    queryFn: listEligibleWorkspaces,
    staleTime: 0,
  });
  const workspaces = eligibleQuery.data ?? [];

  async function switchWorkspace(target: EligibleWorkspace | CreatedOrganizationWorkspace) {
    if (switchingWorkspaceId) return;

    setSwitchingWorkspaceId(target.workspaceId);
    setSwitchError(false);
    setSwitchOutcomeUnknown(false);
    try {
      let transition: WorkspaceContextTransition;
      let confirmationAttempted = false;
      try {
        invalidateClientRequestSession();
        await beginWorkspaceTransition(target.workspaceId);
        confirmationAttempted = true;
        transition = await confirmWorkspaceTransition();
      } catch {
        try {
          transition = await recoverWorkspaceTransition();
        } catch (error) {
          if (confirmationAttempted) {
            setSwitchOutcomeUnknown(true);
            await onWorkspaceChanged().catch(() => undefined);
            await eligibleQuery.refetch().catch(() => undefined);
            return;
          }
          throw error;
        }
      }

      const enteredTarget =
        transition.status === 'Completed' &&
        transition.authoritativeWorkspaceId === target.workspaceId;
      if (!transition.authoritativeWorkspaceId) {
        setSwitchError(true);
        await eligibleQuery.refetch();
        return;
      }

      await onWorkspaceChanged();
      if (enteredTarget) {
        setCreateOpen(false);
      } else {
        setSwitchError(true);
        await eligibleQuery.refetch();
      }
    } catch {
      setSwitchError(true);
      await eligibleQuery.refetch().catch(() => undefined);
    } finally {
      setSwitchingWorkspaceId(null);
    }
  }

  return (
    <>
      <section
        className="grid gap-3"
        aria-label={t('workspace.choose')}
        aria-busy={Boolean(switchingWorkspaceId)}
      >
        <div className="grid gap-0.5 px-1">
          <div className="flex items-center gap-2 text-xs font-medium text-muted-foreground">
            <Building2 className="size-3.5" aria-hidden />
            {t('workspace.choose')}
          </div>
          <p className="text-xs text-muted-foreground">{t('workspace.chooseDescription')}</p>
        </div>

        {eligibleQuery.isPending ? (
          <p
            className="flex items-center gap-2 px-2 py-3 text-sm text-muted-foreground"
            role="status"
          >
            <LoaderCircle className="size-4 animate-spin" aria-hidden />
            {t('workspace.loading')}
          </p>
        ) : eligibleQuery.isError ? (
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
        ) : (
          <WorkspaceGroups
            workspaces={workspaces}
            switchingWorkspaceId={switchingWorkspaceId}
            onSelect={switchWorkspace}
          />
        )}

        <Button
          type="button"
          size="sm"
          variant="outline"
          className="w-full"
          disabled={Boolean(switchingWorkspaceId)}
          onClick={() => {
            setSwitchError(false);
            setSwitchOutcomeUnknown(false);
            setCreateOpen(true);
          }}
        >
          <Plus className="size-3.5" aria-hidden />
          {t('workspace.createOrganization')}
        </Button>

        <div aria-live="polite">
          {switchingWorkspaceId ? (
            <StatusNotice tone="info">{t('workspace.switching')}</StatusNotice>
          ) : null}
          {switchOutcomeUnknown ? (
            <StatusNotice tone="warning">{t('workspace.switchOutcomeUnknown')}</StatusNotice>
          ) : switchError ? (
            <StatusNotice tone="destructive">{t('workspace.switchFailed')}</StatusNotice>
          ) : null}
        </div>
      </section>

      <CreateOrganizationDialog
        open={createOpen}
        switchError={switchError}
        switchOutcomeUnknown={switchOutcomeUnknown}
        switching={Boolean(switchingWorkspaceId)}
        onOpenChange={setCreateOpen}
        onEnter={switchWorkspace}
      />
    </>
  );
}

function WorkspaceGroups({
  workspaces,
  switchingWorkspaceId,
  onSelect,
}: {
  workspaces: EligibleWorkspace[];
  switchingWorkspaceId: string | null;
  onSelect: (workspace: EligibleWorkspace) => void;
}) {
  const { t } = useTranslation();
  const personal = workspaces.filter((workspace) => workspace.type === 'Personal');
  const organizations = workspaces.filter((workspace) => workspace.type === 'Organization');

  return (
    <section className="grid max-h-72 gap-3 overflow-y-auto" aria-label={t('workspace.eligible')}>
      <WorkspaceGroup
        icon={<UserRound className="size-3.5" aria-hidden />}
        label={t('workspace.personal')}
        workspaces={personal}
        switchingWorkspaceId={switchingWorkspaceId}
        onSelect={onSelect}
      />
      {organizations.length > 0 ? (
        <WorkspaceGroup
          icon={<Building2 className="size-3.5" aria-hidden />}
          label={t('workspace.organizations')}
          workspaces={organizations}
          switchingWorkspaceId={switchingWorkspaceId}
          onSelect={onSelect}
        />
      ) : null}
    </section>
  );
}

function WorkspaceGroup({
  icon,
  label,
  workspaces,
  switchingWorkspaceId,
  onSelect,
}: {
  icon: ReactNode;
  label: string;
  workspaces: EligibleWorkspace[];
  switchingWorkspaceId: string | null;
  onSelect: (workspace: EligibleWorkspace) => void;
}) {
  if (workspaces.length === 0) return null;

  return (
    <section className="grid gap-1" aria-label={label}>
      <h3 className="flex items-center gap-2 px-2 text-xs font-medium text-muted-foreground">
        {icon}
        {label}
      </h3>
      {workspaces.map((workspace) => (
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
          disabled={workspace.isCurrent || Boolean(switchingWorkspaceId)}
          aria-current={workspace.isCurrent ? 'page' : undefined}
          onClick={() => onSelect(workspace)}
        >
          <span className="min-w-0 flex-1 truncate text-left">{workspace.name}</span>
          {workspace.isCurrent ? <Check className="size-3.5" aria-hidden /> : null}
          {switchingWorkspaceId === workspace.workspaceId ? (
            <LoaderCircle className="size-3.5 animate-spin" aria-hidden />
          ) : null}
        </Button>
      ))}
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
              {pending ? <StatusNotice tone="info">{t('workspace.creating')}</StatusNotice> : null}
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
            <Button type="button" disabled={switching} onClick={() => onEnter(created)}>
              {switching ? t('workspace.switching') : t('workspace.enterWorkspace')}
            </Button>
          ) : (
            <Button
              type="submit"
              form="create-organization-form"
              disabled={pending || !name.trim()}
            >
              {pending
                ? t('workspace.creating')
                : submitError
                  ? t('app.retry')
                  : t('workspace.create')}
            </Button>
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
