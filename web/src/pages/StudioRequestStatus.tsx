import { Flex, Spin } from 'antd'
import { useContext } from 'react'
import { AxisAlert } from '../platform/AxisAlert'
import { fillText } from '../platform/fillText'
import { type PreviewLink, resourceTitle, type StudioRequest } from '../platform/studio'
import { useText } from '../platform/texts'
import { PlatformContext } from './context'

/**
 * The status line of a Studio request. A queued or running request shows the info icon, with a spinner beside its
 * text. A succeeded one shows its outcome, and a failed one shows an error. A succeeded publish or activate names its
 * release number once it is known. A preview, which is a publish request with target `preview`, has its own texts and
 * shows no release number. A test has its own texts too, and shows no release number. An accept, which is an `accept`
 * publish request, says whether the change is in the application, and shows no release number, see
 * docs/reference/ai-authoring-in-studio.md#accepting. A failed one never shows one, even when the request names a
 * release, so a failure never looks like a success. A succeeded preview ends with one link for each of `previewLinks`,
 * named by its site, such as "Open Purchasing", see docs/reference/studio.md#publishing and
 * docs/reference/ai-authoring-in-studio.md#preview-the-draft.
 */
export function StudioRequestStatus({
  request,
  releaseNumber,
  previewLinks = [],
}: {
  request: StudioRequest
  releaseNumber: number | null
  previewLinks?: PreviewLink[]
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
        kind === 'accept' ? (
          t('shell.studio.request.accepted')
        ) : kind === 'validate' ? (
          t('shell.studio.request.validated')
        ) : kind === 'test' ? (
          t('shell.studio.request.tested')
        ) : preview ? (
          <>
            {t('shell.studio.request.previewed')}
            {previewLinks.map(({ site, url }) => (
              <span key={url}>
                {' '}
                <a data-testid="studio-preview-link" href={url} target="_blank" rel="noreferrer">
                  {fillText(t('shell.studio.request.openSite'), { site: resourceTitle(site, locale, t) })}
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
        kind === 'accept'
          ? 'shell.studio.request.acceptFailed'
          : preview
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
