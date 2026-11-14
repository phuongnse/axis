import { Flex, Form, Input, Modal, Spin, Tabs, Typography } from 'antd'
import { useContext, useEffect, useState, type HTMLAttributes, type ReactNode } from 'react'
import { Link } from 'react-router'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisActionRow, AxisButton } from '../platform/AxisButton'
import { AxisField } from '../platform/AxisField'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
import { EmptyDash } from '../platform/EmptyValue'
import { requiredMark } from '../platform/requiredMark'
import {
  acceptAuthoringRequest,
  actOnAuthoringRequest,
  fetchAuthoringRequest,
  fetchAuthoringRequests,
  fetchDraftDiff,
  fetchStudioRequest,
  ServerUnreachableError,
  type StudioAuthoringRequest,
  type StudioAuthoringRequestDetail,
  type StudioAuthoringRequestState,
  type StudioAuthoringRequestStep,
  type StudioReviewFinding,
  type StudioRequest,
  type StudioResourceItem,
  type StudioRevisionChange,
} from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { StepsBar } from '../platform/StepsBar'
import { useFollowJob } from '../platform/useFollowJob'
import { PlatformContext } from './context'
import {
  authoringRequestAddress,
  requestStateTone,
  requestTitle,
  settled,
  useRequestStateWords,
} from './authoringRequests'
import { StatusTag } from '../platform/StatusTag'
import { StudioChanges } from './StudioHistory'
import { kindSingular } from '../platform/studioKinds'
import { SectionPart, SectionParts } from './ResourceFormSection'
import { ActionBar } from './SaveBar'
import { StudioPageHeading } from './StudioPageHeading'
import { StudioDiagnosticRow } from './StudioProblems'
import { StudioRequestStatus } from './StudioRequestStatus'
import { StudioTestResults } from './StudioTestResults'

/** Proposed: the stages of a request's loop, which a `StepsBar` shows on its page. */
const stageKeys = ['plan', 'approval', 'change', 'checks', 'review', 'ready'] as const

/**
 * The stage a state is in: fixing is AI working again, and an accepted request has finished every stage. A dropped
 * or failed request shows no stages.
 */
function stageOf(state: StudioAuthoringRequestState): number | null {
  switch (state) {
    case 'planning':
      return 0
    case 'awaitingApproval':
      return 1
    case 'working':
    case 'fixing':
      return 2
    case 'checking':
      return 3
    case 'reviewing':
      return 4
    case 'ready':
      return 5
    case 'accepted':
      return stageKeys.length
    default:
      return null
  }
}

/** A request's sending time in the UI locale, to the minute. */
function useSentTime(): (time: string) => string {
  const locale = useContext(PlatformContext)?.current.locale
  const format = new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short' })
  return (time) => format.format(new Date(time))
}

/**
 * Proposed: the AI requests of the open application, a page of the sidebar beside History. One row for each request,
 * newest first: its words, which open its page, its state, who sent it and when. A state that waits for the person
 * reading is in bold.
 */
