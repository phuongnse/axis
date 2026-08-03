import { createLazyFileRoute } from '@tanstack/react-router';
import { ApplicationsPage } from '@/features/applications';

export const Route = createLazyFileRoute('/_authenticated/applications')({
  component: ApplicationsPage,
});
