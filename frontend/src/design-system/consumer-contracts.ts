export type AxisConsumerReadiness = 'ready' | 'candidate';

export type AxisConsumerSurfaceKind = 'public' | 'auth' | 'authenticated';

export type AxisConsumerEvidence =
  | 'api-state'
  | 'auth-state'
  | 'e2e-smoke'
  | 'form-state'
  | 'layout-state'
  | 'route-state'
  | 'unit-test';

export interface AxisConsumerContract {
  surface: string;
  kind: AxisConsumerSurfaceKind;
  route: string;
  component: string;
  file: string;
  owner: string;
  readiness: AxisConsumerReadiness;
  primitives: readonly string[];
  states: readonly string[];
  evidence: readonly AxisConsumerEvidence[];
  testFiles: readonly string[];
}

export const axisConsumerContracts = [
  {
    surface: 'User registration',
    kind: 'auth',
    route: '/register',
    component: 'RegisterPage',
    file: 'frontend/src/features/auth/components/RegisterPage.tsx',
    owner: 'auth',
    readiness: 'ready',
    primitives: ['AuthCard', 'AuthNotice', 'Button', 'Checkbox', 'Field', 'Input'],
    states: ['default', 'validation-error', 'submitting', 'backend-error'],
    evidence: ['unit-test', 'e2e-smoke', 'api-state', 'form-state'],
    testFiles: ['frontend/tests/register-page.test.tsx', 'frontend/e2e/register-user.pw.ts'],
  },
  {
    surface: 'Email confirmation',
    kind: 'auth',
    route: '/register_/confirmation',
    component: 'EmailConfirmationPage',
    file: 'frontend/src/features/auth/components/EmailConfirmationPage.tsx',
    owner: 'auth',
    readiness: 'ready',
    primitives: ['AuthCard', 'AuthNotice', 'Button'],
    states: ['default', 'resend-success'],
    evidence: ['unit-test', 'api-state'],
    testFiles: ['frontend/tests/email-confirmation-page.test.tsx'],
  },
  {
    surface: 'OIDC callback',
    kind: 'auth',
    route: '/callback',
    component: 'CallbackPage',
    file: 'frontend/src/features/auth/components/CallbackPage.tsx',
    owner: 'auth',
    readiness: 'ready',
    primitives: ['route-status-copy'],
    states: ['completing', 'invalid', 'failed'],
    evidence: ['unit-test', 'auth-state', 'route-state'],
    testFiles: ['frontend/tests/callback-page.test.tsx'],
  },
  {
    surface: 'Email verification',
    kind: 'auth',
    route: '/auth/verify',
    component: 'VerifyEmailPage',
    file: 'frontend/src/features/auth/components/VerifyEmailPage.tsx',
    owner: 'auth',
    readiness: 'ready',
    primitives: ['AuthCard', 'Button', 'Field', 'Input'],
    states: ['verifying', 'expired', 'invalid', 'already-used', 'rate-limited'],
    evidence: ['unit-test', 'api-state', 'auth-state', 'route-state'],
    testFiles: ['frontend/tests/verify-email-page.test.tsx'],
  },
  {
    surface: 'Authenticated shell',
    kind: 'authenticated',
    route: '/_authenticated',
    component: 'AppShell',
    file: 'frontend/src/components/shared/AppShell.tsx',
    owner: 'layout',
    readiness: 'ready',
    primitives: ['Button', 'navigation-shell'],
    states: ['default'],
    evidence: ['unit-test', 'auth-state', 'layout-state'],
    testFiles: ['frontend/tests/app-shell.test.tsx'],
  },
  {
    surface: 'Dashboard overview',
    kind: 'authenticated',
    route: '/_authenticated/dashboard',
    component: 'DashboardOverview',
    file: 'frontend/src/features/dashboard/components/DashboardOverview.tsx',
    owner: 'dashboard',
    readiness: 'ready',
    primitives: ['Card', 'Skeleton'],
    states: ['loaded', 'error'],
    evidence: ['unit-test', 'api-state', 'route-state'],
    testFiles: ['frontend/tests/dashboard-overview.test.tsx'],
  },
] as const satisfies readonly AxisConsumerContract[];
