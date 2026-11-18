import { Flex, Modal, Spin, Typography } from 'antd'
import { useContext, useState } from 'react'
import { ActionBar } from '../platform/ActionBar'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisButton, type AxisFoldAction } from '../platform/AxisButton'
import { FilledText } from '../platform/FilledText'
import { fillText } from '../platform/fillText'
import { formatValue } from '../platform/formatValue'
import { ReasonDialog } from '../platform/ReasonDialog'
import { StatusTag } from '../platform/StatusTag'
import { useStudioConnection } from '../platform/studioConnection'
import {
  acceptAuthoringRequest,
  approveAuthoringRequest,
  type AuthoringRequest,
  type AuthoringRequestState,
  changeAuthoringRequestPlan,
  dropAuthoringRequest,
  fetchAuthoringRequest,
  fetchStudioRequest,
  resourceTitle,
  ServerUnreachableError,
  type StudioDiagnostic,
  type StudioRequest,
  type StudioResourceItem,
} from '../platform/studio'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { useFollowJob } from '../platform/useFollowJob'
import { PlatformContext } from './context'
import {
  authoringRequestStates,
  endedAuthoringRequestStates,
  requestName,
  requestResult,
} from './studioAuthoringRequest'
import { StudioAuthoringRequestResult } from './StudioAuthoringRequestResult'
import { StudioPageHeading } from './StudioPageHeading'
import { StudioRequestStatus } from './StudioRequestStatus'

const workingStates: ReadonlySet<AuthoringRequestState> = new Set<AuthoringRequestState>([
  'working',
  'fixing',
  'checking',
  'reviewing',
])

/**
 * The code of an accept's diagnostic that says a resource changed since AI read it, see
 * docs/reference/ai-authoring-in-studio.md#accepting.
 */
const acceptConflict = 'AXC0377'

/**
 * The words of the guide bar for a state, or `null` while the bar is not built for it yet, see
 * docs/reference/ai-authoring-in-studio.md#the-guide. `accepting` is true while the author's accept is queued or running,
 * or has succeeded and the request still reads ready. `conflicted` is true when the accept the page follows met a conflict.
 */
function guideWords(
  state: AuthoringRequestState,
  isAuthor: boolean,
  accepting: boolean,
  conflicted: boolean,
): string | null {
  if (state === 'planning') return 'shell.studio.aiRequest.guide.planning'
  if (state === 'awaitingApproval') {
    return isAuthor
      ? 'shell.studio.aiRequest.guide.awaitingApproval'
      : 'shell.studio.aiRequest.guide.awaitingApprovalOther'
  }
  if (workingStates.has(state)) return 'shell.studio.aiRequest.guide.working'
  if (state === 'ready') {
    if (accepting) return 'shell.studio.aiRequest.guide.accepting'
    if (!isAuthor) return 'shell.studio.aiRequest.guide.readyOther'
    return conflicted ? 'shell.studio.aiRequest.guide.conflict' : 'shell.studio.aiRequest.guide.ready'
  }
  if (state === 'accepted') return 'shell.studio.aiRequest.guide.accepted'
  if (state === 'dropped') return 'shell.studio.aiRequest.guide.dropped'
  return null
}

/**
 * One authoring request, from its name to its state. The heading shows the name, the state as a status tag and the words
 * in full, then the sender's line. Under it come the plan, then the failure alert of a failed request and the Result part
 * of a ready, failed or accepted request, then the status of the accept that the page follows, and the guide bar at the
 * foot of the window, which gives the request's author Approve plan, Change the plan… and Drop…, and Accept and Drop… on
 * a ready request. Accept sends the accept route and follows its publish request with `fetchStudioRequest`, kept in
 * memory for this visit, and reads the request again when the accept ends. An accept that met conflicts shows a warning
 * in place of its status, which names each changed resource with who changed it and when, and the bar offers Drop… only.
 * Until the request waits for its author or ends, the page asks for it every second and keeps the last answer when a read
 * fails. A request that does not exist shows its line and, when `onAskAi` is given, an Ask AI link that opens the request
 * panel. `resources` is the application's resource list, which names the resources of a conflict. See
 * docs/reference/ai-authoring-in-studio.md#the-heading, #the-plan, #the-diff, #accepting and #the-guide.
 */
