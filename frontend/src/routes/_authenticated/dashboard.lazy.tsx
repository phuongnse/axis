import { createLazyFileRoute } from '@tanstack/react-router';

import { DashboardOverview } from '@/features/dashboard/components/DashboardOverview';

export const Route = createLazyFileRoute('/_authenticated/dashboard')({
  component: DashboardPage,
});

function DashboardPage() {
  return <DashboardOverview />;
}
