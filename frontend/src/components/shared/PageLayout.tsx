import type { ComponentProps, ReactElement, ReactNode } from 'react';

import { Button } from '@/components/ui/button';

type PageScrollMode = 'contained' | 'route';

interface EntryLayoutProps {
  children: ReactNode;
  utilities?: ReactNode;
}

function EntryLayout({ children, utilities }: EntryLayoutProps) {
  return (
    <div
      data-slot="entry-layout"
      className="flex min-h-dvh w-full min-w-0 flex-col overflow-x-hidden bg-background p-axis-page-compact sm:p-axis-page-default lg:p-axis-page-wide"
    >
      {utilities ? (
        <div data-slot="entry-utilities" className="flex shrink-0 justify-end">
          {utilities}
        </div>
      ) : null}
      <main
        data-slot="entry-content"
        className="mx-auto flex w-full max-w-lg flex-1 items-center justify-center py-axis-region"
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
      className="min-h-axis-touch-target min-w-axis-touch-target sm:min-h-axis-compact-control sm:min-w-axis-compact-control"
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
      className={`flex h-full min-h-0 w-full min-w-0 flex-col gap-axis-region p-axis-page-compact sm:p-axis-page-default lg:p-axis-page-wide ${
        scrollMode === 'contained' ? 'overflow-hidden' : 'overflow-x-hidden overflow-y-auto'
      }`}
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
      className="flex min-w-0 shrink-0 flex-col gap-axis-region sm:flex-row sm:items-start sm:justify-between"
    >
      <div data-slot="page-header-content" className="min-w-0 space-y-1">
        <h1
          data-slot="page-title"
          className="font-heading text-axis-page-title font-axis-page-title text-foreground"
        >
          {title}
        </h1>
        {description ? (
          <p
            data-slot="page-description"
            className="max-w-3xl text-axis-body font-axis-body text-muted-foreground"
          >
            {description}
          </p>
        ) : null}
      </div>
      {actions ? (
        <div
          data-slot="page-actions"
          className="flex w-full flex-wrap items-center gap-axis-inline sm:w-auto sm:justify-end"
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
      className="flex min-w-0 flex-wrap items-start justify-between gap-axis-region"
    >
      <div data-slot="section-header-content" className="min-w-0 space-y-1">
        <h2
          id={id}
          data-slot="section-title"
          className="font-heading text-axis-section-title font-axis-section-title"
        >
          {title}
        </h2>
        {description ? (
          <p
            data-slot="section-description"
            className="max-w-3xl break-words text-axis-body font-axis-body text-muted-foreground"
          >
            {description}
          </p>
        ) : null}
      </div>
      {actions ? (
        <div
          data-slot="section-actions"
          className="flex shrink-0 flex-wrap items-center gap-axis-inline"
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