export function StudioAuthoringRequests({ application, userId }: { application: string; userId: string | null }) {
  const t = useText()
  const sentTime = useSentTime()
  const stateWords = useRequestStateWords()
  const { generation, lost } = useStudioConnection()
  const [requests, setRequests] = useState<StudioAuthoringRequest[] | 'failed'>()

  useEffect(() => {
    const controller = new AbortController()
    fetchAuthoringRequests(application, controller.signal).then(
      (list) => setRequests(list.items),
      (error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        if (error instanceof ServerUnreachableError) {
          lost()
          return
        }
        setRequests('failed')
      },
    )
    return () => controller.abort()
  }, [application, generation, lost])

  const columns: AxisColumn<StudioAuthoringRequest>[] = [
    {
      key: 'request',
      kind: 'freeText',
      title: t('shell.studio.aiRequests.request'),
      render: (_, row) => (
        <Link
          to={authoringRequestAddress(application, row.id)}
          style={{ display: '-webkit-box', WebkitLineClamp: 2, WebkitBoxOrient: 'vertical', overflow: 'hidden' }}
        >
          {requestTitle(row)}
        </Link>
      ),
    },
    {
      key: 'state',
      // A status column is as wide as its tag, see docs/reference/actions-and-controls.md#tables.
      kind: 'fixed',
      title: t('shell.studio.aiRequests.state'),
      render: (_, row) => <StatusTag tone={requestStateTone(row.state)}>{stateWords(row)}</StatusTag>,
    },
    {
      key: 'who',
      kind: 'freeText',
      title: t('shell.studio.aiRequests.who'),
      render: (_, row) =>
        row.author === userId ? t('shell.studio.aiRequests.you') : (row.authorName ?? t('shell.studio.ai.someone')),
    },
    {
      key: 'when',
      kind: 'fixed',
      title: t('shell.studio.aiRequests.when'),
      render: (_, row) => sentTime(row.sentAt),
    },
  ]

  return (
    <Flex vertical gap="middle">
      <StudioPageHeading title={t('shell.studio.aiRequests')} />
      <AxisTable<StudioAuthoringRequest>
        columns={columns}
        rowKey={(row) => row.id}
        dataSource={Array.isArray(requests) ? requests : []}
        loading={requests === undefined}
        emptyText={t('shell.studio.aiRequests.empty')}
        pagination={false}
        toolbar={{}}
        failure={
          requests === 'failed' ? <AxisAlert kind="error" message={t('shell.studio.ai.listFailed')} /> : undefined
        }
        rowAttributes={(row) =>
          ({ 'data-testid': 'studio-ai-request-row', 'data-id': row.id }) as HTMLAttributes<HTMLElement>
        }
      />
    </Flex>
  )
}

/**
 * Proposed: the page of one AI request, where Send leads. Its heading says who sent it when, then come its words and
 * its state. While AI writes the plan, a spinner says so, and the page follows the request until it waits for its
 * author or ends. In `awaitingApproval` the plan shows in plain words, one line a bullet, and its author approves it, asks for another
 * with a comment or drops the request; anyone else reads that it waits for its author. A `ready` or `failed` request
 * shows its draft's changes, each with the summary its request wrote, then its checks and the reviewer's notes. A
 * failed one says why first. The author of a ready one accepts it, which runs as a publish request whose status shows
 * on the page, or drops it. An accept that met resources changed since the draft read them lists each, and its author
 * makes the change again on the folder as it is now, or drops the request. Below the heading its parts are a rule
 * apart, on the content's `surface`, with no link back: the sidebar marks AI requests and the browser goes back. Under "Progress" a `StepsBar` shows the loop's stages, then the
 * state in words with the fix round, and while it runs "Now:" with the summary of the step that has not finished.
 * "What AI did" lists every step with its result and when it started.
 */
