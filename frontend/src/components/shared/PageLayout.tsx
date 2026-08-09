import type { ComponentProps, ReactElement, ReactNode } from 'react';

import { Button } from '@/components/ui/button';

type PageScrollMode = 'contained' | 'route';

interface PageLayoutProps {
  children: ReactNode;
  scrollMode: PageScrollMode;
}

type PageActionProps = Omit<ComponentProps<typeof Button>, 'className'>;

function PageAction(props: PageActionProps) {
  return <Button {...props} className="min-h-11 min-w-11 sm:min-h-8 sm:min-w-8" />;
}

type PageActionElement = ReactElement<PageActionProps, typeof PageAction>;
type PageActionChild = PageActionElement | null | false | undefined;

function PageLayout({ children, scrollMode }: PageLayoutProps) {
  return (
    <div
      data-slot="page-layout"
      data-scroll-mode={scrollMode}
      className={`flex h-full min-h-0 w-full min-w-0 flex-col gap-4 p-4 sm:p-6 lg:p-8 ${
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
      className="flex min-w-0 shrink-0 flex-col gap-4 sm:flex-row sm:items-start sm:justify-between"
    >
      <div data-slot="page-header-content" className="min-w-0 space-y-1">
        <h1 data-slot="page-title" className="font-heading text-2xl font-semibold text-foreground">
          {title}
        </h1>
        {description ? (
          <p
            data-slot="page-description"
            className="max-w-3xl text-sm leading-6 text-muted-foreground"
          >
            {description}
          </p>
        ) : null}
      </div>
      {actions ? (
        <div
          data-slot="page-actions"
          className="flex w-full flex-wrap items-center gap-2 sm:w-auto sm:justify-end"
        >
          {actions}
        </div>
      ) : null}
    </header>
  );
}

export {
  PageAction,
  type PageActionChild,
  type PageActionElement,
  type PageActionProps,
  PageHeader,
  type PageHeaderProps,
  PageLayout,
  type PageLayoutProps,
  type PageScrollMode,
};
