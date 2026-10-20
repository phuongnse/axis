import { Alert, Spin } from 'antd'
import type { StudioRequest } from '../platform/studio'
import { useText } from '../platform/texts'

/**
 * The status line of a Studio request. A queued or running request shows a spinner, a succeeded one shows its
 * outcome, and a failed one shows an error. A succeeded publish or rollback names its release number once it is
 * known. A failed one never shows one, even when the request names a release, so a failure never looks like a
 * success.
 */
export function StudioRequestStatus({
  request,
  releaseNumber,
}: {
  request: StudioRequest
  releaseNumber: number | null
}) {
  const t = useText()
  const kind = request.kind
  let message
  switch (request.status) {
    case 'queued':
      message = t('shell.studio.request.queued')
      break
    case 'running':
      message = t('shell.studio.request.running')
      break
    case 'succeeded':
      message =
        kind === 'validate' ? (
          t('shell.studio.request.validated')
        ) : (
          <>
            {t(kind === 'publish' ? 'shell.studio.request.published' : 'shell.studio.request.rolledBack')}
            {releaseNumber !== null && (
              <>
                {' '}
                <strong data-testid="studio-release-number">{releaseNumber}</strong>
              </>
            )}
          </>
        )
      break
    case 'failed':
      message = t(
        kind === 'publish'
          ? 'shell.studio.request.publishFailed'
          : kind === 'rollback'
            ? 'shell.studio.request.rollbackFailed'
            : 'shell.studio.request.validateFailed',
      )
      break
  }
  const pending = request.status === 'queued' || request.status === 'running'
  return (
    <Alert
      data-testid="studio-request-status"
      data-status={request.status}
      type={pending ? 'info' : request.status === 'succeeded' ? 'success' : 'error'}
      showIcon={!pending}
      icon={pending ? <Spin size="small" /> : undefined}
      message={message}
    />
  )
}
