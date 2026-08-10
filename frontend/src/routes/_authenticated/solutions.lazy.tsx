import { createLazyFileRoute } from '@tanstack/react-router';
import { SolutionsPage } from '@/features/solutions';

export const Route = createLazyFileRoute('/_authenticated/solutions')({
  component: SolutionsPage,
});
