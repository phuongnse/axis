import { Flex, Spin } from 'antd'
import { AxisAlert } from '../platform/AxisAlert'
import type { StudioRequest } from '../platform/studio'
import { useText } from '../platform/texts'

/**
 * The status line of a Studio request. A queued or running request shows the info icon, with a spinner beside its
 * text. A succeeded one shows its outcome, and a failed one shows an error. A succeeded publish or activate names its
 * release number once it is known. A preview, which is a publish request with target `preview`, has its own texts and
 * shows no release number. A test has its own texts too, and shows no release number. A failed one never shows one,
 * even when the request names a release, so a failure never looks like a success.
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
  const preview = request.target === 'preview'
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
        ) : kind === 'test' ? (
          t('shell.studio.request.tested')
        ) : preview ? (
          t('shell.studio.request.previewed')
        ) : (
          <>
            {t(kind === 'publish' ? 'shell.studio.request.published' : 'shell.studio.request.activated')}
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
        preview
          ? 'shell.studio.request.previewFailed'
          : kind === 'publish'
            ? 'shell.studio.request.publishFailed'
            : kind === 'activate'
              ? 'shell.studio.request.activationFailed'
              : kind === 'test'
                ? 'shell.studio.request.testFailed'
                : 'shell.studio.request.validateFailed',
      )
      break
  }
  const pending = request.status === 'queued' || request.status === 'running'
  return (
    <AxisAlert
      data-testid="studio-request-status"
      data-status={request.status}
      kind={pending ? 'info' : request.status === 'succeeded' ? 'success' : 'error'}
      message={
        pending ? (
          <Flex gap="small" align="center">
            <Spin size="small" />
            {message}
          </Flex>
        ) : (
          message
        )
      }
    />
  )
}
