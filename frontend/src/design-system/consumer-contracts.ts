export type AxisConsumerReadiness = 'ready' | 'candidate';

export type AxisConsumerSurfaceKind = 'public' | 'auth' | 'authenticated';

export type AxisConsumerEvidence =
  | 'api-state'
  | 'auth-state'
  | 'e2e-smoke'
  | 'form-state'
  | 'i18n'
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
    surface: 'Public landing',
    kind: 'public',
    route: '/',
    component: 'LandingPage',
    file: 'frontend/src/features/landing/components/LandingPage.tsx',
    owner: 'landing',
    readiness: 'ready',
    primitives: [
      'ActionLink',
      'AccessPathTrace',
      'BrandHeader',
      'HeaderRule',
      'TopologyBackdrop',
      'PreferenceControls',
    ],
    states: ['default', 'responsive', 'theme-adaptive'],
    evidence: ['e2e-smoke', 'i18n', 'layout-state'],
    testFiles: ['frontend/e2e/local-dev-smoke.pw.ts'],
  },
  {
    surface: 'Sign in',
    kind: 'auth',
    route: '/login',
    component: 'LoginPage',
    file: 'frontend/src/features/auth/components/LoginPage.tsx',
    owner: 'auth',
    readiness: 'ready',
    primitives: ['AuthCard', 'AuthNotice', 'Button', 'Field', 'Input', 'PreferenceControls'],
    states: ['default', 'validation-error'],
    evidence: ['unit-test', 'form-state', 'i18n'],
    testFiles: ['frontend/tests/login-page.test.tsx'],
  },
  {
    surface: 'User registration',
    kind: 'auth',
    route: '/register',
    component: 'RegisterPage',
    file: 'frontend/src/features/auth/components/RegisterPage.tsx',
    owner: 'auth',
    readiness: 'ready',
    primitives: [
      'AuthCard',
      'AuthNotice',
      'Button',
      'Checkbox',
      'Field',
      'Input',
      'PreferenceControls',
    ],
    states: ['default', 'validation-error', 'submitting', 'backend-error'],
    evidence: ['unit-test', 'e2e-smoke', 'api-state', 'form-state', 'i18n'],
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
    evidence: ['unit-test', 'api-state', 'i18n'],
    testFiles: ['frontend/tests/email-confirmation-page.test.tsx'],
  },
  {
    surface: 'Forgot password',
    kind: 'auth',
    route: '/forgot-password',
    component: 'ForgotPasswordPage',
    file: 'frontend/src/features/auth/components/ForgotPasswordPage.tsx',
    owner: 'auth',
    readiness: 'candidate',
    primitives: ['AuthCard', 'Button', 'Field', 'Input'],
    states: ['disabled-form'],
    evidence: ['unit-test', 'form-state', 'i18n'],
    testFiles: ['frontend/tests/forgot-password-page.test.tsx'],
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
    evidence: ['unit-test', 'auth-state', 'route-state', 'i18n'],
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
    evidence: ['unit-test', 'api-state', 'auth-state', 'route-state', 'i18n'],
    testFiles: ['frontend/tests/verify-email-page.test.tsx'],
  },
  {
    surface: 'Workspace provisioning',
    kind: 'auth',
    route: '/provisioning',
    component: 'WorkspaceProvisioningPage',
    file: 'frontend/src/features/auth/components/WorkspaceProvisioningPage.tsx',
    owner: 'auth',
    readiness: 'ready',
    primitives: ['Alert', 'AuthCard', 'Badge', 'Button', 'FlowTrace'],
    states: ['provisioning', 'ready', 'failed'],
    evidence: ['unit-test', 'api-state', 'route-state', 'i18n'],
    testFiles: ['frontend/tests/workspace-provisioning-page.test.tsx'],
  },
  {
    surface: 'Authenticated shell',
    kind: 'authenticated',
    route: '/_authenticated',
    component: 'AppShell',
    file: 'frontend/src/components/shared/AppShell.tsx',
    owner: 'layout',
    readiness: 'ready',
    primitives: ['Button', 'Input', 'NativeSelect', 'navigation-shell'],
    states: ['default', 'workspace-switching'],
    evidence: ['unit-test', 'auth-state', 'layout-state', 'i18n'],
    testFiles: ['frontend/tests/app-shell.test.tsx'],
  },
  {
    surface: 'Dashboard overview',
    kind: 'authenticated',
    route: '/_authenticated/dashboard',
    component: 'DashboardOverview',
    file: 'frontend/src/features/dashboard/components/DashboardOverview.tsx',
    owner: 'dashboard',
    readiness: 'candidate',
    primitives: ['Badge', 'Card', 'Progress', 'Skeleton'],
    states: ['loaded', 'no-workspace'],
    evidence: ['unit-test', 'api-state', 'route-state'],
    testFiles: ['frontend/tests/dashboard-overview.test.tsx'],
  },
] as const satisfies readonly AxisConsumerContract[];
