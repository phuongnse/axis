import type uiFoundationManifest from '../../ui-foundation.json';

type SurfaceContractId = keyof typeof uiFoundationManifest.contracts;

type EnforcedSurfaceContractId = keyof typeof uiFoundationManifest.enforcedContracts;

const activeSurfaceContracts = {
  'account-actions': 'account-surface',
  'authenticated-frame': 'authenticated-frame',
  'business-object-definitions': 'resource-workspace',
  'business-object-editor': 'managed-task-window',
  'email-confirmation': 'entry-surface',
  'invitation-acceptance': 'entry-surface',
  'managed-window-host': 'managed-task-window',
  'membership-management': 'resource-workspace',
  'membership-windows': 'managed-task-window',
  'organization-role-assignments': 'resource-workspace',
  'product-role-windows': 'managed-task-window',
  registration: 'entry-surface',
  'rule-definitions': 'resource-workspace',
  'rule-editor': 'managed-task-window',
  'service-identities': 'resource-workspace',
  'service-identity-windows': 'managed-task-window',
  'session-unavailable': 'entry-surface',
  'sign-in': 'entry-surface',
  'solution-delivery': 'resource-workspace',
  'solution-delivery-windows': 'managed-task-window',
  'verify-email': 'entry-surface',
} as const satisfies Record<string, SurfaceContractId>;

type ActiveSurfaceId = keyof typeof activeSurfaceContracts;

type SurfaceIdFor<Contract extends SurfaceContractId> = {
  [SurfaceId in ActiveSurfaceId]: (typeof activeSurfaceContracts)[SurfaceId] extends Contract
    ? SurfaceId
    : never;
}[ActiveSurfaceId];

function surfaceContractAttributes<Contract extends SurfaceContractId>(
  contract: Contract,
  surfaceId: SurfaceIdFor<Contract>,
) {
  if (activeSurfaceContracts[surfaceId] !== contract) {
    throw new Error(`Surface "${String(surfaceId)}" is not registered to contract "${contract}".`);
  }
  return {
    'data-axis-surface-contract': contract,
    'data-axis-surface-id': surfaceId,
  } as const;
}

export {
  type ActiveSurfaceId,
  activeSurfaceContracts,
  type EnforcedSurfaceContractId,
  type SurfaceContractId,
  type SurfaceIdFor,
  surfaceContractAttributes,
};
