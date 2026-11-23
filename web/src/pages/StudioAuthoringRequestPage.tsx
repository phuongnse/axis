import { Flex, Modal, Spin, Typography } from 'antd'
import { type ReactNode, useContext, useState } from 'react'
import { ActionBar } from '../platform/ActionBar'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisButton, type AxisFoldAction } from '../platform/AxisButton'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
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
import { studioResourceAddress } from '../platform/studioAddress'
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
  requestAuthoringRequestChanges,
  retryAuthoringRequest,
  ServerUnreachableError,
  type StudioDiagnostic,
  type StudioRequest,
  type StudioResourceItem,
} from '../platform/studio'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { useFollowJob } from '../platform/useFollowJob'
import { PlatformContext } from './context'
import { ResourceReference } from './ResourceReference'
import {
  authoringRequestStates,
  type FeedEntry,
  type HistoryRow,
  endedAuthoringRequestStates,
  planChanges,
  requestFeed,
  requestHistory,
  requestResult,
  requestTitle,
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
 * `questions` is how many questions the plan has, which the author's words count while the plan waits.
 */
function guideWords(
  state: AuthoringRequestState,
  isAuthor: boolean,
  accepting: boolean,
  conflicted: boolean,
  questions: number,
): string | null {
  if (state === 'planning') return 'shell.studio.aiRequest.guide.planning'
  if (state === 'awaitingApproval') {
    if (!isAuthor) return 'shell.studio.aiRequest.guide.awaitingApprovalOther'
    if (questions === 0) return 'shell.studio.aiRequest.guide.awaitingApproval'
    return questions === 1 ? 'shell.studio.aiRequest.guide.questionsOne' : 'shell.studio.aiRequest.guide.questions'
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
 * accept that the page follows. The Work item's Show the steps folds open the request's History, a table of its steps. The
 * guide bar at the foot of the window gives the request's author Approve plan, Change the plan… and Drop…, or, while the plan
 * has questions, Answer… and Drop…, where Answer… opens a dialog for the author's answers that sends them as a change of
 * plan, and Accept,
 * Ask for other changes… and Drop… on a ready request, where Ask for other changes… opens a dialog for a comment that
 * sends the request back to work. Accept sends the accept route and follows its publish request with
 * `fetchStudioRequest`, kept in memory for this visit, and reads the request again when the accept ends. An accept that
 * met conflicts shows a warning in place of its status, which names each changed resource with who changed it and when,
 * and the bar offers Make the change again and Drop…. Make the change again sends the retry route, which clears the
 * draft and sends the request back to work, and the page forgets that accept.
 * Until the request waits for its author or ends, the page asks for it every second and keeps the last answer when a read
 * fails. A request that does not exist shows its line and, when `onAskAi` is given, an Ask AI link that opens the request
 * panel. `resources` is the application's resource list, which names the resources of a conflict and holds the sites that Preview the
 * draft links to on `previewHost`, see `StudioAuthoringRequestResult`. See
 * docs/reference/ai-authoring-in-studio.md#the-heading, #the-feed, #the-work-item, #the-plan, #the-plans-groups, #the-diff, #accepting and
 * #the-guide, #replies and #the-history.
 */
export function StudioAuthoringRequestPage({
  application,
  requestId,
  userId,
  onAskAi,
  resources = [],
  previewHost,
}: {
  application: string
  requestId: string
  userId: string | null
  onAskAi?: () => void
  resources?: StudioResourceItem[]
  previewHost: string | null
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
  const [answerOpen, setAnswerOpen] = useState(false)
  const [requestChangesOpen, setRequestChangesOpen] = useState(false)
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
  // The conflicts of the accept the page follows, which only the author sees. The bar then offers Make the change again
  // and Drop… and no Accept, see docs/reference/ai-authoring-in-studio.md#accepting.
  const conflicts =
    isAuthor && request.state === 'ready' && followed?.status === 'failed'
      ? followed.diagnostics.filter((diagnostic) => diagnostic.code === acceptConflict)
      : []
  const conflicted = conflicts.length > 0
  // While the plan has questions its author answers them in place of approving it, see
  // docs/reference/ai-authoring-in-studio.md#the-plans-groups.
  const questions = request.plan?.questions.length ?? 0
  const asking = isAuthor && request.state === 'awaitingApproval' && questions > 0
  const words = guideWords(request.state, isAuthor, accepting, conflicted, questions)
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
      return {
        mark,
        standing:
          request.state === 'fixing'
            ? fillText(t('shell.studio.aiRequest.work.fixRound'), { round: request.round })
            : undefined,
        body: <WorkBody key={request.id} request={request} failure={mark === 'stopped' ? failure : null} />,
      }
    }
    if (key === 'result') {
      return {
        mark,
        body: (
          <Flex vertical gap={spacing.sm}>
            <StudioAuthoringRequestResult
              application={application}
              request={request}
              isAuthor={isAuthor}
              previewHost={previewHost}
              sites={resources.filter((item) => item.kind === 'site')}
            />
            {followed !== null &&
              (conflicted ? (
                <AcceptConflict application={application} conflicts={conflicts} resources={resources} />
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
  if (isAuthor && request.state === 'awaitingApproval' && !asking) {
    fold.push({
      key: 'changePlan',
      labelKey: 'shell.studio.aiRequest.changePlan',
      disabled: acting,
      onClick: () => setChangePlanOpen(true),
    })
  }
  if (isAuthor && request.state === 'ready' && !accepting && !conflicted) {
    fold.push({
      key: 'requestChanges',
      labelKey: 'shell.studio.aiRequest.requestChanges',
      disabled: acting,
      onClick: () => setRequestChangesOpen(true),
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
        title={requestTitle(request)}
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
              start={<span>{fillText(t(words), { count: questions })}</span>}
              fold={fold}
              end={
                asking ? (
                  <AxisButton
                    role="finish"
                    data-testid="studio-ai-request-answer"
                    disabled={acting}
                    onClick={() => setAnswerOpen(true)}
                  >
                    {t('shell.studio.aiRequest.answer')}
                  </AxisButton>
                ) : isAuthor && request.state === 'awaitingApproval' ? (
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
                ) : isAuthor && request.state === 'ready' && conflicted ? (
                  <AxisButton
                    role="finish"
                    data-testid="studio-ai-request-retry"
                    disabled={acting}
                    onClick={() =>
                      void act(
                        () => retryAuthoringRequest(application, requestId),
                        (answered) => {
                          setAccept(undefined)
                          adopt(answered)
                        },
                      )
                    }
                  >
                    {t('shell.studio.aiRequest.retry')}
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
        {answerOpen && (
          <ReasonDialog
            size="medium"
            title={t('shell.studio.aiRequest.answer.title')}
            label={t('shell.studio.aiRequest.answer.label')}
            help={t('shell.studio.aiRequest.answer.help')}
            finishText={t('shell.studio.aiRequest.changePlan.send')}
            cancelText={t('shell.studio.cancel')}
            requiredMessage={t('shell.studio.aiRequest.answer.required')}
            tooLongMessage={t('shell.studio.aiRequest.changePlan.tooLong')}
            maxLength={2000}
            failedText={t('shell.studio.aiRequest.failed')}
            onSend={async (answers) => {
              adopt(await changeAuthoringRequestPlan(application, requestId, answers))
              return { ok: true }
            }}
            onClose={() => setAnswerOpen(false)}
            onChanged={() => setActionError(false)}
          />
        )}
        {requestChangesOpen && (
          <ReasonDialog
            size="medium"
            title={t('shell.studio.aiRequest.requestChanges.title')}
            label={t('shell.studio.aiRequest.requestChanges.label')}
            help={t('shell.studio.aiRequest.requestChanges.help')}
            finishText={t('shell.studio.aiRequest.requestChanges.send')}
            cancelText={t('shell.studio.cancel')}
            requiredMessage={t('shell.studio.aiRequest.requestChanges.required')}
            tooLongMessage={t('shell.studio.aiRequest.requestChanges.tooLong')}
            maxLength={2000}
            failedText={t('shell.studio.aiRequest.failed')}
            onSend={async (comment) => {
              adopt(await requestAuthoringRequestChanges(application, requestId, comment))
              return { ok: true }
            }}
            onClose={() => setRequestChangesOpen(false)}
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
 * The body of the Work item, see docs/reference/ai-authoring-in-studio.md#the-work-item: the failure of a request that stopped
 * here, its line, then Show the steps or Hide the steps once it has steps. That button folds in the History, closed at
 * first, a table of every step with no paging, newest first, see docs/reference/ai-authoring-in-studio.md#the-history.
 */
function WorkBody({ request, failure }: { request: AuthoringRequest; failure: ReactNode }) {
  const t = useText()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const { spacing } = useDesignTokens()
  const [open, setOpen] = useState<boolean>(false)
  const line = workLine(request)
  const hasSteps = (request.steps ?? []).length > 0
  const columns: AxisColumn<HistoryRow>[] = [
    {
      key: 'step',
      title: t('shell.studio.aiRequest.history.step'),
      kind: 'freeText',
      render: (_, row) => (row.step === null ? null : fillText(t(row.step.key), row.step.values)),
    },
    {
      key: 'result',
      title: t('shell.studio.aiRequest.history.result'),
      kind: 'freeText',
      render: (_, row) => (row.result === null ? null : fillText(t(row.result.key), row.result.values)),
    },
    {
      key: 'startedAt',
      title: t('shell.studio.aiRequest.history.started'),
      kind: 'fixed',
      render: (_, row) => formatValue({ name: 'startedAt', type: 'date-time' }, row.startedAt, {}, { locale, text: t }),
    },
    {
      key: 'duration',
      title: t('shell.studio.aiRequest.history.duration'),
      kind: 'fixed',
      render: (_, row) => (row.ms === null ? null : formatDuration(row.ms, locale, t)),
    },
  ]
  return (
    <Flex vertical gap={spacing.xs}>
      {failure}
      <Flex wrap gap="small" align="center">
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
        {hasSteps && (
          <AxisButton
            role="secondary"
            aria-expanded={open}
            data-testid="studio-ai-request-steps-toggle"
            onClick={() => setOpen(!open)}
          >
            {t(open ? 'shell.studio.aiRequest.history.hide' : 'shell.studio.aiRequest.history.show')}
          </AxisButton>
        )}
      </Flex>
      {open && (
        <AxisTable<HistoryRow>
          testId="studio-ai-request-history"
          pagination={false}
          rowKey={(row) => row.key}
          dataSource={requestHistory(request)}
          columns={columns}
        />
      )}
    </Flex>
  )
}

/**
 * The body of the Plan item, see docs/reference/ai-authoring-in-studio.md#the-plan: the failure of a request that stopped
 * here, the comments that asked for another plan, oldest first, then what AI is doing while it writes the plan, or the
 * plan's summary with Show the plan or Hide the plan after it. The rest of the plan is open while the plan waits for its
 * author and folded in any other state, until the reader folds it. Open, it shows the plan's groups in the order of
 * docs/reference/ai-authoring-in-studio.md#the-plans-groups, each only when it has something: the questions with their
 * options and the recommended one marked, what changes, the decisions, the risks, the acceptance criteria, the checklist
 * and the implementation notes, which stay behind Show the implementation notes until the reader opens them.
 */
function PlanBody({ request, failure }: { request: AuthoringRequest; failure: ReactNode }) {
  const t = useText()
  const { spacing } = useDesignTokens()
  const [toggled, setToggled] = useState<boolean>()
  const open = toggled ?? request.state === 'awaitingApproval'
  const summary = request.plan?.summary ?? ''
  const [notesOpen, setNotesOpen] = useState(false)
  const plan = request.plan
  const changes = plan?.changes ?? []
  const group = (key: string, heading: string, content: ReactNode) => (
    <Flex vertical gap={spacing.xs} key={key} data-testid="studio-ai-request-plan-group">
      <Typography.Text strong>{t(heading)}</Typography.Text>
      {content}
    </Flex>
  )
  const list = (items: ReactNode[]) => (
    <ul style={{ margin: 0 }}>
      {items.map((item, index) => (
        <li key={index}>{item}</li>
      ))}
    </ul>
  )
  const lines = (first: string, second: string) => (
    <>
      {first}
      <br />
      {second}
    </>
  )
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
              {changes.length > 0 && (
                <AxisButton role="secondary" aria-expanded={open} onClick={() => setToggled(!open)}>
                  {t(open ? 'shell.studio.aiRequest.plan.hide' : 'shell.studio.aiRequest.plan.show')}
                </AxisButton>
              )}
            </Flex>
            {open && plan !== null && changes.length > 0 && (
              <Flex vertical gap={spacing.md}>
                {plan.questions.length > 0 &&
                  group(
                    'questions',
                    'shell.studio.aiRequest.plan.questions',
                    <ul style={{ margin: 0 }}>
                      {plan.questions.map((question, index) => (
                        <li key={index}>
                          {question.question}
                          <ul>
                            {question.options.map((option, position) => (
                              <li key={position}>
                                {question.recommended === position + 1
                                  ? fillText(t('shell.studio.aiRequest.plan.recommended'), { option })
                                  : option}
                              </li>
                            ))}
                          </ul>
                        </li>
                      ))}
                    </ul>,
                  )}
                {group('changes', 'shell.studio.aiRequest.plan.changes', list(changes))}
                {plan.decisions.length > 0 &&
                  group(
                    'decisions',
                    'shell.studio.aiRequest.plan.decisions',
                    list(plan.decisions.map((entry) => lines(entry.decision, entry.reason))),
                  )}
                {plan.risks.length > 0 && group('risks', 'shell.studio.aiRequest.plan.risks', list(plan.risks))}
                {plan.checks.length > 0 && group('checks', 'shell.studio.aiRequest.plan.checks', list(plan.checks))}
                {plan.checklist.length > 0 &&
                  group(
                    'checklist',
                    'shell.studio.aiRequest.plan.checklist',
                    list(plan.checklist.map((entry) => lines(entry.item, entry.how))),
                  )}
                {plan.details.length > 0 &&
                  group(
                    'notes',
                    'shell.studio.aiRequest.plan.notes',
                    <>
                      <div>
                        <AxisButton role="secondary" aria-expanded={notesOpen} onClick={() => setNotesOpen(!notesOpen)}>
                          {t(
                            notesOpen
                              ? 'shell.studio.aiRequest.plan.notes.hide'
                              : 'shell.studio.aiRequest.plan.notes.show',
                          )}
                        </AxisButton>
                      </div>
                      {notesOpen && list(plan.details)}
                    </>,
                  )}
              </Flex>
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
 * it and when, or only the title when no change is found. The title is a `ResourceReference` link to the resource in
 * Studio, and a resource the list does not hold yet is named by its file, as plain muted text.
 */
function AcceptConflict({
  application,
  conflicts,
  resources,
}: {
  application: string
  conflicts: StudioDiagnostic[]
  resources: StudioResourceItem[]
}) {
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
            const name = (
              <ResourceReference
                file={conflict.file || conflict.resourceId || ''}
                resource={resource}
                href={
                  resource !== undefined && conflict.resourceId
                    ? studioResourceAddress(application, conflict.resourceId)
                    : undefined
                }
              />
            )
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
