import { Languages, Monitor, Moon, Sun } from 'lucide-react';
import { useTranslation } from 'react-i18next';

import { Button } from '@/components/ui/button';
import { SUPPORTED_LANGUAGES, type SupportedLanguage } from '@/features/preferences/i18n-resources';
import {
  THEME_MODES,
  type ThemeMode,
  usePreferencesStore,
} from '@/features/preferences/preferences-store';
import { cn } from '@/lib/utils';

const themeIcons = {
  light: Sun,
  dark: Moon,
  system: Monitor,
} satisfies Record<ThemeMode, typeof Sun>;

const languageLabels = {
  en: 'EN',
  vi: 'VI',
} satisfies Record<SupportedLanguage, string>;

export function PreferenceControls({ className }: { className?: string }) {
  const { t } = useTranslation();
  const language = usePreferencesStore((state) => state.language);
  const setLanguage = usePreferencesStore((state) => state.setLanguage);
  const themeMode = usePreferencesStore((state) => state.themeMode);
  const setThemeMode = usePreferencesStore((state) => state.setThemeMode);

  return (
    <div
      className={cn(
        'inline-flex h-9 items-center gap-1 rounded-md border border-border bg-card/95 p-1 text-muted-foreground shadow-sm backdrop-blur',
        className,
      )}
    >
      <span className="sr-only">{t('preferences.controlsLabel')}</span>
      <div className="flex items-center gap-0.5">
        <Languages className="mx-1 size-3.5 text-muted-foreground" aria-hidden />
        {SUPPORTED_LANGUAGES.map((item) => (
          <Button
            key={item}
            type="button"
            variant={language === item ? 'secondary' : 'ghost'}
            size="xs"
            aria-pressed={language === item}
            aria-label={t('preferences.setLanguage', {
              language: item === 'en' ? t('preferences.english') : t('preferences.vietnamese'),
            })}
            onClick={() => setLanguage(item)}
            className="min-w-7 px-1.5 text-[11px]"
          >
            {languageLabels[item]}
          </Button>
        ))}
      </div>

      <span className="h-4 w-px bg-border" aria-hidden />

      <div className="flex items-center gap-0.5">
        {THEME_MODES.map((mode) => {
          const Icon = themeIcons[mode];
          return (
            <Button
              key={mode}
              type="button"
              variant={themeMode === mode ? 'secondary' : 'ghost'}
              size="icon-xs"
              aria-pressed={themeMode === mode}
              aria-label={t('preferences.setTheme', {
                theme: t(`preferences.${mode}`),
              })}
              title={t(`preferences.${mode}`)}
              onClick={() => setThemeMode(mode)}
            >
              <Icon className="size-3.5" aria-hidden />
            </Button>
          );
        })}
      </div>
    </div>
  );
}
