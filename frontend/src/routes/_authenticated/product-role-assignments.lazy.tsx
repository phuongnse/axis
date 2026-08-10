import { createLazyFileRoute } from '@tanstack/react-router';
import { ProductRoleAssignmentsPage } from '@/features/product-roles';

export const Route = createLazyFileRoute('/_authenticated/product-role-assignments')({
  component: ProductRoleAssignmentsPage,
});
