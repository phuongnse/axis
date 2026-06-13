import { createLazyFileRoute } from '@tanstack/react-router';

import { CallbackPage } from '@/features/auth/components/CallbackPage';

export const Route = createLazyFileRoute('/callback')({
  component: CallbackPage,
});
