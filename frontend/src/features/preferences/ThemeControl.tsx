import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Monitor, Moon, RotateCcw, Sun } from 'lucide-react';
import { useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/ui/button';
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';
import { getAccessToken } from '@/features/auth/auth-store';
import { type CurrentUserProfile, dashboardQueryKeys } from '@/features/dashboard/api';
import { updateThemePreference } from '@/features/preferences/api';
import {
  isSupportedThemeMode,
  persistThemeMode,
  setThemeMode,
  supportedThemeModes,
  type ThemeMode,
  useThemePreference,
} from '@/features/preferences/theme-store';
import { cn } from '@/lib/utils';

interface ThemeControlProps {
  authenticated?: boolean;
  className?: string;
  variant?: 'segmented' | 'menu';
}

const themeModeIcons = {
  system: Monitor,
  light: Sun,
  dark: Moon,
} satisfies Record<ThemeMode, typeof Monitor>;

export function ThemeControl({
  authenticated = false,
  className,
  variant = 'segmented',
}: ThemeControlProps) {
  const queryClient = useQueryClient();
  const { t } = useTranslation();
  const { mode } = useThemePreference();
  const [lastFailedTheme, setLastFailedTheme] = useState<ThemeMode | null>(null);
  const latestServerThemeRef = useRef<ThemeMode | null>(null);
  const isMenu = variant === 'menu';

  const mutation = useMutation({
    mutationFn: updateThemePreference,
    onSuccess: (data, variables) => {
      if (
        !isSupportedThemeMode(variables.theme) ||
        variables.theme !== latestServerThemeRef.current
      ) {
        return;
      }
      if (isSupportedThemeMode(data.theme)) {
        persistThemeMode(data.theme);
        queryClient.setQueryData<CurrentUserProfile | undefined>(
          dashboardQueryKeys.currentUser(),
          (profile) => (profile ? { ...profile, theme: data.theme } : profile),
        );
      }
      setLastFailedTheme(null);
    },
    onError: (_error, variables) => {
      if (
        !isSupportedThemeMode(variables?.theme) ||
        variables.theme !== latestServerThemeRef.current
      ) {
        return;
      }
      setLastFailedTheme(variables.theme);
    },
  });

  const shouldPersistToServer = authenticated && Boolean(getAccessToken());
  const showSaveStatus = authenticated && (mutation.isPending || mutation.isError);
  const statusId = showSaveStatus ? 'theme-save-status' : undefined;

  function chooseTheme(nextThemeMode: ThemeMode) {
    setThemeMode(nextThemeMode);
    setLastFailedTheme(null);
    if (shouldPersistToServer) {
      latestServerThemeRef.current = nextThemeMode;
      mutation.mutate({ theme: nextThemeMode });
    }
  }

  function retrySave() {
    const retryTheme = lastFailedTheme ?? mode;
    latestServerThemeRef.current = retryTheme;
    mutation.mutate({ theme: retryTheme });
  }

  function chooseToggleTheme(nextModes: string[]) {
    const nextThemeMode = nextModes[0];
    if (isSupportedThemeMode(nextThemeMode)) {
      chooseTheme(nextThemeMode);
    }
  }

  return (
    <div
      className={cn(
        isMenu ? 'grid gap-2' : 'flex flex-wrap items-center justify-end gap-2',
        className,
      )}
    >
      <fieldset aria-describedby={statusId} className={cn(isMenu && 'grid gap-1')}>
        <legend
          className={cn(isMenu ? 'mb-1 px-1 text-xs font-medium text-muted-foreground' : 'sr-only')}
        >
          {t('app.theme')}
        </legend>
        <ToggleGroup
          value={[mode]}
          onValueChange={chooseToggleTheme}
          orientation={isMenu ? 'vertical' : 'horizontal'}
          variant={isMenu ? 'default' : 'outline'}
          size="sm"
          aria-label={t('app.theme')}
        >
          {supportedThemeModes.map((item) => {
            const Icon = themeModeIcons[item.value];
            const label = t(item.labelKey);

            return (
              <ToggleGroupItem key={item.value} value={item.value} aria-label={label} title={label}>
                <Icon aria-hidden />
                {isMenu ? label : <span className="sr-only">{label}</span>}
              </ToggleGroupItem>
            );
          })}
        </ToggleGroup>
      </fieldset>

      {showSaveStatus ? (
        <div
          id={statusId}
          className={cn('min-h-4 text-xs text-muted-foreground', isMenu && 'px-1')}
          aria-live="polite"
        >
          {mutation.isPending ? t('app.saving') : null}
          {mutation.isError ? (
            <span className="inline-flex items-center gap-1 text-destructive">
              {t('app.themeSaveFailed')}
              <Button type="button" variant="link" size="sm" onClick={retrySave}>
                <RotateCcw aria-hidden />
                {t('app.retry')}
              </Button>
            </span>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}
