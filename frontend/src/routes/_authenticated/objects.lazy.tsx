import { createLazyFileRoute } from '@tanstack/react-router';
import { BusinessObjectsPage } from '@/features/objects/components/BusinessObjectsPage';

export const Route = createLazyFileRoute('/_authenticated/objects')({
  component: BusinessObjectsPage,
});
