import {
  Building2,
  ChevronDown,
  LogOut,
  PanelsTopLeft,
  Plus,
  RotateCcw,
  Settings2,
  UserRound,
} from 'lucide-react';
import { type ReactNode, useId, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { AccountAvatar } from '@/components/shared/AccountAvatar';
import { AsyncButton } from '@/components/shared/AsyncButton';
import { AsyncContent } from '@/components/shared/AsyncContent';
import {
  persistentItemHighlight,
  transientItemHighlight,
} from '@/components/shared/interactionStates';
import { OptionItemContent, OptionList, OptionListItem } from '@/components/shared/OptionList';
import { StatusNotice } from '@/components/shared/StatusNotice';
import { Avatar, AvatarFallback } from '@/components/ui/avatar';
import { Button } from '@/components/ui/button';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { Separator } from '@/components/ui/separator';
import { type SurfaceIdFor, surfaceContractAttributes } from '@/lib/ui-foundation';
import { cn } from '@/lib/utils';
import { axisStyles } from '@/theme.generated';

export interface AccountSurfaceIdentity {
  displayName: string;
  initials: string;
  secondaryLabel?: string;
  triggerKind: 'organization' | 'person';
  triggerLabel: string;
}

export interface AccountWorkspaceOption {
  current: boolean;
  id: string;
  kind: 'organization' | 'person';
  label: string;
  pending?: boolean;
}

export interface AccountWorkspaceModel {
  busy?: boolean;
  feedback?: 'outcome-unknown' | 'refresh-failed' | 'switch-failed' | null;
  loadState: 'error' | 'loading' | 'ready';
  onCreate: () => void;
  onRetryContext: () => void;
  onRetryLoad: () => void;
  onSelect: (workspaceId: string) => void;
  options: readonly AccountWorkspaceOption[];
}

export interface AccountPreferenceOption {
  icon: ReactNode;
  label: string;
  pending?: boolean;
  value: string;
}

export interface AccountPreferenceFeedback {
  message: string;
  retryLabel: string;
}

export interface AccountPreferenceGroupModel {
  feedback: AccountPreferenceFeedback | null;
  label: string;
  onRetry: () => void;
  onSelect: (value: string) => void;
  options: readonly AccountPreferenceOption[];
  pendingLabel: string;
  value: string;
}

export interface AccountPreferencesModel {
  language: AccountPreferenceGroupModel;
  theme: AccountPreferenceGroupModel;
}

export interface AccountSurfaceProps {
  identity: AccountSurfaceIdentity;
  onSignOut: () => void;
  preferences: AccountPreferencesModel;
  signOutError?: boolean;
  signingOut?: boolean;
  surfaceId: SurfaceIdFor<'account-surface'>;
  transitionLocked?: boolean;
  workspace: AccountWorkspaceModel;
}

const accountActionTypography = cn(
  axisStyles.typography.scale.label,
  axisStyles.typography.weight.label,
);

const accountTargetGeometry = cn(
  axisStyles.density.minHeight.touchTarget,
  axisStyles.density.minHeight.compactControlAtSmall,
);

const accountSectionActionGeometry = cn(
  accountTargetGeometry,
  accountActionTypography,
  'w-full justify-center whitespace-normal',
);

const accountInlineActionGeometry = cn(accountTargetGeometry, accountActionTypography);

function AccountWorkspaceOptionButton({
  busy,
  onSelect,
  workspace,
}: {
  busy: boolean;
  onSelect: (workspaceId: string) => void;
  workspace: AccountWorkspaceOption;
}) {
  const pending = workspace.pending ?? false;
  const emphasized = workspace.current || pending;
  const WorkspaceIcon = workspace.kind === 'person' ? UserRound : Building2;

  return (
    <Button
      type="button"
      variant="ghost"
      size="sm"
      className={cn(
        axisStyles.density.minHeight.touchTarget,
        'w-full justify-start',
        axisStyles.spacing.padding.inline.inline,
        axisStyles.density.minHeight.compactControlAtSmall,
        emphasized ? persistentItemHighlight : transientItemHighlight,
        emphasized && 'disabled:opacity-100',
      )}
      disabled={workspace.current || busy || pending}
      aria-busy={pending || undefined}
      aria-current={workspace.current ? 'page' : undefined}
      onClick={() => onSelect(workspace.id)}
    >
      <OptionItemContent busy={pending} icon={<WorkspaceIcon className="size-3.5" />}>
        {workspace.label}
      </OptionItemContent>
    </Button>
  );
}

export function AccountWorkspaceSection({
  busy = false,
  feedback = null,
  loadState,
  onCreate,
  onRetryContext,
  onRetryLoad,
  onSelect,
  options,
}: AccountWorkspaceModel) {
  const { t } = useTranslation();

  return (
    <section
      data-axis-account-region="workspace"
      className={cn('grid', axisStyles.spacing.gap.inline, axisStyles.spacing.padding.all.region)}
      aria-label={t('workspace.label')}
      aria-busy={busy || undefined}
    >
      <div
        className={cn(
          'flex items-center text-muted-foreground',
          axisStyles.spacing.gap.inline,
          axisStyles.spacing.padding.inline.inline,
          axisStyles.typography.scale.metadata,
          axisStyles.typography.weight.label,
        )}
      >
        <PanelsTopLeft className={axisStyles.icon.size.control} aria-hidden />
        {t('workspace.label')}
      </div>

      <AsyncContent
        className={axisStyles.density.minHeight.defaultControl}
        pending={loadState === 'loading'}
        error={loadState === 'error'}
        pendingLabel={t('workspace.loading')}
      >
        {loadState === 'error' ? (
          <StatusNotice tone="destructive" title={t('workspace.unavailable')}>
            <Button
              type="button"
              size="sm"
              variant="outline"
              className={accountInlineActionGeometry}
              onClick={onRetryLoad}
            >
              {t('app.retry')}
            </Button>
          </StatusNotice>
        ) : loadState === 'ready' ? (
          <section
            className={cn('grid', axisStyles.spacing.gap.inline)}
            aria-label={t('workspace.eligible')}
          >
            {options.map((workspace) => (
              <AccountWorkspaceOptionButton
                key={workspace.id}
                busy={busy}
                workspace={workspace}
                onSelect={onSelect}
              />
            ))}
          </section>
        ) : null}
      </AsyncContent>

      <Button
        type="button"
        size="sm"
        variant="outline"
        className={cn(accountSectionActionGeometry, transientItemHighlight)}
        data-axis-account-role="section-action"
        disabled={busy}
        onClick={onCreate}
      >
        <span
          className={cn('flex shrink-0 items-center justify-center', axisStyles.icon.size.control)}
          aria-hidden
        >
          <Plus className="size-3.5" />
        </span>
        {t('workspace.createOrganization')}
      </Button>

      <div aria-live="polite" className={cn('grid', axisStyles.spacing.gap.inline)}>
        {busy ? <span className="sr-only">{t('workspace.switching')}</span> : null}
        {feedback === 'outcome-unknown' ? (
          <StatusNotice tone="warning">
            <span>{t('workspace.switchOutcomeUnknown')}</span>{' '}
            <Button
              type="button"
              variant="link"
              className={accountInlineActionGeometry}
              onClick={onRetryContext}
            >
              {t('workspace.retryRefresh')}
            </Button>
          </StatusNotice>
        ) : feedback === 'refresh-failed' ? (
          <StatusNotice tone="destructive">
            <span>{t('workspace.refreshFailedDescription')}</span>{' '}
            <Button
              type="button"
              variant="link"
              className={accountInlineActionGeometry}
              onClick={onRetryContext}
            >
              {t('workspace.retryRefresh')}
            </Button>
          </StatusNotice>
        ) : feedback === 'switch-failed' ? (
          <StatusNotice tone="destructive">{t('workspace.switchFailed')}</StatusNotice>
        ) : null}
      </div>
    </section>
  );
}

function AccountPreferenceGroup({
  kind,
  model,
}: {
  kind: keyof AccountPreferencesModel;
  model: AccountPreferenceGroupModel;
}) {
  const labelId = useId();
  const statusId = useId();
  const pending = model.options.some((option) => option.pending);
  const hasStatus = pending || model.feedback !== null;

  return (
    <section
      data-axis-account-preference={kind}
      aria-labelledby={labelId}
      aria-busy={pending || undefined}
      aria-describedby={hasStatus ? statusId : undefined}
      className={cn('grid', axisStyles.spacing.gap.inline)}
    >
      <div
        id={labelId}
        className={cn(
          'text-muted-foreground',
          axisStyles.spacing.padding.inline.inline,
          axisStyles.typography.scale.metadata,
          axisStyles.typography.weight.label,
        )}
      >
        {model.label}
      </div>
      <OptionList label={model.label} value={model.value} onValueChange={model.onSelect}>
        {model.options.map((option) => (
          <OptionListItem
            key={option.value}
            icon={option.icon}
            pending={option.pending}
            value={option.value}
          >
            {option.label}
          </OptionListItem>
        ))}
      </OptionList>
      {pending ? (
        <span id={statusId} role="status" className="sr-only">
          {model.pendingLabel}
        </span>
      ) : model.feedback ? (
        <div id={statusId} aria-live="polite">
          <StatusNotice tone="destructive">
            <span>{model.feedback.message}</span>{' '}
            <Button
              type="button"
              variant="link"
              size="sm"
              className={accountInlineActionGeometry}
              onClick={model.onRetry}
            >
              <RotateCcw aria-hidden />
              {model.feedback.retryLabel}
            </Button>
          </StatusNotice>
        </div>
      ) : null}
    </section>
  );
}

export function AccountSurface({
  identity,
  onSignOut,
  preferences,
  signOutError = false,
  signingOut = false,
  surfaceId,
  transitionLocked = false,
  workspace,
}: AccountSurfaceProps) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);

  return (
    <Popover
      open={open}
      onOpenChange={(nextOpen) => {
        if (!nextOpen && transitionLocked) return;
        setOpen(nextOpen);
      }}
    >
      <PopoverTrigger
        render={
          <Button
            type="button"
            variant="ghost"
            size="lg"
            className={cn(
              axisStyles.density.minHeight.touchTarget,
              'max-w-64 text-foreground',
              axisStyles.density.minHeight.compactControlAtSmall,
              axisStyles.spacing.gap.inline,
              axisStyles.spacing.padding.inline.inline,
              axisStyles.typography.scale.label,
              axisStyles.typography.weight.label,
              transientItemHighlight,
            )}
            aria-label={t('nav.accountMenuContext', { context: identity.triggerLabel })}
            title={t('nav.accountMenuContext', { context: identity.triggerLabel })}
          >
            {identity.triggerKind === 'organization' ? (
              <Avatar aria-hidden>
                <AvatarFallback>
                  <Building2 className={axisStyles.icon.size.control} />
                </AvatarFallback>
              </Avatar>
            ) : (
              <AccountAvatar initials={identity.initials} size="md" />
            )}
            <span className="hidden min-w-0 truncate sm:inline">{identity.triggerLabel}</span>
            <ChevronDown
              className={cn(axisStyles.icon.size.control, 'text-muted-foreground')}
              aria-hidden
            />
          </Button>
        }
      />
      <PopoverContent
        {...surfaceContractAttributes('account-surface', surfaceId)}
        data-slot="account-surface"
        align="end"
        className="max-h-(--available-height) w-80 max-w-(--available-width) gap-0 overflow-y-auto p-0"
        aria-label={t('nav.accountMenu')}
      >
        <section
          data-slot="account-identity"
          data-axis-account-region="identity"
          aria-label={t('app.account')}
          className={cn(
            'flex min-w-0 items-start',
            axisStyles.spacing.gap.inline,
            axisStyles.spacing.padding.all.region,
          )}
        >
          <AccountAvatar initials={identity.initials} size="md" />
          <div className="min-w-0 flex-1">
            <div
              className={cn(
                'whitespace-normal wrap-anywhere',
                axisStyles.typography.scale.label,
                axisStyles.typography.weight.label,
              )}
            >
              {identity.displayName}
            </div>
            {identity.secondaryLabel ? (
              <div
                className={cn(
                  'whitespace-normal wrap-anywhere text-muted-foreground',
                  axisStyles.typography.scale.metadata,
                )}
              >
                {identity.secondaryLabel}
              </div>
            ) : null}
          </div>
        </section>

        <Separator />

        <AccountWorkspaceSection {...workspace} />

        <Separator />

        <section
          data-axis-account-region="preferences"
          aria-label={t('app.preferences')}
          className={cn(
            'grid',
            axisStyles.spacing.gap.region,
            axisStyles.spacing.padding.all.region,
          )}
        >
          <div
            className={cn(
              'flex items-center text-muted-foreground',
              axisStyles.spacing.gap.inline,
              axisStyles.spacing.padding.inline.inline,
              axisStyles.typography.scale.metadata,
              axisStyles.typography.weight.label,
            )}
          >
            <Settings2 className={axisStyles.icon.size.control} aria-hidden />
            {t('app.preferences')}
          </div>
          <div className={cn('grid', axisStyles.spacing.gap.region)}>
            <AccountPreferenceGroup kind="language" model={preferences.language} />
            <AccountPreferenceGroup kind="theme" model={preferences.theme} />
          </div>
        </section>

        <Separator />

        <div
          data-axis-account-region="actions"
          className={cn(
            'grid',
            axisStyles.spacing.gap.inline,
            axisStyles.spacing.padding.all.region,
          )}
        >
          <AsyncButton
            type="button"
            variant="destructive"
            size="sm"
            className={cn(
              accountSectionActionGeometry,
              'border-destructive/30 text-foreground dark:border-destructive/40 [&_[data-slot=async-button-icon]]:text-destructive',
            )}
            data-axis-account-role="section-action"
            icon={<LogOut />}
            pending={signingOut}
            pendingLabel={t('nav.signingOut')}
            onClick={onSignOut}
          >
            {t('nav.signOut')}
          </AsyncButton>
          {signOutError ? (
            <StatusNotice tone="destructive">{t('nav.signOutFailed')}</StatusNotice>
          ) : null}
        </div>
      </PopoverContent>
    </Popover>
  );
}
