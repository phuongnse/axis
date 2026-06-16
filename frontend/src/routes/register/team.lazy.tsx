import { createLazyFileRoute } from '@tanstack/react-router';

import { RegisterTeamAccountPage } from '@/features/auth/components/RegisterTeamAccountPage';

export const Route = createLazyFileRoute('/register/team')({
  component: RegisterTeamAccountPage,
});
