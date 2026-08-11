import { Building2, ChevronDown, LogOut, Settings2 } from 'lucide-react';
import { type ReactNode, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { AccountAvatar } from '@/components/shared/AccountAvatar';
import { AsyncButton } from '@/components/shared/AsyncButton';
import { transientItemHighlight } from '@/components/shared/interactionStates';
import { StatusNotice } from '@/components/shared/StatusNotice';
import { Avatar, AvatarFallback } from '@/components/ui/avatar';
import { Button } from '@/components/ui/button';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { Separator } from '@/components/ui/separator';
import { type SurfaceIdFor, surfaceContractAttributes } from '@/lib/ui-foundation';

export interface AccountSurfaceIdentity {
  displayName: string;
  initials: string;
  secondaryLabel?: string;
  triggerKind: 'organization' | 'person';
  triggerLabel: string;
}

export interface AccountSurfaceProps {
  identity: AccountSurfaceIdentity;
  onSignOut: () => void;
  preferenceControls: ReactNode;
  signOutError?: boolean;
  signingOut?: boolean;
  surfaceId: SurfaceIdFor<'account-surface'>;
  transitionLocked?: boolean;
  workspace: ReactNode;
}

export function AccountSurface({
  identity,
  onSignOut,
  preferenceControls,
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
            className={`min-h-axis-touch-target max-w-64 gap-axis-inline px-axis-inline text-foreground ${transientItemHighlight}`}
            aria-label={t('nav.accountMenu')}
            title={t('nav.accountMenu')}
          >
            {identity.triggerKind === 'organization' ? (
              <Avatar aria-hidden>
                <AvatarFallback>
                  <Building2 className="size-4" />
                </AvatarFallback>
              </Avatar>
            ) : (
              <AccountAvatar initials={identity.initials} size="md" />
            )}
            <span className="hidden min-w-0 truncate sm:inline">{identity.triggerLabel}</span>
            <ChevronDown className="size-3.5 text-muted-foreground" aria-hidden />
          </Button>
        }
      />
      <PopoverContent
        {...surfaceContractAttributes('account-surface', surfaceId)}
        data-slot="account-surface"
        align="end"
        className="max-h-(--available-height) w-80 max-w-full overflow-y-auto"
        aria-label={t('nav.accountMenu')}
      >
        <section
          data-slot="account-identity"
          aria-label={t('app.account')}
          className="flex min-w-0 items-center gap-axis-inline px-axis-inline py-axis-inline"
        >
          <AccountAvatar initials={identity.initials} size="md" />
          <div className="min-w-0 flex-1">
            <div className="truncate text-axis-label font-axis-label">{identity.displayName}</div>
            {identity.secondaryLabel ? (
              <div className="truncate text-axis-metadata text-muted-foreground">
                {identity.secondaryLabel}
              </div>
            ) : null}
          </div>
        </section>

        <Separator />

        {workspace}

        <Separator />

        <section aria-label={t('app.preferences')} className="grid gap-axis-region">
          <div className="flex items-center gap-axis-inline px-axis-inline text-axis-metadata font-axis-label text-muted-foreground">
            <Settings2 className="size-3.5" aria-hidden />
            {t('app.preferences')}
          </div>
          {preferenceControls}
        </section>

        <Separator />

        <div className="grid gap-axis-inline">
          <AsyncButton
            type="button"
            variant="destructive"
            size="sm"
            className="min-h-axis-touch-target w-full justify-start sm:min-h-axis-compact-control"
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
