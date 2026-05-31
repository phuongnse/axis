import { createLazyFileRoute } from '@tanstack/react-router';

import { RegisterCompletePage } from '@/features/auth/components/RegisterCompletePage';

export const Route = createLazyFileRoute('/register/complete')({
  component: RegisterCompleteRoute,
});

function RegisterCompleteRoute() {
  const params = new URLSearchParams(window.location.search);
  const sessionId = params.get('session');

  if (!sessionId) {
    return <RegisterCompletePage sessionId="" invalidSession />;
  }

  return <RegisterCompletePage sessionId={sessionId} />;
}
