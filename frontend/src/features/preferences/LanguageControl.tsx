import { useMutation, useQueryClient } from '@tanstack/react-query';
import { RotateCcw } from 'lucide-react';
import { useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/ui/button';
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';
import { getAccessToken } from '@/features/auth/auth-store';
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

export function LanguageControl({
  authenticated = false,
  className,
  variant = 'segmented',
}: LanguageControlProps) {
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

  const shouldPersistToServer = authenticated && Boolean(getAccessToken());
  const statusId = authenticated ? 'language-save-status' : undefined;

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

  const isMenu = variant === 'menu';
  const activeLanguage = isSupportedLanguage(i18n.resolvedLanguage)
    ? i18n.resolvedLanguage
    : language;

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
          {t('app.language')}
        </legend>
        <ToggleGroup
          value={[activeLanguage]}
          onValueChange={chooseToggleLanguage}
          orientation={isMenu ? 'vertical' : 'horizontal'}
          variant={isMenu ? 'default' : 'outline'}
          size="sm"
          width={isMenu ? 'full' : 'auto'}
          aria-label={t('app.language')}
        >
          {supportedLanguages.map((item) => (
            <ToggleGroupItem
              key={item.value}
              value={item.value}
              align={isMenu ? 'start' : 'center'}
            >
              {isMenu ? (
                <span
                  aria-hidden
                  className="flex size-4 shrink-0 items-center justify-center text-[8px] font-semibold leading-none"
                >
                  {languageBadges[item.value]}
                </span>
              ) : null}
              {t(languageLabelKeys[item.value])}
            </ToggleGroupItem>
          ))}
        </ToggleGroup>
      </fieldset>

      {authenticated ? (
        <div
          id={statusId}
          className={cn('min-h-4 text-[11px] text-muted-foreground', isMenu && 'px-1')}
          aria-live="polite"
        >
          {mutation.isPending ? t('app.saving') : null}
          {mutation.isSuccess && !mutation.isPending ? t('app.languageSaved') : null}
          {mutation.isError ? (
            <span className="inline-flex items-center gap-1 text-destructive">
              {t('app.languageSaveFailed')}
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
