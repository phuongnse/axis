import { Flex, Form, Input, Modal, Spin, Tabs, Typography } from 'antd'
import { useContext, useEffect, useState, type HTMLAttributes, type ReactNode } from 'react'
import { Link } from 'react-router'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisActionRow, AxisButton } from '../platform/AxisButton'
import { AxisField } from '../platform/AxisField'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
import { EmptyDash } from '../platform/EmptyValue'
import { FilledText } from '../platform/FilledText'
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
  type StudioAuthoringStepKind,
  type StudioDiagnostic,
  type StudioPlan,
  type StudioReview,
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
import { SectionPart, SectionParts } from './ResourceFormSection'
import { ActionBar } from './SaveBar'
import { StudioPageHeading } from './StudioPageHeading'
import { StudioProblemList } from './StudioProblems'
import { ResourceReference } from './ResourceReference'
import { visuallyHidden } from '../platform/visuallyHidden'
import { CheckOutlined, CloseOutlined } from '@ant-design/icons'
import { useDiagnosticItems } from './useDiagnosticItems'
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
  const diagnosticItems = useDiagnosticItems()
  const sentTime = useSentTime()
  const stateWords = useRequestStateWords()
  const { generation, lost } = useStudioConnection()
  const [loaded, setLoaded] = useState<{ id: string; request: StudioAuthoringRequestDetail | null }>()
  const [follow, setFollow] = useState(0)
  const [acting, setActing] = useState(false)
  const [actionError, setActionError] = useState(false)
  // Which comment the dialog asks for: another plan, or changes to a ready draft.
  const [changeOpen, setChangeOpen] = useState<false | 'change-plan' | 'request-changes'>(false)
  // Proposed: the page's tab, see the review on #3263.
  const [tab, setTab] = useState<{ id: string; key: RequestTab }>()
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
  // Proposed: a step in words Studio writes from its kind, see the review on #3263, the same for every request and in the
  // reader's language. A change of plan, and an ask for changes to a ready draft, keep the author's comment, and a write
  // names its resource as a link.
  const stepWords = (step: StudioAuthoringRequestStep): ReactNode => {
    switch (step.kind) {
      case 'planChange':
      case 'changeRequest':
        return t(`shell.studio.aiRequest.${step.kind}`).replace('{comment}', step.summary ?? '')
      case 'write_resource':
      case 'write_layer':
      case 'delete_resource':
        return (
          <FilledText
            text={t(`shell.studio.aiRequest.step.${step.kind === 'delete_resource' ? 'delete' : 'write'}`)}
            parts={{
              resource:
                step.resource == null ? null : (
                  <ResourceReference
                    resourceId={step.resource.resourceId}
                    file={step.resource.file}
                    name={step.resource.name}
                    kind={step.resource.kind}
                    path=""
                    resources={resources}
                    onSelect={onOpen}
                  />
                ),
            }}
          />
        )
      default:
        return t(stepKeys[step.kind])
    }
  }
  const runningWords = <span style={{ color: colour.textMuted }}>{t('shell.studio.aiRequest.stepRunning')}</span>
  const stepColumns: AxisColumn<HistoryRow>[] = [
    {
      key: 'step',
      kind: 'freeText',
      title: t('shell.studio.aiRequest.step'),
      // Proposed: writes in a row read as one group row, which the table opens to a row for each, see the review on #3263.
      render: (_, row) =>
        row.steps.length === 1
          ? stepWords(row.steps[0])
          : t(
              row.steps.some((step) => step.kind === 'delete_resource')
                ? 'shell.studio.aiRequest.history.changes'
                : 'shell.studio.aiRequest.history.writes',
            ).replace('{count}', String(row.steps.length)),
    },
    {
      key: 'result',
      kind: 'freeText',
      title: t('shell.studio.aiRequest.result'),
      // Proposed: a step's result in words Studio writes from its outcome and its checks, see the review on #3263; a group
      // row's is its writes' when they share one.
      render: (_, row) => {
        if (row.steps.some((step) => step.finishedAt === null)) {
          return runningWords
        }
        const results = new Set(row.steps.map((step) => stepResult(step, t)))
        return results.size > 1 ? t('shell.studio.aiRequest.history.mixed') : ([...results][0] ?? <EmptyDash />)
      },
    },
    {
      key: 'started',
      kind: 'fixed',
      title: t('shell.studio.aiRequest.started'),
      render: (_, row) => sentTime(row.steps[0].startedAt),
    },
    {
      // Proposed: how long a step took, which, with when it started, says when it ended, see the review on #3263.
      key: 'took',
      kind: 'fixed',
      title: t('shell.studio.aiRequest.took'),
      render: (_, row) => {
        const end = row.steps.at(-1)!.finishedAt
        return end === null ? <EmptyDash /> : formatDuration(Date.parse(end) - Date.parse(row.steps[0].startedAt), t)
      },
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
  // The overview shows only what an author decides on, the checks and the review or an alert, so it may have nothing.
  const hasOverview = hasChecks || request.state === 'failed' || conflicts.length > 0 || accept !== null
  const shown: RequestTab[] = [
    ...(hasOverview ? (['overview'] as const) : []),
    ...(hasDiff ? (['changes'] as const) : []),
    ...(hasChecks ? (['checks'] as const) : []),
    ...(request.plan !== null ? (['plan'] as const) : []),
    ...(request.steps.length > 0 ? (['history'] as const) : []),
  ]
  // A request that waits for its plan's approval opens on its plan; any other on its first tab.
  const activeTab: RequestTab | undefined =
    tab?.id === requestId && shown.includes(tab.key) ? tab.key : awaiting && shown.includes('plan') ? 'plan' : shown[0]
  const checksLine =
    request.checks !== null
      ? checksSummary(
          request.checks.diagnostics,
          (request.checks.scenarios ?? []).filter(
            (scenario) => 'outcome' in scenario && scenario.outcome !== undefined,
          ),
          t,
        )
      : null
  const overview = (
    <SectionParts>
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
          // Proposed: the checks and the review as lines of one shape, see the review on #3263: each its name in bold and its
          // verdict as a tag, in columns so the tags start together, its summary under it, then what the review found.
          <div
            data-testid="studio-ai-checks"
            style={{
              display: 'grid',
              gridTemplateColumns: 'max-content minmax(0, 1fr)',
              columnGap: spacing.sm,
              rowGap: spacing.xs,
              alignItems: 'baseline',
              // A tag keeps its own width in its column.
              justifyItems: 'start',
            }}
          >
            {checksLine !== null && (
              <>
                <Typography.Text strong>{t('shell.studio.aiRequest.tab.checks')}</Typography.Text>
                <StatusTag tone={checksLine.passed ? 'success' : 'danger'}>
                  {t(
                    checksLine.passed ? 'shell.studio.aiRequest.checks.passed' : 'shell.studio.aiRequest.checks.failed',
                  )}
                </StatusTag>
                <span style={{ gridColumn: '1 / -1' }}>
                  <Typography.Text>{checksLine.text}</Typography.Text>{' '}
                  <AxisButton role="link" size="small" onClick={() => setTab({ id: requestId, key: 'checks' })}>
                    {t('shell.studio.aiRequest.seeChecks')}
                  </AxisButton>
                </span>
              </>
            )}
            {/* Proposed: an outside app's ready request has no review when its author has no AI of their own. */}
            {request.review === null && request.app !== null && request.state === 'ready' && (
              <div style={{ display: 'contents' }} data-testid="studio-ai-not-reviewed">
                <Typography.Text strong style={{ marginTop: spacing.xs }}>
                  {t('shell.studio.aiRequest.review')}
                </Typography.Text>
                <span style={{ marginTop: spacing.xs }}>
                  <StatusTag tone="muted">{t('shell.studio.aiRequest.review.notReviewed')}</StatusTag>
                </span>
                <Typography.Text style={{ gridColumn: '1 / -1' }}>
                  {t(mine ? 'shell.studio.aiRequest.notReviewedMine' : 'shell.studio.aiRequest.notReviewed')}
                </Typography.Text>
              </div>
            )}
            {/* The review runs only once the checks pass, so a request that failed before it has no findings. */}
            {request.review !== null && (
              <ReviewFindings review={request.review} resources={resources} onOpen={onOpen} />
            )}
          </div>,
        )}
    </SectionParts>
  )
  const planView = request.plan !== null && (
    <PlanView plan={request.plan} review={request.review} resources={resources} onOpen={onOpen}>
      {awaiting && !mine && (
        <Typography.Text style={{ color: colour.textMuted }}>{t('shell.studio.aiRequest.notYours')}</Typography.Text>
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
    </PlanView>
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
        <StudioProblemList items={diagnosticItems(problems)} resources={resources} onSelect={onOpen} />
      )}
      {request.checks !== null && (
        <StudioTestResults request={request.checks} resources={resources} onSelect={onOpen} nested />
      )}
    </Flex>
  )
  const historyView = (
    <AxisTable<HistoryRow>
      columns={stepColumns}
      rowKey={(row) => row.key}
      dataSource={historyRows(request.steps)}
      pagination={false}
      toolbar={{}}
      emptyText=""
    />
  )
  const openBlocking = (request.review?.findings ?? []).filter((finding) => finding.blocking).length

  return (
    <Flex vertical gap="middle" data-testid="studio-ai-request">
      {/* Proposed: the request's name as the heading with its state beside it, then, close under it, its words and who
          sent it with the AIs that made it, see the review on #3263. */}
      <Flex vertical gap={spacing.xs} data-testid="studio-ai-asked">
        <StudioPageHeading
          title={requestTitle(request)}
          tags={<StatusTag tone={requestStateTone(request.state)}>{stateWords(request)}</StatusTag>}
          actions={
            canDrop && !awaiting && !ready ? (
              <AxisButton role="secondary" disabled={acting} onClick={() => setDropOpen(true)}>
                {t('shell.studio.aiRequest.drop')}
              </AxisButton>
            ) : undefined
          }
        />
        {quoted(request.text)}
        <Typography.Text style={{ color: colour.textMuted }}>{line}</Typography.Text>
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
                {request.state === 'planning' || running === undefined ? (
                  t('shell.studio.aiRequest.planning')
                ) : (
                  <FilledText text={t('shell.studio.aiRequest.now')} parts={{ step: stepWords(running) }} />
                )}
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
      {/* A failed action shows above the tabs, as it may come from any of them. */}
      {actionError && <AxisAlert kind="error" message={t('shell.studio.aiRequest.actionFailed')} />}
      <Tabs
        activeKey={activeTab}
        onChange={(key) => setTab({ id: requestId, key: key as RequestTab })}
        items={shown.map((key) => ({
          key,
          // Proposed: a tab is named by its words alone, with no count, as no other tab in Axis counts, see the review on #3263.
          label: t(`shell.studio.aiRequest.tab.${key}`),
          children: { overview, changes: changesView, checks: checksView, plan: planView, history: historyView }[key],
        }))}
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
 * Proposed: a request's plan, written as a pull request's plan is, see the review on #3263: each a group named in bold,
 * What changes, Decisions to check, each choice with its reason, muted, Effects and risks, and How it will be checked,
 * then its details, resource by resource, folded until asked for, and last `children`, such as the approval's actions.
 * Once a review ran, each step of What changes is its checklist item, a check mark when the draft does it and a cross
 * when it does not, with the resource where, as a pull request's description ticks its own list.
 */
function PlanView({
  plan,
  review,
  resources,
  onOpen,
  children,
}: {
  plan: StudioPlan
  review: StudioReview | null
  resources: readonly StudioResourceItem[]
  onOpen: (resourceId: string, path: string) => void
  children: ReactNode
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const [details, setDetails] = useState(false)
  const bullets = (items: readonly ReactNode[]) => (
    <ul style={{ margin: 0, paddingInlineStart: spacing.lg }}>
      {items.map((item, index) => (
        <li key={index}>{item}</li>
      ))}
    </ul>
  )
  const group = (key: string, testId: string, items: readonly ReactNode[]) =>
    items.length > 0 && (
      <Flex vertical gap={spacing.xs} data-testid={testId}>
        <Typography.Text strong>{t(key)}</Typography.Text>
        {bullets(items)}
      </Flex>
    )
  return (
    <Flex vertical gap="middle" data-testid="studio-ai-plan">
      {review === null || review.steps.length === 0 ? (
        group('shell.studio.aiRequest.plan.changes', 'studio-ai-plan-changes', plan.changes)
      ) : (
        <Flex vertical gap={spacing.xs} data-testid="studio-ai-plan-changes">
          <Typography.Text strong>{t('shell.studio.aiRequest.plan.changes')}</Typography.Text>
          <ul style={{ listStyle: 'none', margin: 0, padding: 0 }}>
            {review.steps.map((step, index) => (
              <li key={index} data-testid="studio-ai-review-step" data-done={step.done}>
                <Flex gap={spacing.xs} align="baseline" wrap>
                  <span style={{ color: step.done ? colour.successText : colour.dangerText }}>
                    {step.done ? <CheckOutlined aria-hidden /> : <CloseOutlined aria-hidden />}
                    <span style={visuallyHidden}>
                      {t(
                        step.done
                          ? 'shell.studio.aiRequest.review.stepDone'
                          : 'shell.studio.aiRequest.review.stepNotDone',
                      )}
                    </span>
                  </span>
                  <Typography.Text>{step.step}</Typography.Text>
                  <ResourceReference
                    resourceId={step.resourceId}
                    file={step.file}
                    name={step.name}
                    kind={step.kind}
                    path={step.path}
                    resources={resources}
                    onSelect={onOpen}
                  />
                </Flex>
              </li>
            ))}
          </ul>
        </Flex>
      )}
      {group(
        'shell.studio.aiRequest.decisions',
        'studio-ai-plan-decisions',
        // Proposed: each choice, then why, muted, so colour and not weight tells them apart, see the review on #3263.
        plan.decisions.map(({ decision, reason }) => (
          <>
            <Typography.Text>{decision}</Typography.Text>{' '}
            <Typography.Text style={{ color: colour.textMuted }}>{reason}</Typography.Text>
          </>
        )),
      )}
      {group('shell.studio.aiRequest.plan.risks', 'studio-ai-plan-risks', plan.risks)}
      {group('shell.studio.aiRequest.plan.checks', 'studio-ai-plan-checks', plan.checks)}
      {plan.details.length > 0 && (
        <Flex vertical gap={spacing.xs} data-testid="studio-ai-plan-details">
          <span>
            <AxisButton role="link" aria-expanded={details} onClick={() => setDetails((open) => !open)}>
              {t(details ? 'shell.studio.aiRequest.plan.hideDetails' : 'shell.studio.aiRequest.plan.showDetails')}
            </AxisButton>
          </span>
          {details && (
            <StudioProblemList
              items={plan.details.flatMap((detail) =>
                detail.edits.map((edit) => ({
                  lead: null,
                  message: edit,
                  resourceId: detail.resourceId,
                  file: detail.file,
                  path: '',
                  name: detail.name,
                  kind: detail.kind,
                })),
              )}
              resources={resources}
              onSelect={onOpen}
            />
          )}
        </Flex>
      )}
      {children}
    </Flex>
  )
}

/**
 * Proposed: a request's review, see the review on #3263, as cells of the overview's columns: "Review" in bold, its
 * verdict as a `StatusTag`, "Changes requested" while a finding must change and "No changes requested" otherwise, never
 * "Approved", as only its author approves, by accepting; its summary, a sentence or two, reads under them across the
 * columns. Its findings follow across the columns in a `StudioProblemList`, grouped by the resource they are about, what must change first,
 * each led by "Must change" or "Note". How it checked each plan step shows on the plan.
 */
function ReviewFindings({
  review,
  resources,
  onOpen,
}: {
  review: StudioReview
  resources: readonly StudioResourceItem[]
  onOpen: (resourceId: string, path: string) => void
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const blocking = review.findings.filter((finding) => finding.blocking)
  const notes = review.findings.filter((finding) => !finding.blocking)
  return (
    <div style={{ display: 'contents' }} data-testid="studio-ai-review">
      {/* The checks' lines come first; a wider gap parts the review's from them than its own lines have. */}
      <Typography.Text strong style={{ marginTop: spacing.xs }}>
        {t('shell.studio.aiRequest.review')}
      </Typography.Text>
      <span style={{ marginTop: spacing.xs }}>
        <StatusTag tone={blocking.length > 0 ? 'attention' : 'success'}>
          {t(
            blocking.length > 0
              ? 'shell.studio.aiRequest.review.changesRequested'
              : 'shell.studio.aiRequest.review.noChangesRequested',
          )}
        </StatusTag>
      </span>
      {/* Its tag closes the line, and its summary, a sentence or two, reads under it across the columns. */}
      <Typography.Text style={{ gridColumn: '1 / -1' }}>{review.summary}</Typography.Text>
      {review.findings.length > 0 && (
        <div style={{ gridColumn: '1 / -1' }}>
          <StudioProblemList
            items={[...blocking, ...notes].map((finding) => ({
              lead: (
                <Typography.Text style={{ color: finding.blocking ? colour.dangerText : colour.textMuted }}>
                  {t(
                    finding.blocking
                      ? 'shell.studio.aiRequest.review.mustChangeLead'
                      : 'shell.studio.aiRequest.review.noteLead',
                  )}
                </Typography.Text>
              ),
              message: finding.text,
              resourceId: finding.resourceId,
              file: finding.file,
              path: finding.path,
            }))}
            resources={resources}
            onSelect={onOpen}
          />
        </div>
      )}
    </div>
  )
}

/** Proposed: the tabs of a request's page, see the review on #3263. */
type RequestTab = 'overview' | 'changes' | 'checks' | 'plan' | 'history'

/**
 * Proposed: checks in one line, and whether they passed, see the review on #3263: the compile's errors when there are
 * any, otherwise the scenarios that passed, or that it compiles when no scenario ran, `scenarios` being `null`.
 */
function checksSummary(
  diagnostics: readonly StudioDiagnostic[],
  scenarios: readonly { outcome?: string }[] | null,
  t: (key: string) => string,
): { text: string; passed: boolean } {
  const errors = diagnostics.filter((diagnostic) => diagnostic.severity === 'error').length
  if (errors > 0) {
    return {
      text: t(
        errors === 1 ? 'shell.studio.aiRequest.checks.oneProblem' : 'shell.studio.aiRequest.checks.problems',
      ).replace('{count}', String(errors)),
      passed: false,
    }
  }
  if (scenarios === null) {
    return { text: t('shell.studio.aiRequest.checks.compiles'), passed: true }
  }
  const passed = scenarios.filter((scenario) => scenario.outcome === 'passed').length
  if (scenarios.length === 0) {
    return { text: t('shell.studio.aiRequest.checks.noScenarios'), passed: true }
  }
  if (passed === scenarios.length) {
    // One scenario, two and more read differently in English: "The scenario passes", "Both scenarios pass", "All 8 …".
    const key =
      passed === 1
        ? 'shell.studio.aiRequest.checks.oneScenario'
        : passed === 2
          ? 'shell.studio.aiRequest.checks.bothScenarios'
          : 'shell.studio.aiRequest.checks.allScenarios'
    return { text: t(key).replace('{total}', String(passed)), passed: true }
  }
  return {
    text: t('shell.studio.aiRequest.checks.scenarios')
      .replace('{passed}', String(passed))
      .replace('{total}', String(scenarios.length)),
    passed: false,
  }
}

/** The text key of each step kind whose words name nothing else. */
const stepKeys: Record<
  Exclude<
    StudioAuthoringStepKind,
    'planChange' | 'changeRequest' | 'write_resource' | 'write_layer' | 'delete_resource'
  >,
  string
> = {
  plan: 'shell.studio.aiRequest.step.plan',
  work: 'shell.studio.aiRequest.step.work',
  check: 'shell.studio.aiRequest.step.check',
  fix: 'shell.studio.aiRequest.step.fix',
  review: 'shell.studio.aiRequest.step.review',
  accept: 'shell.studio.aiRequest.step.accept',
  propose_plan: 'shell.studio.aiRequest.step.proposePlan',
  validate_draft: 'shell.studio.aiRequest.step.validateDraft',
  run_scenarios: 'shell.studio.aiRequest.step.runScenarios',
}

/**
 * The text key of each outcome whose words do not hang on the step's checks: why a step stopped its request, and how an
 * accept by the agent ended, see docs/reference/authoring-requests.md#budgets and #accepting.
 */
const outcomeKeys: Record<string, string> = {
  timedOut: 'shell.studio.aiRequest.result.timedOut',
  overBudget: 'shell.studio.aiRequest.result.overBudget',
  tokenEnded: 'shell.studio.aiRequest.result.tokenEnded',
  noPlan: 'shell.studio.aiRequest.result.noPlan',
  unfinished: 'shell.studio.aiRequest.result.unfinished',
  notAccepted: 'shell.studio.aiRequest.result.notAccepted',
  accepted: 'shell.studio.aiRequest.result.accepted',
  refused: 'shell.studio.aiRequest.result.refused',
  conflict: 'shell.studio.aiRequest.result.conflict',
}

/**
 * Proposed: a finished step's result in words, see the review on #3263: why it stopped its request, else what its checks
 * found, else what a step of its kind did, or `null` for a step that has no result, such as the author's comment.
 */
function stepResult(step: StudioAuthoringRequestStep, t: (key: string) => string): string | null {
  if (step.outcome !== null && Object.hasOwn(outcomeKeys, step.outcome)) {
    return t(outcomeKeys[step.outcome])
  }
  const checks = step.checks ?? null
  if (checks?.review !== undefined) {
    return t(
      checks.review.outcome === 'passed'
        ? 'shell.studio.aiRequest.review.noChangesRequested'
        : 'shell.studio.aiRequest.review.changesRequested',
    )
  }
  if (checks?.compile !== undefined || checks?.scenarios !== undefined) {
    const scenarios = checks.scenarios
    return checksSummary(
      checks.compile?.diagnostics ?? [],
      scenarios === undefined || scenarios.outcome === 'notRun' ? null : scenarios.items,
      t,
    ).text
  }
  switch (step.kind) {
    case 'plan':
    case 'propose_plan':
      return t('shell.studio.aiRequest.result.planWritten')
    case 'work':
    case 'fix':
      return t('shell.studio.aiRequest.result.draftSaved')
    case 'write_resource':
    case 'write_layer':
      return step.outcome === 'refused'
        ? t('shell.studio.aiRequest.result.notSaved')
        : t('shell.studio.aiRequest.result.saved')
    case 'delete_resource':
      return step.outcome === 'refused'
        ? t('shell.studio.aiRequest.result.notDeleted')
        : t('shell.studio.aiRequest.result.deleted')
    case 'check':
    case 'validate_draft':
    case 'run_scenarios':
      return step.outcome === 'failed'
        ? t('shell.studio.aiRequest.checks.failed')
        : t('shell.studio.aiRequest.checks.passed')
    case 'review':
      return t(
        step.outcome === 'findings'
          ? 'shell.studio.aiRequest.review.changesRequested'
          : 'shell.studio.aiRequest.review.noChangesRequested',
      )
    default:
      return null
  }
}

/** The kinds of step that write the draft, which the history reads as one row while they follow each other. */
const writeKinds = new Set(['write_resource', 'write_layer', 'delete_resource'])

/**
 * Proposed: one row of a request's history, see the review on #3263: one step, or writes that follow each other as a
 * group row whose `children`, a row for each write, the table opens and closes as it does any row's children.
 */
interface HistoryRow {
  key: string
  steps: StudioAuthoringRequestStep[]
  children?: HistoryRow[]
}

/** A request's steps as its history's rows: each step a row, but two writes or more in a row one row together. */
function historyRows(steps: readonly StudioAuthoringRequestStep[]): HistoryRow[] {
  const rows: HistoryRow[] = []
  for (const step of steps) {
    const last = rows.at(-1)
    if (last !== undefined && writeKinds.has(step.kind) && writeKinds.has(last.steps[0].kind)) {
      last.steps.push(step)
    } else {
      rows.push({ key: `${step.startedAt}/${step.kind}/${rows.length}`, steps: [step] })
    }
  }
  return rows.map((row) =>
    row.steps.length === 1
      ? row
      : { ...row, children: row.steps.map((step, index) => ({ key: `${row.key}/${index}`, steps: [step] })) },
  )
}

/** Proposed: a span of time in the reader's words, in seconds, minutes, or hours and minutes, see the review on #3263. */
function formatDuration(milliseconds: number, t: (key: string) => string): string {
  const seconds = Math.max(0, Math.round(milliseconds / 1000))
  if (seconds < 60) {
    return t('shell.studio.duration.seconds').replace('{count}', String(seconds))
  }
  const minutes = Math.round(seconds / 60)
  if (minutes < 60) {
    return t('shell.studio.duration.minutes').replace('{count}', String(minutes))
  }
  return t('shell.studio.duration.hours')
    .replace('{hours}', String(Math.floor(minutes / 60)))
    .replace('{minutes}', String(minutes % 60))
}