export function StudioAuthoringRequestPage({
  application,
  requestId,
  userId,
  resources,
  onOpen,
}: {
  application: string
  requestId: string
  userId: string | null
  /** The application's resources, which name the files of the draft's diff. */
  resources: StudioResourceItem[]
  /** Opens a resource at a path, from a problem or a failed scenario of the checks. */
  onOpen: (resourceId: string, path: string) => void
}) {
  const t = useText()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const { colour, size, spacing } = useDesignTokens()
  const sentTime = useSentTime()
  const stateWords = useRequestStateWords()
  const { generation, lost } = useStudioConnection()
  const [loaded, setLoaded] = useState<{ id: string; request: StudioAuthoringRequestDetail | null }>()
  const [follow, setFollow] = useState(0)
  const [acting, setActing] = useState(false)
  const [actionError, setActionError] = useState(false)
  // Which comment the dialog asks for: another plan, or changes to a ready draft.
  const [changeOpen, setChangeOpen] = useState<false | 'change-plan' | 'request-changes'>(false)
  // Proposed: the page's tabs, the overview first, and the file a change in the overview opens, see the review on #3263.
  const [tab, setTab] = useState<{ id: string; key: RequestTab; file?: string }>()
  useEffect(() => {
    const file = tab?.key === 'changes' ? tab.file : undefined
    if (file === undefined) {
      return
    }
    // The diffs above the file draw late and push it down, so it is brought back into view each time the page grows,
    // until the person scrolls or two seconds pass.
    const target = () => document.querySelector(`[data-file="${CSS.escape(file)}"]`)
    // It stops below the header and the band, which stay at the top of the window.
    const bring = () => {
      const element = target()
      if (element instanceof HTMLElement) {
        element.style.scrollMarginTop = `${2 * size.bar + spacing.md}px`
        element.scrollIntoView({ block: 'start' })
      }
    }
    const frame = requestAnimationFrame(bring)
    const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(bring)
    const page = document.querySelector('[data-testid="studio-ai-request"]')
    if (observer !== null && page !== null) {
      observer.observe(page)
    }
    const stop = () => {
      cancelAnimationFrame(frame)
      observer?.disconnect()
      window.removeEventListener('wheel', stop, true)
      window.removeEventListener('touchmove', stop, true)
      window.removeEventListener('keydown', stop, true)
    }
    window.addEventListener('wheel', stop, true)
    window.addEventListener('touchmove', stop, true)
    window.addEventListener('keydown', stop, true)
    const timer = window.setTimeout(stop, 2000)
    return () => {
      window.clearTimeout(timer)
      stop()
    }
  }, [tab, size.bar, spacing.md])
  const [dropOpen, setDropOpen] = useState(false)
  const [changeForm] = Form.useForm<{ comment: string }>()
  const [diff, setDiff] = useState<{ draft: string; changes: StudioRevisionChange[] | 'failed' }>()
  const [accepting, setAccepting] = useState<{ id: string; accept: StudioRequest }>()

  const request = loaded?.id === requestId ? loaded.request : undefined
  const following = request !== undefined && request !== null && !settled.includes(request.state)
  const draft = request?.draft ?? null
  const accept = accepting?.id === requestId ? accepting.accept : (request?.accept ?? null)
  const acceptPending = accept !== null && (accept.status === 'queued' || accept.status === 'running')

  // The draft's diff, once the request has a draft that is ready or failed.
  const showsDiff = request?.state === 'ready' || request?.state === 'failed'
  useEffect(() => {
    if (draft === null || !showsDiff) {
      return
    }
    const controller = new AbortController()
    fetchDraftDiff(application, draft, controller.signal).then(
      (changes) => setDiff({ draft, changes }),
      (error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        if (error instanceof ServerUnreachableError) {
          lost()
          return
        }
        setDiff({ draft, changes: 'failed' })
      },
    )
    return () => controller.abort()
  }, [application, draft, showsDiff, generation, lost])

  // An accept runs as a publish request; when it ends the request is read again.
  useFollowJob(acceptPending && accept !== null ? `${application}/accept/${accept.id}/${generation}` : null, {
    read: (signal) => fetchStudioRequest(application, accept!.id, signal),
    ends: (next) => next.status !== 'queued' && next.status !== 'running',
    interval: 1000,
    waitFirst: true,
    onAnswer: (next) => setAccepting({ id: requestId, accept: next }),
    onEnd: async (next, signal) => {
      setAccepting({ id: requestId, accept: next })
      const reread = await fetchAuthoringRequest(application, requestId, signal)
      setLoaded({ id: requestId, request: reread })
    },
    onError: (error) => {
      if (error instanceof ServerUnreachableError) {
        lost()
        return 'stop'
      }
      return 'retry'
    },
  })

  useEffect(() => {
    const controller = new AbortController()
    fetchAuthoringRequest(application, requestId, controller.signal).then(
      (value) => setLoaded({ id: requestId, request: value }),
      (error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        if (error instanceof ServerUnreachableError) {
          lost()
          return
        }
        setLoaded({ id: requestId, request: null })
      },
    )
    return () => controller.abort()
  }, [application, requestId, generation, lost])

  useFollowJob(following ? `${application}/${requestId}/${generation}/${follow}` : null, {
    read: (signal) => fetchAuthoringRequest(application, requestId, signal),
    ends: (next) => next === null || settled.includes(next.state),
    interval: 1000,
    waitFirst: true,
    onAnswer: (next) => setLoaded({ id: requestId, request: next }),
    onEnd: (next) => {
      setLoaded({ id: requestId, request: next })
    },
    onError: (error) => {
      if (error instanceof ServerUnreachableError) {
        lost()
        return 'stop'
      }
      return 'retry'
    },
  })

  const back = <Link to={authoringRequestAddress(application)}>{t('shell.studio.aiRequests.back')}</Link>
  if (request === undefined) {
    return <Spin />
  }
  if (request === null) {
    return (
      <Flex vertical gap="small">
        <Typography.Text>{t('shell.studio.aiRequests.notFound')}</Typography.Text>
        {back}
      </Flex>
    )
  }

  const mine = request.author === userId
  const act = async (action: 'approve' | 'change-plan' | 'request-changes' | 'drop' | 'retry', comment?: string) => {
    setActing(true)
    setActionError(false)
    try {
      const next = await actOnAuthoringRequest(application, request.id, action, comment)
      setLoaded({ id: requestId, request: { ...request, ...next } })
      setAccepting(undefined)
      setFollow((value) => value + 1)
      setChangeOpen(false)
      setDropOpen(false)
      changeForm.resetFields()
    } catch (error) {
      if (error instanceof ServerUnreachableError) {
        lost()
      } else {
        setActionError(true)
      }
    } finally {
      setActing(false)
    }
  }
  // Proposed: the AIs that made the change are named as git names co-authors, in the sender's line, see the review
  // on #3263.
  const lineKey = `shell.studio.aiRequest.${mine ? 'lineMine' : 'line'}${request.coAuthors.length > 0 ? 'CoAuthored' : ''}`
  const line = t(lineKey)
    .replace('{coAuthors}', new Intl.ListFormat(locale, { type: 'conjunction' }).format(request.coAuthors))
    .replace('{name}', request.authorName ?? t('shell.studio.ai.someone'))
    .replace('{time}', sentTime(request.sentAt))
  // The page reads as one document on the content's `surface`, its parts a rule apart.
  const section = (testName: string, title: string, body: ReactNode) => (
    <SectionPart testName={testName} title={title}>
      {body}
    </SectionPart>
  )
  const quoted = (text: string) => <div style={{ whiteSpace: 'pre-wrap' }}>{text}</div>
  // Proposed: one frame for every request, sent from Studio or made by an outside AI app, see the review on #3263.
  const stage = stageOf(request.state)
  // Axis does the AI's part of any request whose author has connected their AI, whichever way it began.
  const waitsForApp =
    request.app !== null && !request.aiConnected && ['planning', 'working', 'fixing'].includes(request.state)
  const stageTitles = stageKeys.map((key) => t(`shell.studio.aiRequest.stage.${key}`))
  const running = request.steps.findLast((step) => step.finishedAt === null)
  const stepColumns: AxisColumn<StudioAuthoringRequestStep>[] = [
    {
      key: 'step',
      kind: 'freeText',
      title: t('shell.studio.aiRequest.step'),
      // A change of plan, and an ask for changes to a ready draft, keep the author's comment as their summary.
      render: (_, step) =>
        step.kind === 'planChange'
          ? t('shell.studio.aiRequest.planChange').replace('{comment}', step.summary)
          : step.kind === 'changeRequest'
            ? t('shell.studio.aiRequest.changeRequest').replace('{comment}', step.summary)
            : step.summary,
    },
    {
      key: 'result',
      kind: 'freeText',
      title: t('shell.studio.aiRequest.result'),
      render: (_, step) =>
        step.finishedAt === null ? (
          <span style={{ color: colour.textMuted }}>{t('shell.studio.aiRequest.stepRunning')}</span>
        ) : (
          (step.outcome ?? <EmptyDash />)
        ),
    },
    {
      key: 'started',
      kind: 'fixed',
      title: t('shell.studio.aiRequest.started'),
      render: (_, step) => sentTime(step.startedAt),
    },
  ]
  const startAccept = async () => {
    setActing(true)
    setActionError(false)
    try {
      setAccepting({ id: requestId, accept: await acceptAuthoringRequest(application, request.id) })
    } catch (error) {
      if (error instanceof ServerUnreachableError) {
        lost()
      } else {
        setActionError(true)
      }
    } finally {
      setActing(false)
    }
  }
  const canDrop = mine && request.state !== 'accepted' && request.state !== 'dropped'
  const awaiting = request.state === 'awaitingApproval'
  const ready = request.state === 'ready'
  // An accept that met resources changed since the draft read them, one AXC0377 for each.
  const conflicts =
    accept !== null && accept.status === 'failed'
      ? accept.diagnostics.filter((diagnostic) => diagnostic.code === 'AXC0377')
      : []
  const changes = diff?.draft === draft ? diff?.changes : undefined
  const askingChanges = changeOpen === 'request-changes'
  const summaries = new Map(request.changes.map((change) => [change.file, change.summary]))
  const problems = request.checks?.diagnostics ?? []

  // Proposed: the page in tabs, as a pull request is: what a person needs to decide first, then the detail.
  const hasDiff = showsDiff && draft !== null
  const hasChecks = showsDiff && request.checks !== null
  const working = !['ready', 'accepted', 'failed', 'dropped'].includes(request.state)
  const fixRound = request.round > 1 && working && request.state !== 'planning'
  const activeTab: RequestTab =
    tab?.id === requestId &&
    ((tab.key === 'changes' && hasDiff) ||
      (tab.key === 'checks' && hasChecks) ||
      tab.key === 'overview' ||
      (tab.key === 'activity' && request.steps.length > 0))
      ? tab.key
      : 'overview'
  const openFile = (file: string) => setTab({ id: requestId, key: 'changes', file })
  const changeList = Array.isArray(changes)
    ? changes.map((change) => ({
        file: change.file,
        name: change.name ?? change.file,
        detail: `${kindSingular(change.kind ?? '', t)}, ${t(`shell.studio.overview.change.${change.change}`).toLowerCase()}`,
        summary: summaries.get(change.file),
      }))
    : request.changes.map((change) => ({ file: change.file, name: change.file, detail: '', summary: change.summary }))
  const checksLine = request.checks !== null ? checksSummary(request.checks, t) : null
  const overview = (
    <SectionParts>
      {actionError && <AxisAlert kind="error" message={t('shell.studio.aiRequest.actionFailed')} />}
      {request.state === 'failed' && (
        <AxisAlert
          kind="error"
          message={t('shell.studio.aiRequest.failed')}
          description={request.failure ?? undefined}
          data-testid="studio-ai-failure"
        />
      )}
      {conflicts.length > 0 ? (
        <AxisAlert
          kind="warning"
          data-testid="studio-ai-conflict"
          message={t('shell.studio.aiRequest.conflict')}
          description={
            <ul style={{ margin: 0, paddingInlineStart: spacing.lg }}>
              {conflicts.map((conflict, index) => (
                <li key={index}>{conflict.message}</li>
              ))}
            </ul>
          }
        />
      ) : (
        accept !== null && <StudioRequestStatus request={accept} releaseNumber={null} />
      )}
      {hasChecks &&
        section(
          'ai-checks',
          t('shell.studio.aiRequest.checks'),
          <Flex vertical gap="small" data-testid="studio-ai-checks">
            {checksLine !== null && (
              <Flex align="center" gap={spacing.sm} wrap>
                <Typography.Text style={{ color: checksLine.passed ? colour.successText : colour.dangerText }}>
                  {checksLine.text}
                </Typography.Text>
                <AxisButton role="link" onClick={() => setTab({ id: requestId, key: 'checks' })}>
                  {t('shell.studio.aiRequest.seeChecks')}
                </AxisButton>
              </Flex>
            )}
            {/* Proposed: an outside app's ready request has no review when its author has no AI of their own. */}
            {request.review === null && request.app !== null && request.state === 'ready' && (
              <Typography.Text style={{ color: colour.textMuted }} data-testid="studio-ai-not-reviewed">
                {t(mine ? 'shell.studio.aiRequest.notReviewedMine' : 'shell.studio.aiRequest.notReviewed')}
              </Typography.Text>
            )}
            {/* The review runs only once the checks pass, so a request that failed before it has no findings. */}
            {request.review !== null && (
              <ReviewFindings findings={request.review} ready={request.state === 'ready'} onOpen={onOpen} />
            )}
          </Flex>,
        )}
      {hasDiff &&
        changeList.length > 0 &&
        section(
          'ai-what-changes',
          t('shell.studio.aiRequest.whatChanges'),
          <ul style={{ listStyle: 'none', margin: 0, padding: 0 }} data-testid="studio-ai-what-changes">
            {changeList.map((change) => (
              <li key={change.file} style={{ paddingBlock: spacing.xs / 2 }}>
                <AxisButton role="link" onClick={() => openFile(change.file)}>
                  {change.name}
                </AxisButton>
                {change.detail !== '' && (
                  <Typography.Text style={{ color: colour.textMuted }}>{` ${change.detail}`}</Typography.Text>
                )}
                {change.summary !== undefined && <div>{change.summary}</div>}
              </li>
            ))}
          </ul>,
        )}
      {request.plan !== null &&
        section(
          'ai-plan',
          t('shell.studio.aiRequest.plan'),
          <Flex vertical gap="small">
            <ul style={{ margin: 0, paddingInlineStart: spacing.lg }} data-testid="studio-ai-plan">
              {request.plan
                .split('\n')
                .filter((step) => step.trim() !== '')
                .map((step, index) => (
                  <li key={index}>{step}</li>
                ))}
            </ul>
            {awaiting && !mine && (
              <Typography.Text style={{ color: colour.textMuted }}>
                {t('shell.studio.aiRequest.notYours')}
              </Typography.Text>
            )}
          </Flex>,
        )}
      {awaiting && mine && (
        <AxisActionRow>
          <AxisButton role="finish" disabled={acting} onClick={() => void act('approve')}>
            {t('shell.studio.aiRequest.approve')}
          </AxisButton>
          <AxisButton role="secondary" disabled={acting} onClick={() => setChangeOpen('change-plan')}>
            {t('shell.studio.aiRequest.changePlan')}
          </AxisButton>
          <AxisButton role="secondary" disabled={acting} onClick={() => setDropOpen(true)}>
            {t('shell.studio.aiRequest.drop')}
          </AxisButton>
        </AxisActionRow>
      )}
    </SectionParts>
  )
  const changesView =
    changes === undefined ? (
      <Spin />
    ) : changes === 'failed' ? (
      <AxisAlert kind="error" message={t('shell.studio.aiRequest.diffFailed')} />
    ) : (
      <div data-testid="studio-ai-changes" style={{ display: 'flex', flexDirection: 'column', gap: spacing.md }}>
        <StudioChanges changes={changes} resources={resources} summaries={summaries} />
      </div>
    )
  const checksView = (
    <Flex vertical gap="small" data-testid="studio-ai-check-details">
      {problems.length > 0 && (
        <ul style={{ listStyle: 'none', margin: 0, padding: 0 }}>
          {problems.map((diagnostic, index) => (
            <li key={index}>
              <StudioDiagnosticRow diagnostic={diagnostic} onSelect={onOpen} />
            </li>
          ))}
        </ul>
      )}
      {request.checks !== null && <StudioTestResults request={request.checks} onSelect={onOpen} nested />}
    </Flex>
  )
  const activityView = (
    <AxisTable<StudioAuthoringRequestStep>
      columns={stepColumns}
      rowKey={(step) => `${step.startedAt}/${step.kind}`}
      dataSource={request.steps}
      pagination={false}
      toolbar={{}}
      emptyText=""
    />
  )
  const openBlocking = (request.review ?? []).filter((finding) => finding.blocking).length

  return (
    <Flex vertical gap="middle" data-testid="studio-ai-request">
      {/* Proposed: the request's name as the heading, then its state at the start of the line that says who sent it and
          the AIs that made it, as a pull request's page does, then its words, see the review on #3263. */}
      <StudioPageHeading
        title={requestTitle(request)}
        actions={
          canDrop && !awaiting && !ready ? (
            <AxisButton role="secondary" disabled={acting} onClick={() => setDropOpen(true)}>
              {t('shell.studio.aiRequest.drop')}
            </AxisButton>
          ) : undefined
        }
      />
      <Flex vertical gap={spacing.sm} data-testid="studio-ai-asked">
        <Flex align="center" gap={spacing.sm} wrap>
          <StatusTag tone={requestStateTone(request.state)}>{stateWords(request)}</StatusTag>
          <Typography.Text style={{ color: colour.textMuted }}>{line}</Typography.Text>
        </Flex>
        {quoted(request.text)}
      </Flex>
      {/* Proposed: while AI works, its stages, the fix round and what it does now, see the review on #3263. */}
      <Flex vertical gap="small" data-testid="studio-ai-progress">
        {stage !== null && working && <StepsBar titles={stageTitles} current={stage} />}
        {/* One line while AI works: the spinner, what it does now, or that it writes the plan, and the fix round. */}
        {(fixRound || (following && !waitsForApp)) && (
          <Flex align="center" gap={spacing.sm} wrap>
            {following && !waitsForApp && <Spin size="small" />}
            {following && !waitsForApp && (running !== undefined || request.state === 'planning') && (
              <Typography.Text style={{ color: colour.textMuted }}>
                {request.state === 'planning' || running === undefined
                  ? t('shell.studio.aiRequest.planning')
                  : t('shell.studio.aiRequest.now').replace('{step}', running.summary)}
              </Typography.Text>
            )}
            {fixRound && (
              <Typography.Text style={{ color: colour.textMuted }}>
                {t('shell.studio.aiRequest.round').replace('{round}', String(request.round))}
              </Typography.Text>
            )}
          </Flex>
        )}
        {/* Proposed: with no AI of its author's here, only an outside app goes on with it, see the review on #3263. */}
        {waitsForApp && (
          <Typography.Text style={{ color: colour.textMuted }} data-testid="studio-ai-app-works">
            {t(mine ? 'shell.studio.aiRequest.appWorksMine' : 'shell.studio.aiRequest.appWorks').replaceAll(
              '{app}',
              request.app ?? '',
            )}
          </Typography.Text>
        )}
      </Flex>
      <Tabs
        activeKey={activeTab}
        onChange={(key) => setTab({ id: requestId, key: key as RequestTab })}
        items={[
          { key: 'overview', label: t('shell.studio.aiRequest.tab.overview'), children: overview },
          ...(hasDiff
            ? [
                {
                  key: 'changes',
                  label: t('shell.studio.aiRequest.tab.changes').replace(
                    '{count}',
                    String(Array.isArray(changes) ? changes.length : request.changes.length),
                  ),
                  children: changesView,
                },
              ]
            : []),
          ...(hasChecks
            ? [{ key: 'checks', label: t('shell.studio.aiRequest.tab.checks'), children: checksView }]
            : []),
          ...(request.steps.length > 0
            ? [{ key: 'activity', label: t('shell.studio.aiRequest.steps'), children: activityView }]
            : []),
        ]}
      />
      {/* What finishes the page's task, accepting the draft, sits in the bar at the foot of the window. */}
      {ready && mine && !acceptPending && (
        <ActionBar testId="studio-ai-request-bar">
          <span>
            {openBlocking > 0
              ? t(
                  openBlocking === 1
                    ? 'shell.studio.aiRequest.bar.oneMustChange'
                    : 'shell.studio.aiRequest.bar.mustChange',
                ).replace('{count}', String(openBlocking))
              : t('shell.studio.aiRequest.bar.passed')}
          </span>
          <AxisActionRow align="center">
            <AxisButton role="secondary" disabled={acting} onClick={() => setDropOpen(true)}>
              {t('shell.studio.aiRequest.drop')}
            </AxisButton>
            {/* Proposed: the author asks AI to change the ready draft and it works on again, see the review on #3263. */}
            <AxisButton role="secondary" disabled={acting} onClick={() => setChangeOpen('request-changes')}>
              {t('shell.studio.aiRequest.requestChanges')}
            </AxisButton>
            {conflicts.length > 0 ? (
              <AxisButton role="finish" disabled={acting} onClick={() => void act('retry')}>
                {t('shell.studio.aiRequest.retry')}
              </AxisButton>
            ) : (
              <AxisButton role="finish" disabled={acting} onClick={() => void startAccept()}>
                {t('shell.studio.aiRequest.accept')}
              </AxisButton>
            )}
          </AxisActionRow>
        </ActionBar>
      )}
      <Modal
        forceRender
        open={changeOpen !== false}
        width={size.dialog.medium}
        title={t(
          changeOpen === 'request-changes'
            ? 'shell.studio.aiRequest.requestChanges.title'
            : 'shell.studio.aiRequest.changePlan.title',
        )}
        okText={t(
          changeOpen === 'request-changes'
            ? 'shell.studio.aiRequest.requestChanges.ok'
            : 'shell.studio.aiRequest.changePlan.ok',
        )}
        cancelText={t('shell.studio.cancel')}
        confirmLoading={acting}
        onOk={() => changeForm.submit()}
        onCancel={() => setChangeOpen(false)}
      >
        <Form
          form={changeForm}
          layout="vertical"
          requiredMark={requiredMark}
          onFinish={(values) => void act(changeOpen || 'change-plan', values.comment.trim())}
        >
          <AxisField
            name="comment"
            label={t(askingChanges ? 'shell.studio.aiRequest.changesComment' : 'shell.studio.aiRequest.comment')}
            help={t(
              askingChanges ? 'shell.studio.aiRequest.changesComment.help' : 'shell.studio.aiRequest.comment.help',
            )}
            requiredMessage={t(
              askingChanges
                ? 'shell.studio.aiRequest.changesCommentRequired'
                : 'shell.studio.aiRequest.commentRequired',
            )}
          >
            <Input.TextArea
              aria-label={t(askingChanges ? 'shell.studio.aiRequest.changesComment' : 'shell.studio.aiRequest.comment')}
              autoSize={{ minRows: 3, maxRows: 8 }}
              maxLength={2000}
            />
          </AxisField>
        </Form>
      </Modal>
      <Modal
        open={dropOpen}
        width={size.dialog.small}
        title={t('shell.studio.aiRequest.dropConfirm')}
        okText={t('shell.studio.aiRequest.dropOk')}
        okButtonProps={{ danger: true }}
        cancelText={t('shell.studio.cancel')}
        confirmLoading={acting}
        onOk={() => void act('drop')}
        onCancel={() => setDropOpen(false)}
      >
        {t('shell.studio.aiRequest.dropHelp')}
      </Modal>
    </Flex>
  )
}

