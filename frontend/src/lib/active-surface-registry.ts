import { AccountSurface } from '@/components/shared/AccountSurface';
import { AppHeader } from '@/components/shared/AppHeader';
import { AuthenticatedFrame } from '@/components/shared/AuthenticatedFrame';
import { EntrySurface } from '@/components/shared/EntrySurface';
import { ManagedDialog } from '@/components/shared/ManagedDialog';
import { ManagedWindowHost } from '@/components/shared/ManagedWindowHost';
import { ProcessWorkbench } from '@/components/shared/ProcessWorkbench';
import { ResourceWorkspace } from '@/components/shared/ResourceWorkspace';
import { EmailConfirmationPage } from '@/features/auth/components/EmailConfirmationPage';
import { RegisterPage } from '@/features/auth/components/RegisterPage';
import { SessionUnavailablePage } from '@/features/auth/components/SessionUnavailablePage';
import { SignInPage } from '@/features/auth/components/SignInPage';
import { VerifyEmailPage } from '@/features/auth/components/VerifyEmailPage';
import { BusinessObjectDefinitionDialog } from '@/features/business-objects/components/BusinessObjectDefinitionDialog';
import { BusinessObjectsPage } from '@/features/business-objects/components/BusinessObjectsPage';
import { AcceptWorkspaceInvitationPage } from '@/features/memberships/components/AcceptWorkspaceInvitationPage';
import { MembershipManagementPage } from '@/features/memberships/components/MembershipManagementPage';
import { membershipsManagedWindowRenderers } from '@/features/memberships/managed-windows';
import { ProductRoleAssignmentsPage } from '@/features/product-roles/components/ProductRoleAssignmentsPage';
import { productRolesManagedWindowRenderers } from '@/features/product-roles/managed-windows';
import { RuleEditorDialog } from '@/features/rules/components/RuleEditorDialog';
import { RulesPage } from '@/features/rules/components/RulesPage';
import { ServiceIdentitiesPage } from '@/features/service-identities/components/ServiceIdentitiesPage';
import { serviceIdentitiesManagedWindowRenderers } from '@/features/service-identities/managed-windows';
import { SolutionsPage } from '@/features/solutions/components/SolutionsPage';
import type { ActiveSurfaceId, SurfaceContractId } from '@/lib/ui-foundation';
import { Route as AuthenticatedRoute } from '@/routes/_authenticated';

const surfaceContractOwners = {
  'account-surface': AccountSurface,
  'authenticated-frame': AuthenticatedFrame,
  'entry-surface': EntrySurface,
  'managed-task-window': ManagedDialog,
  'process-workbench': ProcessWorkbench,
  'resource-workspace': ResourceWorkspace,
} satisfies Record<SurfaceContractId, unknown>;

// Real module symbols make both inventories compiler-checked. Surface owners also require a
// contract-compatible surface id, so registration and composition cannot drift independently.
const activeSurfaceImplementations = {
  'account-actions': AppHeader,
  'authenticated-frame': AuthenticatedRoute,
  'business-object-definitions': BusinessObjectsPage,
  'business-object-editor': BusinessObjectDefinitionDialog,
  'email-confirmation': EmailConfirmationPage,
  'invitation-acceptance': AcceptWorkspaceInvitationPage,
  'managed-window-host': ManagedWindowHost,
  'membership-management': MembershipManagementPage,
  'membership-windows': membershipsManagedWindowRenderers,
  'organization-role-assignments': ProductRoleAssignmentsPage,
  'product-role-windows': productRolesManagedWindowRenderers,
  registration: RegisterPage,
  'rule-definitions': RulesPage,
  'rule-editor': RuleEditorDialog,
  'service-identities': ServiceIdentitiesPage,
  'service-identity-windows': serviceIdentitiesManagedWindowRenderers,
  'session-unavailable': SessionUnavailablePage,
  'sign-in': SignInPage,
  'solution-delivery': SolutionsPage,
  'verify-email': VerifyEmailPage,
} satisfies Record<ActiveSurfaceId, unknown>;

export { activeSurfaceImplementations, surfaceContractOwners };