export function StudioAuthoringRequestPage({
  application,
  requestId,
  userId,
  onAskAi,
  resources = [],
}: {
  application: string
  requestId: string
  userId: string | null
  onAskAi?: () => void
  resources?: StudioResourceItem[]
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
  // The accept that the author sent in this visit, which the page follows to its end. It is never read back from the server.
  const [accept, setAccept] = useState<{ requestId: string; value: StudioRequest }>()
  const followed = accept?.requestId === requestId ? accept.value : null
  const pending = followed !== null && (followed.status === 'queued' || followed.status === 'running')

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

  useFollowJob(pending ? `${application}/${followed.id}/${generation}` : null, {
    read: (signal) => fetchStudioRequest(application, followed!.id, signal),
    ends: (answer) => answer.status !== 'queued' && answer.status !== 'running',
    interval: 1000,
    waitFirst: true,
    onAnswer: (value) => setAccept({ requestId, value }),
    onEnd: (value) => {
      setAccept({ requestId, value })
      setRound((current) => current + 1)
    },
    onError: (error) => {
      if (error instanceof ServerUnreachableError) {
        lost()
        return 'stop'
      }
      console.warn(`Reading the accept '${followed?.id}' of the authoring request '${requestId}' failed`, error)
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
  const accepting =
    isAuthor && request.state === 'ready' && followed !== null && (pending || followed.status === 'succeeded')
  // The conflicts of the accept the page follows, which only the author sees. The bar then offers no Accept until the
  // retry route exists, see docs/reference/ai-authoring-in-studio.md#accepting.
  const conflicts =
    isAuthor && request.state === 'ready' && followed?.status === 'failed'
      ? followed.diagnostics.filter((diagnostic) => diagnostic.code === acceptConflict)
      : []
  const conflicted = conflicts.length > 0
  const words = guideWords(request.state, isAuthor, accepting, conflicted)
  const result = requestResult(request, isAuthor)
  const showResult =
    request.state === 'accepted' ||
    ((request.state === 'ready' || request.state === 'failed') && result.checks !== null)
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
  /** Runs an action of the request's author and keeps its answer. A failure shows the page's alert above the plan. */
  const act = async <T,>(action: () => Promise<T>, then: (answer: T) => void) => {
    setActionError(false)
    setActing(true)
    try {
      then(await action())
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
    (request.state === 'planning' ||
      request.state === 'awaitingApproval' ||
      workingStates.has(request.state) ||
      (request.state === 'ready' && !accepting))
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
      {result.failureKey !== null && (
        <AxisAlert
          kind="error"
          data-testid="studio-ai-request-failure"
          message={t('shell.studio.aiRequest.failure')}
          description={t(result.failureKey)}
        />
      )}
      {showResult && <StudioAuthoringRequestResult application={application} request={request} isAuthor={isAuthor} />}
      {followed !== null &&
        (conflicted ? (
          <AcceptConflict conflicts={conflicts} resources={resources} />
        ) : (
          <StudioRequestStatus request={followed} releaseNumber={null} />
        ))}
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
                onClick={() => void act(() => approveAuthoringRequest(application, requestId), adopt)}
              >
                {t('shell.studio.aiRequest.approve')}
              </AxisButton>
            ) : isAuthor && request.state === 'ready' && !accepting && !conflicted ? (
              <AxisButton
                role="finish"
                data-testid="studio-ai-request-accept"
                disabled={acting}
                onClick={() =>
                  void act(
                    () => acceptAuthoringRequest(application, requestId),
                    (sent) => setAccept({ requestId, value: sent }),
                  )
                }
              >
                {t('shell.studio.aiRequest.accept')}
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
          void act(() => dropAuthoringRequest(application, requestId), adopt)
        }}
        onCancel={() => setDropOpen(false)}
      >
        {t('shell.studio.aiRequest.drop.body')}
      </Modal>
    </Flex>
  )
}

/**
 * The warning that stands in place of "Not accepted" when the accept the page follows met conflicts, see
 * docs/reference/ai-authoring-in-studio.md#accepting. Each conflict is one bullet: the resource's title, then who changed
 * it and when, or only the title when no change is found. A resource the list does not hold yet is named by its file.
 */
function AcceptConflict({ conflicts, resources }: { conflicts: StudioDiagnostic[]; resources: StudioResourceItem[] }) {
  const t = useText()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  return (
    <AxisAlert
      kind="warning"
      data-testid="studio-ai-request-conflict"
      message={t('shell.studio.aiRequest.conflict')}
      description={
        <ul style={{ margin: 0 }}>
          {conflicts.map((conflict) => {
            const resource = resources.find((item) => item.id === conflict.resourceId)
            const name =
              resource !== undefined ? resourceTitle(resource, locale, t) : conflict.file || conflict.resourceId || ''
            const time = formatValue(
              { name: 'changedAt', type: 'date-time' },
              conflict.changedAt ?? null,
              {},
              { locale, text: t },
            )
            return (
              <li key={`${conflict.resourceId ?? ''}|${conflict.file}`} data-testid="studio-ai-request-conflict-item">
                {conflict.changedBy ? (
                  <FilledText
                    text={t('shell.studio.aiRequest.conflict.item')}
                    parts={{
                      resource: name,
                      author: conflict.changedBy,
                      time: <time dateTime={conflict.changedAt ?? undefined}>{time}</time>,
                    }}
                  />
                ) : (
                  name
                )}
              </li>
            )
          })}
        </ul>
      }
    />
  )
}
