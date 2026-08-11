import type { ReactNode } from 'react';
import { type PageActionChild, PageHeader, PageLayout } from '@/components/shared/PageLayout';
import { type SurfaceIdFor, surfaceContractAttributes } from '@/lib/ui-foundation';
import { cn } from '@/lib/utils';
import { axisStyles } from '@/theme.generated';

interface ResourceWorkspaceProps {
  actions?: PageActionChild | readonly PageActionChild[];
  children: ReactNode;
  description?: ReactNode;
  status?: ReactNode;
  surfaceId: SurfaceIdFor<'resource-workspace'>;
  title: ReactNode;
}

export function ResourceWorkspace({
  actions,
  children,
  description,
  status,
  surfaceId,
  title,
}: ResourceWorkspaceProps) {
  return (
    <PageLayout scrollMode="contained">
      <div
        {...surfaceContractAttributes('resource-workspace', surfaceId)}
        data-slot="resource-workspace"
        className={cn('flex min-h-0 min-w-0 flex-1 flex-col', axisStyles.spacing.gap.region)}
      >
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
