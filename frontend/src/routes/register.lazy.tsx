import { createLazyFileRoute } from '@tanstack/react-router';

import { RegisterPage } from '@/features/auth/components/RegisterPage';

export const Route = createLazyFileRoute('/register')({
  component: RegisterPage,
});
