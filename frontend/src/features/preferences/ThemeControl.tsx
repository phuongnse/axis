import { Monitor, Moon, Sun } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';
import {
  isSupportedThemeMode,
  setThemeMode,
  supportedThemeModes,
  type ThemeMode,
  useThemePreference,
} from '@/features/preferences/theme-store';
import { cn } from '@/lib/utils';

interface ThemeControlProps {
  className?: string;
  variant?: 'segmented' | 'menu';
}

const themeModeIcons = {
  system: Monitor,
  light: Sun,
  dark: Moon,
} satisfies Record<ThemeMode, typeof Monitor>;

export function ThemeControl({ className, variant = 'segmented' }: ThemeControlProps) {
  const { t } = useTranslation();
  const { mode } = useThemePreference();
  const isMenu = variant === 'menu';

  function chooseToggleTheme(nextModes: string[]) {
    const nextThemeMode = nextModes[0];
    if (isSupportedThemeMode(nextThemeMode)) {
      setThemeMode(nextThemeMode);
    }
  }

  return (
    <fieldset className={cn(isMenu && 'grid gap-1', className)}>
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
        width={isMenu ? 'full' : 'auto'}
        aria-label={t('app.theme')}
      >
        {supportedThemeModes.map((item) => {
          const Icon = themeModeIcons[item.value];
          const label = t(item.labelKey);

          return (
            <ToggleGroupItem
              key={item.value}
              value={item.value}
              align={isMenu ? 'start' : 'center'}
              aria-label={label}
              title={label}
            >
              <Icon aria-hidden />
              {isMenu ? label : <span className="sr-only">{label}</span>}
            </ToggleGroupItem>
          );
        })}
      </ToggleGroup>
    </fieldset>
  );
}
