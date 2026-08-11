import type { ReactNode } from 'react';

import { PageHeader, PageLayout } from '@/components/shared/PageLayout';
import { type SurfaceIdFor, surfaceContractAttributes } from '@/lib/ui-foundation';

interface ProcessWorkbenchProps {
  children: ReactNode;
  description?: ReactNode;
  surfaceId: SurfaceIdFor<'process-workbench'>;
  title: ReactNode;
}

function ProcessWorkbench({ children, description, surfaceId, title }: ProcessWorkbenchProps) {
  return (
    <PageLayout scrollMode="route">
      <div
        {...surfaceContractAttributes('process-workbench', surfaceId)}
        data-slot="process-workbench"
        className="contents"
      >
        <PageHeader title={title} description={description} />
        {children}
      </div>
    </PageLayout>
  );
}

export { ProcessWorkbench, type ProcessWorkbenchProps };
