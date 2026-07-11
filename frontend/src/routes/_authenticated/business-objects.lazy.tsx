import { createLazyFileRoute } from '@tanstack/react-router';
import { BusinessObjectsPage } from '@/features/business-objects';

export const Route = createLazyFileRoute('/_authenticated/business-objects')({
  component: BusinessObjectsPage,
});
