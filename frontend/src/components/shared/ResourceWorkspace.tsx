import type { ReactNode } from 'react';
import { type PageActionChild, PageHeader, PageLayout } from '@/components/shared/PageLayout';

interface ResourceWorkspaceProps {
  actions?: PageActionChild | readonly PageActionChild[];
  children: ReactNode;
  description?: ReactNode;
  status?: ReactNode;
  title: ReactNode;
}

export function ResourceWorkspace({
  actions,
  children,
  description,
  status,
  title,
}: ResourceWorkspaceProps) {
  return (
    <PageLayout scrollMode="contained">
      <div data-slot="resource-workspace" className="flex min-h-0 min-w-0 flex-1 flex-col gap-4">
        <PageHeader title={title} description={description} actions={actions} />
        {status}
        <div data-slot="resource-workspace-content" className="min-h-0 min-w-0 flex-1">
          {children}
        </div>
      </div>
    </PageLayout>
  );
}

export type { ResourceWorkspaceProps };
