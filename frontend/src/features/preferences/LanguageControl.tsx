import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Check, RotateCcw } from 'lucide-react';
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
import { cn } from '@/lib/utils';

interface LanguageControlProps {
  authenticated?: boolean;
  className?: string;
  variant?: 'segmented' | 'menu';
}

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
      <fieldset
        aria-describedby={statusId}
        className={cn(
          isMenu
            ? 'grid gap-1'
            : 'inline-flex h-8 overflow-hidden rounded-lg border border-border bg-background',
        )}
      >
        <legend
          className={cn(isMenu ? 'mb-1 px-1 text-xs font-medium text-muted-foreground' : 'sr-only')}
        >
          {t('app.language')}
        </legend>
        <ToggleGroup
          value={[activeLanguage]}
          onValueChange={chooseToggleLanguage}
          spacing={0}
          orientation={isMenu ? 'vertical' : 'horizontal'}
          variant={isMenu ? 'default' : 'outline'}
          size="sm"
          aria-label={t('app.language')}
          className={cn(isMenu ? 'w-full items-stretch gap-1' : 'h-8 rounded-lg')}
        >
          {supportedLanguages.map((item) => {
            const selected = activeLanguage === item.value;

            return (
              <ToggleGroupItem
                key={item.value}
                value={item.value}
                className={cn(
                  isMenu
                    ? 'flex h-9 w-full justify-between gap-3 rounded-md px-2.5 text-sm data-[state=on]:bg-primary/10 data-[state=on]:text-primary'
                    : 'min-w-10 rounded-none border-0 px-2.5 text-xs data-[state=on]:bg-primary data-[state=on]:text-primary-foreground',
                )}
              >
                {isMenu ? (
                  <>
                    <span className="flex min-w-0 items-center gap-2">
                      <span
                        className={cn(
                          'flex h-5 min-w-7 items-center justify-center rounded border px-1 text-[10px] font-semibold',
                          selected
                            ? 'border-primary/20 bg-background text-primary'
                            : 'border-border bg-muted/40 text-muted-foreground',
                        )}
                      >
                        {item.shortLabel}
                      </span>
                      <span className="truncate">{item.label}</span>
                    </span>
                    <Check
                      className={cn('size-3.5', selected ? 'opacity-100' : 'opacity-0')}
                      aria-hidden
                    />
                  </>
                ) : (
                  item.shortLabel
                )}
              </ToggleGroupItem>
            );
          })}
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
              <Button
                type="button"
                variant="link"
                size="sm"
                className="h-6 px-1.5 text-[11px]"
                onClick={retrySave}
              >
                <RotateCcw className="size-3" aria-hidden />
                {t('app.retry')}
              </Button>
            </span>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}
