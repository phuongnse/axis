import { createLazyFileRoute } from '@tanstack/react-router';

import { VerifyEmailPage } from '@/features/auth/components/VerifyEmailPage';

export const Route = createLazyFileRoute('/auth/verify')({
  component: VerifyEmailPage,
});
