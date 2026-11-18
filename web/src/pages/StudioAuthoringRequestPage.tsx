import { Flex, Spin, Typography } from 'antd'
import { useContext, useState } from 'react'
import { AxisButton } from '../platform/AxisButton'
import { fillText } from '../platform/fillText'
import { formatValue } from '../platform/formatValue'
import { StatusTag } from '../platform/StatusTag'
import { useStudioConnection } from '../platform/studioConnection'
import { type AuthoringRequest, fetchAuthoringRequest, ServerUnreachableError } from '../platform/studio'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { useFollowJob } from '../platform/useFollowJob'
import { PlatformContext } from './context'
import { authoringRequestStates, endedAuthoringRequestStates, requestName } from './studioAuthoringRequest'
import { StudioPageHeading } from './StudioPageHeading'

/**
 * One authoring request, from its name to its state. The heading shows the name, the state as a status tag and the words
 * in full, then the sender's line. Until the request waits for its author or ends, the page asks for it every second and
 * keeps the last answer when a read fails. A request that does not exist shows its line and, when `onAskAi` is given, an
 * Ask AI link that opens the request panel. See docs/reference/ai-authoring-in-studio.md#the-heading.
 */
export function StudioAuthoringRequestPage({
  application,
  requestId,
  userId,
  onAskAi,
}: {
  application: string
  requestId: string
  userId: string | null
  onAskAi?: () => void
}) {
  const t = useText()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const { colour, spacing } = useDesignTokens()
  const { generation, lost } = useStudioConnection()
  const [loaded, setLoaded] = useState<{ requestId: string; value: AuthoringRequest | null }>()

  useFollowJob(`${application}/${requestId}/${generation}`, {
    read: (signal) => fetchAuthoringRequest(application, requestId, signal),
    ends: (answer) => answer === null || endedAuthoringRequestStates.has(answer.state),
    interval: 1000,
    onAnswer: (value) => setLoaded({ requestId, value }),
    onEnd: (value) => setLoaded({ requestId, value }),
    onError: (error) => {
      if (error instanceof ServerUnreachableError) {
        lost()
        return 'stop'
      }
      console.warn(`Reading the authoring request '${requestId}' of '${application}' failed`, error)
      return 'retry'
    },
  })

  if (loaded?.requestId !== requestId) {
    return <Spin />
  }
  const request = loaded.value
  if (request === null) {
    return (
      <Flex vertical gap="small" data-testid="studio-ai-request-not-found">
        <Typography.Text>{t('shell.studio.aiRequest.notFound')}</Typography.Text>
        {onAskAi !== undefined && (
          <AxisButton role="link" onClick={onAskAi}>
            {t('shell.studio.aiRequest.askAi')}
          </AxisButton>
        )}
      </Flex>
    )
  }

  const state = authoringRequestStates[request.state]
  const time = formatValue({ name: 'sentAt', type: 'date-time' }, request.sentAt, {}, { locale, text: t })
  const sender = fillText(
    t(request.author === userId ? 'shell.studio.aiRequest.sentByYou' : 'shell.studio.aiRequest.sentBy'),
    { name: request.authorName ?? request.author, time },
  )
  return (
    <Flex vertical gap={spacing.xs} data-testid="studio-ai-request">
      <StudioPageHeading
        title={requestName(request.text)}
        tags={
          <StatusTag tone={state.tone} data-testid="studio-ai-request-state">
            {t(state.word)}
          </StatusTag>
        }
      />
      <Typography.Paragraph style={{ margin: 0, whiteSpace: 'pre-wrap' }}>{request.text}</Typography.Paragraph>
      <Typography.Text style={{ color: colour.textMuted }}>{sender}</Typography.Text>
    </Flex>
  )
}
