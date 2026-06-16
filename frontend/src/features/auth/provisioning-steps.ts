import type { ProvisioningStatusResponse } from '@/features/auth/types';

export type ProvisioningStepVisual = 'pending' | 'active' | 'complete' | 'failed';

export interface ProvisioningUiState {
  steps: ProvisioningStepVisual[];
  showAttemptLine: boolean;
  attemptCount: number;
  failed: boolean;
}

export function deriveProvisioningUiState(status: ProvisioningStatusResponse): ProvisioningUiState {
  const modules = status.modules ?? [];
  const maxAttempt = modules.reduce((max, module) => Math.max(max, module.attemptCount ?? 0), 0);
  const allModulesSucceeded =
    modules.length > 0 && modules.every((module) => module.status === 'Succeeded');
  const failed = status.tenantStatus === 'ProvisioningFailed';

  if (failed) {
    return {
      steps: ['failed', 'pending', 'pending'],
      showAttemptLine: false,
      attemptCount: maxAttempt,
      failed: true,
    };
  }

  if (status.isReady) {
    return {
      steps: ['complete', 'complete', 'complete'],
      showAttemptLine: false,
      attemptCount: maxAttempt,
      failed: false,
    };
  }

  if (allModulesSucceeded) {
    return {
      steps: ['complete', 'active', 'pending'],
      showAttemptLine: false,
      attemptCount: maxAttempt,
      failed: false,
    };
  }

  const showAttemptLine = maxAttempt > 0 && modules.some((module) => module.status === 'Failed');
  return {
    steps: ['active', 'pending', 'pending'],
    showAttemptLine,
    attemptCount: maxAttempt,
    failed: false,
  };
}
