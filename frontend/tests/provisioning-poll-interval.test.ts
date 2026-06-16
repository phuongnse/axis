import { describe, expect, it } from 'vitest';

import { resolveProvisioningPollInterval } from '@/features/auth/hooks/useProvisioningStatus';
import type { ProvisioningStatusResponse } from '@/features/auth/types';
import { ApiError } from '@/lib/api';

function makeStatus(
  overrides: Partial<ProvisioningStatusResponse> = {},
): ProvisioningStatusResponse {
  return {
    tenantId: '00000000-0000-0000-0000-000000000001',
    tenantStatus: 'Provisioning',
    isReady: false,
    modules: [],
    ...overrides,
  };
}

describe('resolveProvisioningPollInterval', () => {
  it('keeps polling while provisioning is in progress', () => {
    expect(resolveProvisioningPollInterval(makeStatus(), null)).toBe(5000);
  });

  it('stops polling once the workspace is ready', () => {
    expect(resolveProvisioningPollInterval(makeStatus({ isReady: true }), null)).toBe(false);
  });

  it('keeps polling after a transient network error (no data yet)', () => {
    expect(resolveProvisioningPollInterval(undefined, new Error('network'))).toBe(5000);
  });

  it('keeps polling after a transient 5xx error', () => {
    expect(resolveProvisioningPollInterval(undefined, new ApiError(503, null))).toBe(5000);
  });

  it('stops polling on a terminal 4xx (invalid/expired token)', () => {
    expect(resolveProvisioningPollInterval(undefined, new ApiError(404, null))).toBe(false);
  });

  it('still stops when ready even if a later refetch 4xxs', () => {
    expect(
      resolveProvisioningPollInterval(makeStatus({ isReady: true }), new ApiError(404, null)),
    ).toBe(false);
  });
});
