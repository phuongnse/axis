import { ChevronDown, Globe2 } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/ui/button';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { currentSiteLanguage } from '@/features/preferences/i18n';
import { LanguageControl } from '@/features/preferences/LanguageControl';
import { supportedLanguages } from '@/features/preferences/language-store';
import { cn } from '@/lib/utils';

interface PreferencesMenuProps {
  authenticated?: boolean;
  className?: string;
}

export function PreferencesMenu({ authenticated = false, className }: PreferencesMenuProps) {
  const { t } = useTranslation();
  const language = currentSiteLanguage();
  const currentLanguage =
    supportedLanguages.find((item) => item.value === language) ?? supportedLanguages[0];

  return (
    <div className={cn('inline-flex', className)}>
      <Popover>
        <PopoverTrigger
          render={
            <Button
              type="button"
              variant="outline"
              size="sm"
              className="group h-8 gap-1.5 rounded-lg border-border/80 bg-card px-2.5 text-xs text-muted-foreground shadow-none hover:text-foreground data-[popup-open]:border-primary/25 data-[popup-open]:bg-primary/5 data-[popup-open]:text-primary"
              aria-label={t('app.preferences')}
              title={t('app.preferences')}
            />
          }
        >
          <Globe2 className="size-3.5" aria-hidden />
          <span className="min-w-5 font-semibold text-foreground">
            {currentLanguage.shortLabel}
          </span>
          <ChevronDown
            className="size-3 opacity-60 transition-transform group-data-[popup-open]:rotate-180"
            aria-hidden
          />
        </PopoverTrigger>
        <PopoverContent
          align="end"
          sideOffset={8}
          aria-label={t('app.preferences')}
          className="w-56 gap-0 border-border/80 p-2 shadow-[0_16px_40px_color-mix(in_oklch,var(--foreground),transparent_86%)] ring-0"
        >
          <LanguageControl authenticated={authenticated} variant="menu" />
        </PopoverContent>
      </Popover>
    </div>
  );
}
