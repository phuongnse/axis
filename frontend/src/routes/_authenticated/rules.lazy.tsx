import { createLazyFileRoute } from '@tanstack/react-router';
import { RulesPage } from '@/features/rules';

export const Route = createLazyFileRoute('/_authenticated/rules')({
  component: RulesPage,
});
