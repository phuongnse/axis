import { createLazyFileRoute } from '@tanstack/react-router';
import { MembershipManagementPage } from '@/features/memberships';

export const Route = createLazyFileRoute('/_authenticated/memberships')({
  component: MembershipManagementPage,
});