/**
 * Proposed: a request's review, see the review on #3263. Blocking findings that its fix rounds did not resolve come
 * first, under a warning, then its notes, each as the Problems panel draws a problem at its resource and path. With
 * none, it says the review found nothing that must change, and on a ready request that a review is no guarantee.
 */
function ReviewFindings({
  findings,
  ready,
  onOpen,
}: {
  findings: StudioReviewFinding[]
  ready: boolean
  onOpen: (resourceId: string, path: string) => void
}) {
  const t = useText()
  const { colour } = useDesignTokens()
  const blocking = findings.filter((finding) => finding.blocking)
  const notes = findings.filter((finding) => !finding.blocking)
  const rows = (items: StudioReviewFinding[]) => (
    <ul style={{ listStyle: 'none', margin: 0, padding: 0 }}>
      {items.map((finding, index) => (
        <li key={index}>
          <StudioDiagnosticRow
            diagnostic={{
              code: '',
              severity: finding.blocking ? 'error' : 'warning',
              message: finding.text,
              file: finding.file,
              path: finding.path,
              resourceId: finding.resourceId,
            }}
            onSelect={onOpen}
            bare
          />
        </li>
      ))}
    </ul>
  )
  return (
    <Flex vertical gap="small" data-testid="studio-ai-review">
      <Typography.Text strong>{t('shell.studio.aiRequest.review')}</Typography.Text>
      {blocking.length > 0 && (
        <>
          <AxisAlert kind="warning" message={t('shell.studio.aiRequest.openFindings')} />
          <Typography.Text strong>{t('shell.studio.aiRequest.mustChange')}</Typography.Text>
          {rows(blocking)}
        </>
      )}
      {notes.length > 0 && (
        <>
          <Typography.Text strong>{t('shell.studio.aiRequest.notes')}</Typography.Text>
          {rows(notes)}
        </>
      )}
      {findings.length === 0 && (
        <Typography.Text style={{ color: colour.textMuted }}>{t('shell.studio.aiRequest.noFindings')}</Typography.Text>
      )}
      {ready && (
        <Typography.Text style={{ color: colour.textMuted }}>{t('shell.studio.aiRequest.noGuarantee')}</Typography.Text>
      )}
    </Flex>
  )
}

/** Proposed: the tabs of a request's page, see the review on #3263. */
type RequestTab = 'overview' | 'changes' | 'checks' | 'activity'

/** Proposed: the checks of a request in one line, and whether they passed, see the review on #3263. */
function checksSummary(checks: StudioRequest, t: (key: string) => string): { text: string; passed: boolean } {
  const errors = checks.diagnostics.filter((diagnostic) => diagnostic.severity === 'error').length
  if (errors > 0) {
    return {
      text: t(
        errors === 1 ? 'shell.studio.aiRequest.checks.oneProblem' : 'shell.studio.aiRequest.checks.problems',
      ).replace('{count}', String(errors)),
      passed: false,
    }
  }
  const scenarios = (checks.scenarios ?? []).filter(
    (scenario) => 'outcome' in scenario && scenario.outcome !== undefined,
  )
  const passed = scenarios.filter((scenario) => scenario.outcome === 'passed').length
  if (scenarios.length === 0) {
    return { text: t('shell.studio.aiRequest.checks.noScenarios'), passed: true }
  }
  return {
    text: t('shell.studio.aiRequest.checks.scenarios')
      .replace('{passed}', String(passed))
      .replace('{total}', String(scenarios.length)),
    passed: passed === scenarios.length,
  }
}
