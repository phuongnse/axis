import { createLazyFileRoute } from '@tanstack/react-router';

import { EmailConfirmationPage } from '@/features/auth/components/EmailConfirmationPage';

export const Route = createLazyFileRoute('/register_/confirmation')({
  component: EmailConfirmationPage,
});
