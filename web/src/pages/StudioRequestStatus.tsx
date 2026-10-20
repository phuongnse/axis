import { Alert, Spin } from 'antd'
import type { StudioRequest } from '../platform/studio'
import { useText } from '../platform/texts'

/**
 * The status line of a Studio request. A queued or running request shows a spinner, a succeeded one shows its
 * outcome, and a failed one shows an error. A succeeded publish names its release number once it is known. A failed
 * publish never shows one, even when the request names a release, so a failure never looks like a success.
 */
export function StudioRequestStatus({
  request,
  releaseNumber,
}: {
  request: StudioRequest
  releaseNumber: number | null
}) {
  const t = useText()
  const publish = request.kind === 'publish'
  let message
  switch (request.status) {
    case 'queued':
      message = t('shell.studio.request.queued')
      break
    case 'running':
      message = t('shell.studio.request.running')
      break
    case 'succeeded':
      message = publish ? (
        <>
          {t('shell.studio.request.published')}
          {releaseNumber !== null && (
            <>
              {' '}
              <strong data-testid="studio-release-number">{releaseNumber}</strong>
            </>
          )}
        </>
      ) : (
        t('shell.studio.request.validated')
      )
      break
    case 'failed':
      message = publish ? t('shell.studio.request.publishFailed') : t('shell.studio.request.validateFailed')
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
