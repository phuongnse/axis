import { Flex, Modal, Spin, Typography } from 'antd'
import { useContext, useState } from 'react'
import { ActionBar } from '../platform/ActionBar'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisButton, type AxisFoldAction } from '../platform/AxisButton'
import { fillText } from '../platform/fillText'
import { formatValue } from '../platform/formatValue'
import { ReasonDialog } from '../platform/ReasonDialog'
import { StatusTag } from '../platform/StatusTag'
import { useStudioConnection } from '../platform/studioConnection'
import {
  approveAuthoringRequest,
  type AuthoringRequest,
  type AuthoringRequestState,
  changeAuthoringRequestPlan,
  dropAuthoringRequest,
  fetchAuthoringRequest,
  ServerUnreachableError,
} from '../platform/studio'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { useFollowJob } from '../platform/useFollowJob'
import { PlatformContext } from './context'
import { authoringRequestStates, endedAuthoringRequestStates, requestName } from './studioAuthoringRequest'
import { StudioPageHeading } from './StudioPageHeading'

const workingStates: ReadonlySet<AuthoringRequestState> = new Set<AuthoringRequestState>([
  'working',
  'fixing',
  'checking',
  'reviewing',
])

/**
 * The words of the guide bar for a state, or `null` while the bar is not built for it yet, see
 * docs/reference/ai-authoring-in-studio.md#the-guide.
 */
function guideWords(state: AuthoringRequestState, isAuthor: boolean): string | null {
  if (state === 'planning') return 'shell.studio.aiRequest.guide.planning'
  if (state === 'awaitingApproval') {
    return isAuthor
      ? 'shell.studio.aiRequest.guide.awaitingApproval'
      : 'shell.studio.aiRequest.guide.awaitingApprovalOther'
  }
  if (workingStates.has(state)) return 'shell.studio.aiRequest.guide.working'
  if (state === 'accepted') return 'shell.studio.aiRequest.guide.accepted'
  if (state === 'dropped') return 'shell.studio.aiRequest.guide.dropped'
  return null
}

/**
 * One authoring request, from its name to its state. The heading shows the name, the state as a status tag and the words
 * in full, then the sender's line. Under it come the plan and the guide bar at the foot of the window, which gives the
 * request's author Approve plan, Change the plan… and Drop…. Until the request waits for its author or ends, the page
 * asks for it every second and keeps the last answer when a read fails. A request that does not exist shows its line
 * and, when `onAskAi` is given, an Ask AI link that opens the request panel. See
 * docs/reference/ai-authoring-in-studio.md#the-heading, #the-plan and #the-guide.
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
  const { colour, size, spacing } = useDesignTokens()
  const { generation, lost } = useStudioConnection()
  const [loaded, setLoaded] = useState<{ requestId: string; value: AuthoringRequest | null }>()
  // Counts the actions that changed the request, so the page follows the request again after each one.
  const [round, setRound] = useState(0)
  const [actionError, setActionError] = useState(false)
  const [changePlanOpen, setChangePlanOpen] = useState(false)
  const [dropOpen, setDropOpen] = useState(false)
  const [acting, setActing] = useState(false)

  useFollowJob(`${application}/${requestId}/${generation}/${round}`, {
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
  const isAuthor = request.author === userId
  const words = guideWords(request.state, isAuthor)
  const time = formatValue({ name: 'sentAt', type: 'date-time' }, request.sentAt, {}, { locale, text: t })
  const sender = fillText(
    t(request.author === userId ? 'shell.studio.aiRequest.sentByYou' : 'shell.studio.aiRequest.sentBy'),
    { name: request.authorName ?? request.author, time },
  )

  /** Keeps the request that an action answered and follows it again. */
  const adopt = (answered: AuthoringRequest) => {
    setLoaded({ requestId, value: answered })
    setRound((current) => current + 1)
  }
  /** Runs an action of the request's author. A failure shows the page's alert above the plan. */
  const act = async (action: () => Promise<AuthoringRequest>) => {
    setActionError(false)
    setActing(true)
    try {
      adopt(await action())
    } catch (failure) {
      console.warn(`An action on the authoring request '${requestId}' of '${application}' failed`, failure)
      setActionError(true)
    } finally {
      setActing(false)
    }
  }
  const fold: AxisFoldAction[] = []
  if (isAuthor && request.state === 'awaitingApproval') {
    fold.push({
      key: 'changePlan',
      labelKey: 'shell.studio.aiRequest.changePlan',
      disabled: acting,
      onClick: () => setChangePlanOpen(true),
    })
  }
  if (
    isAuthor &&
    (request.state === 'planning' || request.state === 'awaitingApproval' || workingStates.has(request.state))
  ) {
    fold.push({
      key: 'drop',
      labelKey: 'shell.studio.aiRequest.drop',
      disabled: acting,
      onClick: () => setDropOpen(true),
    })
  }

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
      {actionError && (
        <AxisAlert data-testid="studio-ai-request-error" kind="error" message={t('shell.studio.aiRequest.failed')} />
      )}
      {(request.state === 'planning' || request.plan !== null) && (
        <Flex vertical gap={spacing.xs} data-testid="studio-ai-request-plan">
          <Typography.Text strong>{t('shell.studio.aiRequest.plan')}</Typography.Text>
          {request.state === 'planning' ? (
            <Flex gap="small" align="center">
              <Spin size="small" />
              <Typography.Text>{t('shell.studio.aiRequest.plan.writing')}</Typography.Text>
            </Flex>
          ) : (
            <Typography.Paragraph style={{ margin: 0, whiteSpace: 'pre-wrap' }}>{request.plan}</Typography.Paragraph>
          )}
        </Flex>
      )}
      {words !== null && (
        <ActionBar
          docked
          data-testid="studio-ai-request-guide"
          start={<span>{t(words)}</span>}
          fold={fold}
          end={
            isAuthor && request.state === 'awaitingApproval' ? (
              <AxisButton
                role="finish"
                data-testid="studio-ai-request-approve"
                disabled={acting}
                onClick={() => void act(() => approveAuthoringRequest(application, requestId))}
              >
                {t('shell.studio.aiRequest.approve')}
              </AxisButton>
            ) : undefined
          }
        />
      )}
      {changePlanOpen && (
        <ReasonDialog
          size="medium"
          title={t('shell.studio.aiRequest.changePlan.title')}
          label={t('shell.studio.aiRequest.changePlan.label')}
          help=""
          okText={t('shell.studio.aiRequest.changePlan.send')}
          cancelText={t('shell.studio.cancel')}
          requiredMessage={t('shell.studio.aiRequest.changePlan.required')}
          tooLongMessage={t('shell.studio.aiRequest.changePlan.tooLong')}
          maxLength={2000}
          failedText={t('shell.studio.aiRequest.failed')}
          onSend={async (comment) => {
            adopt(await changeAuthoringRequestPlan(application, requestId, comment))
            return { ok: true }
          }}
          onClose={() => setChangePlanOpen(false)}
          onChanged={() => setActionError(false)}
        />
      )}
      <Modal
        open={dropOpen}
        width={size.dialog.small}
        title={t('shell.studio.aiRequest.drop.title')}
        okText={t('shell.studio.aiRequest.drop.confirm')}
        okButtonProps={{ danger: true }}
        cancelText={t('shell.studio.cancel')}
        onOk={() => {
          setDropOpen(false)
          void act(() => dropAuthoringRequest(application, requestId))
        }}
        onCancel={() => setDropOpen(false)}
      >
        {t('shell.studio.aiRequest.drop.body')}
      </Modal>
    </Flex>
  )
}
