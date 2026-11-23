import { Flex, Modal, Spin, Typography } from 'antd'
import { type ReactNode, useContext, useState } from 'react'
import { ActionBar } from '../platform/ActionBar'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisButton, type AxisFoldAction } from '../platform/AxisButton'
import { DialogFooter } from '../platform/DialogFooter'
import { FilledText } from '../platform/FilledText'
import { fillText } from '../platform/fillText'
import { PersonName } from '../platform/PersonName'
import { formatDuration, formatValue } from '../platform/formatValue'
import { PageLayout } from '../platform/PageLayout'
import { ReasonDialog } from '../platform/ReasonDialog'
import { ShellBarSlot } from '../platform/shellBarSlot'
import { StatusTag } from '../platform/StatusTag'
import { StepsBar, type StepsBarItem } from '../platform/StepsBar'
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
  type FeedEntry,
  endedAuthoringRequestStates,
  planChanges,
  planSummary,
  requestFeed,
  requestName,
  requestResult,
  workLine,
} from './studioAuthoringRequest'
import { StudioAuthoringRequestResult } from './StudioAuthoringRequestResult'
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

/** The title of each item of the feed. */
const feedTitles: Record<FeedEntry['key'], string> = {
  plan: 'shell.studio.aiRequest.plan',
  work: 'shell.studio.aiRequest.work',
  result: 'shell.studio.aiRequest.result',
  dropped: 'shell.studio.aiRequest.dropped',
}

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
 * in full, then the sender's line and the alert of an action that failed. Under them comes the feed, one `StepsBar` of
 * the Plan, Work, Result and Dropped items that the request has, each with its mark. A failed request shows its failure
 * in the item where it stopped, and the Result item of a ready, failed or accepted request ends with the status of the
 * accept that the page follows. The guide bar at the foot of the window gives the request's author Approve plan, Change the plan… and Drop…, and Accept and Drop… on
 * a ready request. Accept sends the accept route and follows its publish request with `fetchStudioRequest`, kept in
 * memory for this visit, and reads the request again when the accept ends. An accept that met conflicts shows a warning
 * in place of its status, which names each changed resource with who changed it and when, and the bar offers Drop… only.
 * Until the request waits for its author or ends, the page asks for it every second and keeps the last answer when a read
 * fails. A request that does not exist shows its line and, when `onAskAi` is given, an Ask AI link that opens the request
 * panel. `resources` is the application's resource list, which names the resources of a conflict. See
 * docs/reference/ai-authoring-in-studio.md#the-heading, #the-feed, #the-work-item, #the-plan, #the-diff, #accepting and
 * #the-guide.
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
          <AxisButton role="secondary" onClick={onAskAi}>
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
  const time = formatValue({ name: 'sentAt', type: 'date-time' }, request.sentAt, {}, { locale, text: t })
  const sender = (
    <FilledText
      text={t('shell.studio.aiRequest.sentBy')}
      parts={{ name: <PersonName id={request.author} name={request.authorName ?? request.author} />, time }}
    />
  )

  const feed = requestFeed(request)
  const failure =
    result.failureKey === null ? null : (
      <AxisAlert
        kind="error"
        data-testid="studio-ai-request-failure"
        message={fillText(t('shell.studio.aiRequest.failure.line'), { reason: t(result.failureKey) })}
      />
    )
  const formatTime = (name: string, value: string | null) =>
    formatValue({ name, type: 'date-time' }, value, {}, { locale, text: t })
  /** The mark, standing and body of one item of the feed. */
  const feedItem = ({ key, mark }: FeedEntry): StepsBarItem => {
    if (key === 'plan') {
      return {
        mark,
        standing:
          request.approvedAt === null
            ? undefined
            : fillText(t('shell.studio.aiRequest.plan.approved'), {
                time: formatTime('approvedAt', request.approvedAt),
              }),
        body: <PlanBody key={request.id} request={request} failure={mark === 'stopped' ? failure : null} />,
      }
    }
    if (key === 'work') {
      const line = workLine(request)
      return {
        mark,
        standing:
          request.state === 'fixing'
            ? fillText(t('shell.studio.aiRequest.work.fixRound'), { round: request.round })
            : undefined,
        body: (
          <Flex vertical gap={spacing.xs}>
            {mark === 'stopped' && failure}
            {line !== null && (
              <Typography.Text>
                {'key' in line
                  ? t(line.key)
                  : fillText(
                      t(line.count === 1 ? 'shell.studio.aiRequest.work.doneOne' : 'shell.studio.aiRequest.work.done'),
                      {
                        count: line.count,
                        duration: formatDuration(line.ms, locale, t),
                      },
                    )}
              </Typography.Text>
            )}
          </Flex>
        ),
      }
    }
    if (key === 'result') {
      return {
        mark,
        body: (
          <Flex vertical gap={spacing.sm}>
            <StudioAuthoringRequestResult application={application} request={request} isAuthor={isAuthor} />
            {followed !== null &&
              (conflicted ? (
                <AcceptConflict conflicts={conflicts} resources={resources} />
              ) : (
                <StudioRequestStatus request={followed} releaseNumber={null} />
              ))}
          </Flex>
        ),
      }
    }
    return {
      mark,
      standing: request.finishedAt === null ? undefined : formatTime('finishedAt', request.finishedAt),
    }
  }

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
    <div data-testid="studio-ai-request">
      <PageLayout
        title={requestName(request.text)}
        tags={
          <StatusTag tone={state.tone} data-testid="studio-ai-request-state">
            {t(state.word)}
          </StatusTag>
        }
      >
        <Typography.Paragraph style={{ margin: 0, whiteSpace: 'pre-wrap' }}>{request.text}</Typography.Paragraph>
        <Typography.Text style={{ color: colour.textMuted }}>{sender}</Typography.Text>
        {actionError && (
          <AxisAlert data-testid="studio-ai-request-error" kind="error" message={t('shell.studio.aiRequest.failed')} />
        )}
        <div data-testid="studio-ai-request-feed">
          <StepsBar vertical titles={feed.map((entry) => t(feedTitles[entry.key]))} items={feed.map(feedItem)} />
        </div>
        {words !== null && (
          <ShellBarSlot>
            <ActionBar
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
          </ShellBarSlot>
        )}
        {changePlanOpen && (
          <ReasonDialog
            size="medium"
            title={t('shell.studio.aiRequest.changePlan.title')}
            label={t('shell.studio.aiRequest.changePlan.label')}
            help=""
            finishText={t('shell.studio.aiRequest.changePlan.send')}
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
          footer={
            <DialogFooter
              destructive
              finishText={t('shell.studio.aiRequest.drop.confirm')}
              cancelText={t('shell.studio.cancel')}
              onFinish={() => {
                setDropOpen(false)
                void act(() => dropAuthoringRequest(application, requestId), adopt)
              }}
              onCancel={() => setDropOpen(false)}
            />
          }
          onCancel={() => setDropOpen(false)}
        >
          {t('shell.studio.aiRequest.drop.body')}
        </Modal>
      </PageLayout>
    </div>
  )
}

