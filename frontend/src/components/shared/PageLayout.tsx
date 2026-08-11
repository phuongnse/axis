import type { ComponentProps, ReactElement, ReactNode } from 'react';

import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import { axisStyles } from '@/theme.generated';

type PageScrollMode = 'contained' | 'route';

interface EntryLayoutProps {
  children: ReactNode;
  utilities?: ReactNode;
}

function EntryLayout({ children, utilities }: EntryLayoutProps) {
  return (
    <div
      data-slot="entry-layout"
      className={cn(
        'flex min-h-dvh w-full min-w-0 flex-col overflow-x-hidden bg-background',
        axisStyles.spacing.padding.all.pageCompact,
        axisStyles.spacing.padding.all.pageDefaultAtSmall,
        axisStyles.spacing.padding.all.pageWideAtLarge,
      )}
    >
      {utilities ? (
        <div data-slot="entry-utilities" className="flex shrink-0 justify-end">
          {utilities}
        </div>
      ) : null}
      <main
        data-slot="entry-content"
        className={cn(
          'mx-auto flex w-full max-w-lg flex-1 items-center justify-center',
          axisStyles.spacing.padding.block.region,
        )}
      >
        {children}
      </main>
    </div>
  );
}

interface PageLayoutProps {
  children: ReactNode;
  scrollMode: PageScrollMode;
}

type PageActionProps = Omit<ComponentProps<typeof Button>, 'className'>;

function PageAction(props: PageActionProps) {
  return (
    <Button
      {...props}
      className={cn(
        axisStyles.density.minHeight.touchTarget,
        axisStyles.density.minWidth.touchTarget,
        axisStyles.density.minHeight.compactControlAtSmall,
        axisStyles.density.minWidth.compactControlAtSmall,
      )}
    />
  );
}

type PageActionElement = ReactElement<PageActionProps, typeof PageAction>;
type PageActionChild = PageActionElement | null | false | undefined;

function PageLayout({ children, scrollMode }: PageLayoutProps) {
  return (
    <div
      data-slot="page-layout"
      data-scroll-mode={scrollMode}
      className={cn(
        'flex h-full min-h-0 w-full min-w-0 flex-col',
        axisStyles.spacing.gap.region,
        axisStyles.spacing.padding.all.pageCompact,
        axisStyles.spacing.padding.all.pageDefaultAtSmall,
        axisStyles.spacing.padding.all.pageWideAtLarge,
        scrollMode === 'contained' ? 'overflow-hidden' : 'overflow-x-hidden overflow-y-auto',
      )}
    >
      {children}
    </div>
  );
}

interface PageHeaderProps {
  title: ReactNode;
  description?: ReactNode;
  actions?: PageActionChild | readonly PageActionChild[];
}

function PageHeader({ title, description, actions }: PageHeaderProps) {
  return (
    <header
      data-slot="page-header"
      className={cn(
        'flex min-w-0 shrink-0 flex-col sm:flex-row sm:items-start sm:justify-between',
        axisStyles.spacing.gap.region,
      )}
    >
      <div data-slot="page-header-content" className="min-w-0 space-y-1">
        <h1
          data-slot="page-title"
          className={cn(
            'font-heading text-foreground',
            axisStyles.typography.scale.pageTitle,
            axisStyles.typography.weight.pageTitle,
          )}
        >
          {title}
        </h1>
        {description ? (
          <p
            data-slot="page-description"
            className={cn(
              'max-w-3xl text-muted-foreground',
              axisStyles.typography.scale.body,
              axisStyles.typography.weight.body,
            )}
          >
            {description}
          </p>
        ) : null}
      </div>
      {actions ? (
        <div
          data-slot="page-actions"
          className={cn(
            'flex w-full flex-wrap items-center sm:w-auto sm:justify-end',
            axisStyles.spacing.gap.inline,
          )}
        >
          {actions}
        </div>
      ) : null}
    </header>
  );
}

interface SectionHeaderProps {
  actions?: ReactNode;
  description?: ReactNode;
  id: string;
  title: ReactNode;
}

function SectionHeader({ actions, description, id, title }: SectionHeaderProps) {
  return (
    <div
      data-slot="section-header"
      className={cn(
        'flex min-w-0 flex-wrap items-start justify-between',
        axisStyles.spacing.gap.region,
      )}
    >
      <div data-slot="section-header-content" className="min-w-0 space-y-1">
        <h2
          id={id}
          data-slot="section-title"
          className={cn(
            'font-heading',
            axisStyles.typography.scale.sectionTitle,
            axisStyles.typography.weight.sectionTitle,
          )}
        >
          {title}
        </h2>
        {description ? (
          <p
            data-slot="section-description"
            className={cn(
              'max-w-3xl break-words text-muted-foreground',
              axisStyles.typography.scale.body,
              axisStyles.typography.weight.body,
            )}
          >
            {description}
          </p>
        ) : null}
      </div>
      {actions ? (
        <div
          data-slot="section-actions"
          className={cn('flex shrink-0 flex-wrap items-center', axisStyles.spacing.gap.inline)}
        >
          {actions}
        </div>
      ) : null}
    </div>
  );
}

export {
  EntryLayout,
  type EntryLayoutProps,
  PageAction,
  type PageActionChild,
  type PageActionElement,
  type PageActionProps,
  PageHeader,
  type PageHeaderProps,
  PageLayout,
  type PageLayoutProps,
  type PageScrollMode,
  SectionHeader,
  type SectionHeaderProps,
};
