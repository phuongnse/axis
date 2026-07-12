import { useQuery } from '@tanstack/react-query';
import { ChevronDown, LogOut, Settings2 } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { AccountAvatar } from '@/components/shared/AccountAvatar';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { Separator } from '@/components/ui/separator';
import { useAuthStore } from '@/features/auth/auth-store';
import { sessionDisplayFromLabel } from '@/features/auth/session-from-token';
import { dashboardQueryKeys, getCurrentUserProfile } from '@/features/dashboard/api';
import { LanguageControl, ThemeControl } from '@/features/preferences';

interface AppActionsMenuProps {
  onSignOut: () => void;
  signOutError?: boolean;
  signingOut?: boolean;
}

function firstNonEmpty(...values: Array<string | null | undefined>): string | null {
  for (const value of values) {
    const trimmed = value?.trim();
    if (trimmed) return trimmed;
  }
  return null;
}

export function AppActionsMenu({
  onSignOut,
  signOutError = false,
  signingOut = false,
}: AppActionsMenuProps) {
  const { t } = useTranslation();
  const accessToken = useAuthStore((state) => state.accessToken);
  const userLabel = useAuthStore((state) => state.userLabel);
  const userInitials = useAuthStore((state) => state.userInitials);
  const profileQuery = useQuery({
    queryKey: dashboardQueryKeys.currentUser(),
    queryFn: getCurrentUserProfile,
    enabled: Boolean(accessToken),
  });
  const profileLabel = firstNonEmpty(profileQuery.data?.fullName, profileQuery.data?.email);
  const profileDisplay = profileLabel ? sessionDisplayFromLabel(profileLabel) : null;
  const displayName = profileDisplay?.userLabel ?? userLabel ?? t('nav.user');
  const displayInitials = profileDisplay?.userInitials ?? userInitials ?? '?';

  return (
    <Popover>
      <PopoverTrigger
        render={
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="max-w-56"
            aria-label={t('nav.accountMenu')}
            title={t('nav.accountMenu')}
          >
            <AccountAvatar initials={displayInitials} />
            <span className="hidden min-w-0 truncate sm:inline">{displayName}</span>
            <ChevronDown className="size-3.5" aria-hidden />
          </Button>
        }
      />
      <PopoverContent align="end" className="w-80" aria-label={t('nav.accountMenu')}>
        <section aria-label={t('app.preferences')} className="grid gap-3">
          <div className="flex items-center gap-2 px-1 text-xs font-medium text-muted-foreground">
            <Settings2 className="size-3.5" aria-hidden />
            {t('app.preferences')}
          </div>
          <LanguageControl authenticated variant="menu" />
          <ThemeControl authenticated variant="menu" />
        </section>

        <Separator />

        <Button
          type="button"
          variant="destructive"
          size="sm"
          className="w-full"
          aria-busy={signingOut}
          disabled={signingOut}
          onClick={onSignOut}
        >
          <LogOut className="size-3.5" aria-hidden />
          {signingOut ? t('nav.signingOut') : t('nav.signOut')}
        </Button>
        {signOutError ? (
          <Alert variant="destructive">
            <AlertDescription>{t('nav.signOutFailed')}</AlertDescription>
          </Alert>
        ) : null}
      </PopoverContent>
    </Popover>
  );
}
