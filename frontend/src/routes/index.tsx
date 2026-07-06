import { createFileRoute } from '@tanstack/react-router';

import { redirectFromAppEntryRoute } from '@/features/auth/route-guards';

export const Route = createFileRoute('/')({
  beforeLoad: redirectFromAppEntryRoute,
});
