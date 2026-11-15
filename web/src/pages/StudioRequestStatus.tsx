import { Flex, Spin } from 'antd'
import { useContext } from 'react'
import { AxisAlert } from '../platform/AxisAlert'
import { PlatformContext } from './context'
import { resourceTitle, type StudioPreviewLink, type StudioRequest } from '../platform/studio'
import { useText } from '../platform/texts'

/**
 * The status line of a Studio request. A queued or running request shows the info icon, with a spinner beside its
 * text. A succeeded one shows its outcome, and a failed one shows an error. A succeeded publish or activate names its
 * release number once it is known. A preview, which is a publish request with target `preview`, has its own texts and
 * shows no release number. Proposed: a succeeded preview ends its line with a link for each site, "Open <site>", to its
 * address on the preview host, see the review on #3263. A test has its own texts too, and shows no release number. A
 * failed one never shows one, even when the request names a release, so a failure never looks like a success.
 */
export function StudioRequestStatus({
  request,
  releaseNumber,
  links = [],
}: {
  request: StudioRequest
  releaseNumber: number | null
  /** The sites a succeeded preview links to. */
  links?: readonly StudioPreviewLink[]
}) {
  const t = useText()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
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
        ) : kind === 'accept' ? (
          t('shell.studio.request.accepted')
        ) : kind === 'test' ? (
          t('shell.studio.request.tested')
        ) : preview ? (
          <>
            {t('shell.studio.request.previewed')}
            {links.map((link) => (
              <span key={link.url}>
                {' '}
                <a href={link.url} target="_blank" rel="noreferrer" data-testid="studio-preview-link">
                  {t('shell.studio.request.previewOpen').replace('{site}', resourceTitle(link.site, locale, t))}
                </a>
              </span>
            ))}
          </>
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
                : kind === 'accept'
                  ? 'shell.studio.request.acceptFailed'
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
