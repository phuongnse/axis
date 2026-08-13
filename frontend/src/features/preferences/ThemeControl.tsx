import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Monitor, Moon, RotateCcw, Sun } from 'lucide-react';
import { useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { AccountPreferenceGroupModel } from '@/components/shared/AccountSurface';
import { AsyncContent } from '@/components/shared/AsyncContent';
import type { EntryPreferenceGroupModel } from '@/components/shared/EntrySurface';
import { OptionList, OptionListItem } from '@/components/shared/OptionList';
import { Button } from '@/components/ui/button';
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';
import { getBrowserSessionStatus } from '@/features/auth/auth-store';
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
import { axisStyles } from '@/theme.generated';

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

function useThemePreferenceState(authenticated: boolean) {
  const queryClient = useQueryClient();
  const { t } = useTranslation();
  const { mode } = useThemePreference();
  const [lastFailedTheme, setLastFailedTheme] = useState<ThemeMode | null>(null);
  const latestServerThemeRef = useRef<ThemeMode | null>(null);

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

  const shouldPersistToServer = authenticated && getBrowserSessionStatus() === 'authenticated';
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

  return {
    chooseTheme,
    chooseToggleTheme,
    latestServerThemeRef,
    mode,
    mutation,
    retrySave,
    t,
  };
}

export function useAccountThemePreferenceModel(): AccountPreferenceGroupModel {
  const { chooseTheme, latestServerThemeRef, mode, mutation, retrySave, t } =
    useThemePreferenceState(true);

  return {
    feedback: mutation.isError
      ? { message: t('app.themeSaveFailed'), retryLabel: t('app.retry') }
      : null,
    label: t('app.theme'),
    onRetry: retrySave,
    onSelect: (value) => {
      if (isSupportedThemeMode(value)) chooseTheme(value);
    },
    options: supportedThemeModes.map((item) => {
      const Icon = themeModeIcons[item.value];
      return {
        icon: <Icon />,
        label: t(item.labelKey),
        pending: mutation.isPending && latestServerThemeRef.current === item.value,
        value: item.value,
      };
    }),
    pendingLabel: t('app.saving'),
    value: mode,
  };
}

export function useEntryThemePreferenceModel(): EntryPreferenceGroupModel {
  const { t } = useTranslation();
  const { mode } = useThemePreference();

  return {
    label: t('app.theme'),
    onSelect: (value) => {
      if (isSupportedThemeMode(value)) setThemeMode(value);
    },
    options: supportedThemeModes.map((item) => {
      const Icon = themeModeIcons[item.value];
      return {
        icon: <Icon />,
        label: t(item.labelKey),
        value: item.value,
      };
    }),
    value: mode,
  };
}

export function ThemeControl({
  authenticated = false,
  className,
  variant = 'segmented',
}: ThemeControlProps) {
  const { chooseToggleTheme, latestServerThemeRef, mode, mutation, retrySave, t } =
    useThemePreferenceState(authenticated);
  const statusId = authenticated ? 'theme-save-status' : undefined;
  const isMenu = variant === 'menu';

  return (
    <div
      className={cn(
        isMenu
          ? cn('relative grid', axisStyles.spacing.gap.inline)
          : cn('flex flex-wrap items-center justify-end', axisStyles.spacing.gap.inline),
        className,
      )}
    >
      <fieldset
        aria-busy={mutation.isPending || undefined}
        aria-describedby={statusId}
        className={cn(isMenu && 'grid', isMenu && axisStyles.spacing.gap.inline)}
      >
        <legend
          className={cn(
            isMenu ? 'text-muted-foreground' : 'sr-only',
            isMenu && axisStyles.spacing.padding.inline.inline,
            isMenu && axisStyles.typography.scale.metadata,
            isMenu && axisStyles.typography.weight.label,
          )}
        >
          {t('app.theme')}
        </legend>
        {isMenu ? (
          <OptionList
            label={t('app.theme')}
            value={mode}
            onValueChange={(value) => chooseToggleTheme([value])}
          >
            {supportedThemeModes.map((item) => {
              const Icon = themeModeIcons[item.value];
              const label = t(item.labelKey);

              return (
                <OptionListItem
                  key={item.value}
                  icon={<Icon />}
                  pending={mutation.isPending && latestServerThemeRef.current === item.value}
                  value={item.value}
                >
                  {label}
                </OptionListItem>
              );
            })}
          </OptionList>
        ) : (
          <ToggleGroup
            aria-label={t('app.theme')}
            orientation="horizontal"
            size="sm"
            value={[mode]}
            variant="outline"
            onValueChange={chooseToggleTheme}
          >
            {supportedThemeModes.map((item) => {
              const Icon = themeModeIcons[item.value];
              const label = t(item.labelKey);

              return (
                <ToggleGroupItem
                  key={item.value}
                  value={item.value}
                  aria-label={label}
                  title={label}
                >
                  <Icon aria-hidden />
                  <span className="sr-only">{label}</span>
                </ToggleGroupItem>
              );
            })}
          </ToggleGroup>
        )}
      </fieldset>

      {authenticated ? (
        <AsyncContent
          id={statusId}
          className={cn(
            'min-h-5 text-muted-foreground',
            axisStyles.typography.scale.metadata,
            isMenu && axisStyles.spacing.padding.inline.inline,
            isMenu && 'sr-only',
          )}
          error={mutation.isError}
          pending={mutation.isPending}
          pendingLabel={t('app.saving')}
        >
          {mutation.isError ? (
            <span className="inline-flex items-center gap-1 text-destructive">
              {t('app.themeSaveFailed')}
              <Button type="button" variant="link" size="sm" onClick={retrySave}>
                <RotateCcw aria-hidden />
                {t('app.retry')}
              </Button>
            </span>
          ) : null}
        </AsyncContent>
      ) : null}
    </div>
  );
}
