import { Copyright } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { cn } from '@/lib/utils';
import { axisStyles } from '@/theme.generated';
import packageMetadata from '../../../package.json';

const COPYRIGHT_YEAR = '2026';

export function AppFooter() {
  const { t } = useTranslation();

  return (
    <footer className="shrink-0 border-t border-border bg-card">
      <div
        className={cn(
          'flex w-full min-w-0 flex-col text-muted-foreground sm:flex-row sm:items-center sm:justify-between',
          axisStyles.spacing.gap.inline,
          axisStyles.spacing.padding.inline.pageCompact,
          axisStyles.spacing.padding.block.region,
          axisStyles.typography.scale.metadata,
          axisStyles.typography.weight.metadata,
          axisStyles.spacing.padding.inline.pageDefaultAtSmall,
          axisStyles.spacing.padding.inline.pageWideAtLarge,
        )}
      >
        <p className="font-medium">{t('nav.version', { version: packageMetadata.version })}</p>

        <p className="inline-flex items-center gap-1.5 font-medium">
          <span>{t('app.productName')}</span>
          <Copyright className="size-3.5" aria-hidden />
          <span className="sr-only">{t('nav.copyright')}</span>
          <span>{COPYRIGHT_YEAR}</span>
        </p>
      </div>
    </footer>
  );
}
