import { describe, expect, it } from 'vitest';

import { deriveProvisioningUiState } from '@/features/auth/provisioning-steps';
import type { ProvisioningStatusResponse } from '@/features/auth/types';

function makeStatus(
  overrides: Partial<ProvisioningStatusResponse> = {},
): ProvisioningStatusResponse {
  return {
    tenantId: '00000000-0000-0000-0000-000000000001',
    tenantStatus: 'Provisioning',
    isReady: false,
    modules: [
      { module: 'DataModeling', status: 'Pending', attemptCount: 0, lastError: null },
      { module: 'WorkflowBuilder', status: 'Pending', attemptCount: 0, lastError: null },
    ],
    ...overrides,
  };
}

describe('deriveProvisioningUiState', () => {
  it('shows first step active while modules are provisioning', () => {
    const ui = deriveProvisioningUiState(makeStatus());
    expect(ui.steps).toEqual(['active', 'pending', 'pending']);
    expect(ui.failed).toBe(false);
  });

  it('shows attempt line when modules are retrying', () => {
    const ui = deriveProvisioningUiState(
      makeStatus({
        modules: [
          { module: 'DataModeling', status: 'Failed', attemptCount: 2, lastError: 'timeout' },
          { module: 'WorkflowBuilder', status: 'Pending', attemptCount: 0, lastError: null },
        ],
      }),
    );
    expect(ui.showAttemptLine).toBe(true);
    expect(ui.attemptCount).toBe(2);
  });

  it('marks all steps complete when workspace is ready', () => {
    const ui = deriveProvisioningUiState(
      makeStatus({
        tenantStatus: 'Active',
        isReady: true,
        modules: [
          { module: 'DataModeling', status: 'Succeeded', attemptCount: 1, lastError: null },
          { module: 'WorkflowBuilder', status: 'Succeeded', attemptCount: 1, lastError: null },
        ],
      }),
    );
    expect(ui.steps).toEqual(['complete', 'complete', 'complete']);
  });

  it('shows failed state when provisioning exhausted retries', () => {
    const ui = deriveProvisioningUiState(
      makeStatus({
        tenantStatus: 'ProvisioningFailed',
        modules: [
          { module: 'DataModeling', status: 'Failed', attemptCount: 3, lastError: 'timeout' },
        ],
      }),
    );
    expect(ui.failed).toBe(true);
    expect(ui.steps[0]).toBe('failed');
  });
});
