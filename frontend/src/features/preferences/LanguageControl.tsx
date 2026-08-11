import { useMutation, useQueryClient } from '@tanstack/react-query';
import { RotateCcw } from 'lucide-react';
import { useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { AccountPreferenceGroupModel } from '@/components/shared/AccountSurface';
import { AsyncContent } from '@/components/shared/AsyncContent';
import { OptionList, OptionListItem } from '@/components/shared/OptionList';
import { Button } from '@/components/ui/button';
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';
import { getBrowserSessionStatus } from '@/features/auth/auth-store';
import { type CurrentUserProfile, dashboardQueryKeys } from '@/features/dashboard/api';
import { updateLanguagePreference } from '@/features/preferences/api';
import { changeSiteLanguage, currentSiteLanguage } from '@/features/preferences/i18n';
import {
  isSupportedLanguage,
  persistLanguage,
  type SupportedLanguage,
  supportedLanguages,
} from '@/features/preferences/language-store';
import type { TranslationKey } from '@/features/preferences/translations';
import { cn } from '@/lib/utils';

interface LanguageControlProps {
  authenticated?: boolean;
  className?: string;
  variant?: 'segmented' | 'menu';
}

const languageLabelKeys = {
  en: 'app.languageEnglish',
  vi: 'app.languageVietnamese',
} satisfies Record<SupportedLanguage, TranslationKey>;

const languageBadges = {
  en: 'EN',
  vi: 'VI',
} satisfies Record<SupportedLanguage, string>;

function useLanguagePreferenceState(authenticated: boolean) {
  const queryClient = useQueryClient();
  const { i18n, t } = useTranslation();
  const language = currentSiteLanguage();
  const [lastFailedLanguage, setLastFailedLanguage] = useState<SupportedLanguage | null>(null);
  const latestServerLanguageRef = useRef<SupportedLanguage | null>(null);

  const mutation = useMutation({
    mutationFn: updateLanguagePreference,
    onSuccess: (data, variables) => {
      if (
        !isSupportedLanguage(variables.language) ||
        variables.language !== latestServerLanguageRef.current
      ) {
        return;
      }
      if (isSupportedLanguage(data.language)) {
        persistLanguage(data.language);
        queryClient.setQueryData<CurrentUserProfile | undefined>(
          dashboardQueryKeys.currentUser(),
          (profile) => (profile ? { ...profile, language: data.language } : profile),
        );
      }
      setLastFailedLanguage(null);
    },
    onError: (_error, variables) => {
      if (
        !isSupportedLanguage(variables?.language) ||
        variables.language !== latestServerLanguageRef.current
      ) {
        return;
      }
      setLastFailedLanguage(variables.language);
    },
  });

  const shouldPersistToServer = authenticated && getBrowserSessionStatus() === 'authenticated';
  function chooseLanguage(nextLanguage: SupportedLanguage) {
    void changeSiteLanguage(nextLanguage);
    setLastFailedLanguage(null);
    if (shouldPersistToServer) {
      latestServerLanguageRef.current = nextLanguage;
      mutation.mutate({ language: nextLanguage });
    }
  }

  function retrySave() {
    const retryLanguage = lastFailedLanguage ?? language;
    latestServerLanguageRef.current = retryLanguage;
    mutation.mutate({ language: retryLanguage });
  }

  function chooseToggleLanguage(nextLanguages: string[]) {
    const nextLanguage = nextLanguages[0];
    if (isSupportedLanguage(nextLanguage)) {
      chooseLanguage(nextLanguage);
    }
  }

  const activeLanguage = isSupportedLanguage(i18n.resolvedLanguage)
    ? i18n.resolvedLanguage
    : language;

  return {
    activeLanguage,
    chooseLanguage,
    chooseToggleLanguage,
    latestServerLanguageRef,
    mutation,
    retrySave,
    t,
  };
}

export function useAccountLanguagePreferenceModel(): AccountPreferenceGroupModel {
  const { activeLanguage, chooseLanguage, latestServerLanguageRef, mutation, retrySave, t } =
    useLanguagePreferenceState(true);

  return {
    feedback: mutation.isError
      ? { message: t('app.languageSaveFailed'), retryLabel: t('app.retry') }
      : null,
    label: t('app.language'),
    onRetry: retrySave,
    onSelect: (value) => {
      if (isSupportedLanguage(value)) chooseLanguage(value);
    },
    options: supportedLanguages.map((item) => ({
      icon: languageBadges[item.value],
      label: t(languageLabelKeys[item.value]),
      pending: mutation.isPending && latestServerLanguageRef.current === item.value,
      value: item.value,
    })),
    pendingLabel: t('app.saving'),
    value: activeLanguage,
  };
}

export function LanguageControl({
  authenticated = false,
  className,
  variant = 'segmented',
}: LanguageControlProps) {
  const { activeLanguage, chooseToggleLanguage, latestServerLanguageRef, mutation, retrySave, t } =
    useLanguagePreferenceState(authenticated);
  const statusId = authenticated ? 'language-save-status' : undefined;
  const isMenu = variant === 'menu';

  return (
    <div
      className={cn(
        isMenu
          ? 'relative grid gap-axis-inline'
          : 'flex flex-wrap items-center justify-end gap-axis-inline',
        className,
      )}
    >
      <fieldset
        aria-busy={mutation.isPending || undefined}
        aria-describedby={statusId}
        className={cn(isMenu && 'grid gap-axis-inline')}
      >
        <legend
          className={cn(
            isMenu
              ? 'px-axis-inline text-axis-metadata font-axis-label text-muted-foreground'
              : 'sr-only',
          )}
        >
          {t('app.language')}
        </legend>
        {isMenu ? (
          <OptionList
            label={t('app.language')}
            value={activeLanguage}
            onValueChange={(value) => chooseToggleLanguage([value])}
          >
            {supportedLanguages.map((item) => (
              <OptionListItem
                key={item.value}
                icon={languageBadges[item.value]}
                pending={mutation.isPending && latestServerLanguageRef.current === item.value}
                value={item.value}
              >
                {t(languageLabelKeys[item.value])}
              </OptionListItem>
            ))}
          </OptionList>
        ) : (
          <ToggleGroup
            aria-label={t('app.language')}
            orientation="horizontal"
            size="sm"
            value={[activeLanguage]}
            variant="outline"
            onValueChange={chooseToggleLanguage}
          >
            {supportedLanguages.map((item) => (
              <ToggleGroupItem key={item.value} value={item.value}>
                {t(languageLabelKeys[item.value])}
              </ToggleGroupItem>
            ))}
          </ToggleGroup>
        )}
      </fieldset>

      {authenticated ? (
        <AsyncContent
          id={statusId}
          className={cn(
            'min-h-5 text-axis-metadata text-muted-foreground',
            isMenu && 'px-axis-inline sr-only',
          )}
          error={mutation.isError}
          pending={mutation.isPending}
          pendingLabel={t('app.saving')}
        >
          {mutation.isError ? (
            <span className="inline-flex items-center gap-1 text-destructive">
              {t('app.languageSaveFailed')}
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
