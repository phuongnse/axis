import { ChevronDown, Settings2 } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/ui/button';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { LanguageControl } from '@/features/preferences/LanguageControl';
import { ThemeControl } from '@/features/preferences/ThemeControl';
import { useThemePreference } from '@/features/preferences/theme-store';
import { cn } from '@/lib/utils';

interface PreferencesMenuProps {
  authenticated?: boolean;
  className?: string;
}

export function PreferencesMenu({ authenticated = false, className }: PreferencesMenuProps) {
  const { t } = useTranslation();
  useThemePreference();

  return (
    <div className={cn('inline-flex', className)}>
      <Popover>
        <PopoverTrigger
          render={
            <Button
              type="button"
              variant="outline"
              size="sm"
              aria-label={t('app.preferences')}
              title={t('app.preferences')}
            />
          }
        >
          <Settings2 aria-hidden />
          <span>{t('app.preferences')}</span>
          <ChevronDown aria-hidden />
        </PopoverTrigger>
        <PopoverContent align="end" aria-label={t('app.preferences')}>
          <LanguageControl authenticated={authenticated} variant="menu" />
          <ThemeControl authenticated={authenticated} variant="menu" />
        </PopoverContent>
      </Popover>
    </div>
  );
}