/**
 * The body of the Plan item, see docs/reference/ai-authoring-in-studio.md#the-plan: the failure of a request that stopped
 * here, the comments that asked for another plan, oldest first, then what AI is doing while it writes the plan, or the
 * plan's summary with Show the plan or Hide the plan after it. The rest of the plan is open while the plan waits for its
 * author and folded in any other state, until the reader folds it.
 */
function PlanBody({ request, failure }: { request: AuthoringRequest; failure: ReactNode }) {
  const t = useText()
  const { spacing } = useDesignTokens()
  const [toggled, setToggled] = useState<boolean>()
  const open = toggled ?? request.state === 'awaitingApproval'
  const { summary, rest } = planSummary(request.plan ?? '')
  return (
    <Flex vertical gap={spacing.md} data-testid="studio-ai-request-plan">
      {failure}
      {planChanges(request).length > 0 && (
        <Flex vertical gap={spacing.xs}>
          {planChanges(request).map((comment, index) => (
            <Typography.Text key={index} style={{ whiteSpace: 'pre-wrap' }}>
              {fillText(t('shell.studio.aiRequest.plan.asked'), { comment })}
            </Typography.Text>
          ))}
        </Flex>
      )}
      {request.state === 'planning' ? (
        <Typography.Text>{t('shell.studio.aiRequest.plan.writing')}</Typography.Text>
      ) : (
        request.plan !== null && (
          <>
            <Flex wrap gap="small" align="center">
              <Typography.Text style={{ whiteSpace: 'pre-wrap' }}>{summary}</Typography.Text>
              {rest !== '' && (
                <AxisButton role="secondary" aria-expanded={open} onClick={() => setToggled(!open)}>
                  {t(open ? 'shell.studio.aiRequest.plan.hide' : 'shell.studio.aiRequest.plan.show')}
                </AxisButton>
              )}
            </Flex>
            {open && rest !== '' && (
              <Typography.Paragraph style={{ margin: 0, whiteSpace: 'pre-wrap' }}>{rest}</Typography.Paragraph>
            )}
          </>
        )
      )}
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
                      name,
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
