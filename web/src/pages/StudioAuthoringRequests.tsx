import { Flex, Form, Input, Modal, Spin, Typography } from 'antd'
import { createContext, useContext, useEffect, useState, type HTMLAttributes, type ReactNode } from 'react'
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
  findPreviewLinks,
  previewAuthoringDraft,
  ServerUnreachableError,
  type StudioAuthoringRequest,
  type StudioAuthoringRequestDetail,
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
import { ActionBar } from './SaveBar'
import { StudioPageHeading } from './StudioPageHeading'
import { StudioProblemList } from './StudioProblems'
import { ResourceReference } from './ResourceReference'
import { visuallyHidden } from '../platform/visuallyHidden'
import { CheckOutlined, ClockCircleOutlined, CloseOutlined, LoadingOutlined, MinusOutlined } from '@ant-design/icons'
import { useDiagnosticItems } from './useDiagnosticItems'
import { StudioRequestStatus } from './StudioRequestStatus'
import { StudioTestResults } from './StudioTestResults'

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
  previewHost = null,
}: {
  application: string
  requestId: string
  userId: string | null
  /** The application's resources, which name the files of the draft's diff. */
  resources: StudioResourceItem[]
  /** Opens a resource at a path, from a problem or a failed scenario of the checks. */
  onOpen: (resourceId: string, path: string) => void
  /** Proposed: the preview host, where the author sees a ready draft at work; with none, no preview shows. */
  previewHost?: string | null
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
  // Proposed: what the feed has unfolded, see the review on #3263.
  const [planOpen, setPlanOpen] = useState(false)
  const [stepsOpen, setStepsOpen] = useState(false)
  const [checksOpen, setChecksOpen] = useState(false)
  const [changesOpen, setChangesOpen] = useState(false)
  const [dropOpen, setDropOpen] = useState(false)
  const [replyForm] = Form.useForm<{ comment: string }>()
  // Proposed: the preview of the ready draft, as a preview request, then the links to its sites.
  const [preview, setPreview] = useState<{ draft: string; request: StudioRequest; links: string[] }>()
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

  // A preview runs as a preview request; when it succeeds, the links to the application's sites show.
  const previewPending =
    preview !== undefined && (preview.request.status === 'queued' || preview.request.status === 'running')
  useFollowJob(previewPending ? `${application}/preview/${preview.request.id}/${generation}` : null, {
    read: (signal) => fetchStudioRequest(application, preview!.request.id, signal),
    ends: (next) => next.status !== 'queued' && next.status !== 'running',
    interval: 1000,
    waitFirst: true,
    onAnswer: (next) => setPreview((current) => current && { ...current, request: next }),
    onEnd: async (next, signal) => {
      const sites = resources.filter((resource) => resource.kind === 'site')
      const links = await findPreviewLinks(application, previewHost, sites, next, signal)
      setPreview((current) => current && { ...current, request: next, links })
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
      setDropOpen(false)
      replyForm.resetFields()
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
  const quoted = (text: string) => <div style={{ whiteSpace: 'pre-wrap' }}>{text}</div>
  // Proposed: one frame for every request, sent from Studio or made by an outside AI app, see the review on #3263.
  // Axis does the AI's part of any request whose author has connected their AI, whichever way it began.
  const waitsForApp =
    request.app !== null && !request.aiConnected && ['planning', 'working', 'fixing'].includes(request.state)
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
  const startPreview = async () => {
    setActionError(false)
    try {
      const started = await previewAuthoringDraft(application, draft!)
      setPreview({ draft: draft!, request: started, links: [] })
      // A preview that has already ended links its sites at once; one that runs is followed until it ends.
      if (started.status !== 'queued' && started.status !== 'running') {
        const sites = resources.filter((resource) => resource.kind === 'site')
        const links = await findPreviewLinks(application, previewHost, sites, started, new AbortController().signal)
        setPreview({ draft: draft!, request: started, links })
      }
    } catch (error) {
      if (error instanceof ServerUnreachableError) {
        lost()
      } else {
        setActionError(true)
      }
    }
  }
  const canDrop = mine && request.state !== 'accepted' && request.state !== 'dropped'
  const awaiting = request.state === 'awaitingApproval'
  const ready = request.state === 'ready'
  const failed = request.state === 'failed'
  const dropped = request.state === 'dropped'
  // An accept that met resources changed since the draft read them, one AXC0377 for each.
  const conflicts =
    accept !== null && accept.status === 'failed'
      ? accept.diagnostics.filter((diagnostic) => diagnostic.code === 'AXC0377')
      : []
  const changes = diff?.draft === draft ? diff?.changes : undefined
  const summaries = new Map(request.changes.map((change) => [change.file, change.summary]))
  const problems = request.checks?.diagnostics ?? []
  const working = ['working', 'checking', 'fixing', 'reviewing'].includes(request.state)
  const fixRound = request.round > 1 && working
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
  const openBlocking = (request.review?.findings ?? []).filter((finding) => finding.blocking).length

  // Proposed: the request as one feed read from the top, as an AI agent's task is, see the review on #3263: its plan,
  // then AI's work, then the result, each an item with its mark, and the author's reply at the foot.
  const workSteps = request.steps.filter((step) => !planKinds.has(step.kind))
  // The plan is done once AI works on it, whether or not the approval's time is known.
  const approved =
    request.state !== 'planning' &&
    !awaiting &&
    (request.approvedAt !== null ||
      workSteps.length > 0 ||
      ['working', 'checking', 'fixing', 'reviewing', 'ready', 'accepted'].includes(request.state))
  const hasWork = approved
  const hasResult = ((ready || failed) && request.checks !== null) || request.state === 'accepted'
  const fold = (open: boolean, toggle: () => void, show: string, hide: string, testId: string) => (
    <span>
      <AxisButton role="link" size="small" aria-expanded={open} onClick={toggle} data-testid={testId}>
        {t(open ? hide : show)}
      </AxisButton>
    </span>
  )
  const failure = (
    <AxisAlert
      kind="error"
      message={t('shell.studio.aiRequest.failed')}
      description={request.failure ?? undefined}
      data-testid="studio-ai-failure"
    />
  )
  const appWorks = waitsForApp && (
    <Typography.Text style={{ color: colour.textMuted }} data-testid="studio-ai-app-works">
      {t(mine ? 'shell.studio.aiRequest.appWorksMine' : 'shell.studio.aiRequest.appWorks').replaceAll(
        '{app}',
        request.app ?? '',
      )}
    </Typography.Text>
  )
  const nowLine = (words: ReactNode) => (
    <Flex align="center" gap={spacing.sm} wrap data-testid="studio-ai-now">
      <Spin size="small" />
      <Typography.Text style={{ color: colour.textMuted }}>{words}</Typography.Text>
    </Flex>
  )

  const planComments = request.steps.filter((step) => step.kind === 'planChange')
  const planItem = (
    <FeedItem
      testId="studio-ai-feed-plan"
      mark={
        request.state === 'planning' ? 'now' : awaiting ? 'waiting' : approved ? 'done' : failed ? 'stopped' : 'idle'
      }
      title={t('shell.studio.aiRequest.plan')}
      aside={
        awaiting
          ? t(mine ? 'shell.studio.aiRequest.feed.planWaits' : 'shell.studio.aiRequest.feed.planWaitsAuthor')
          : request.approvedAt !== null
            ? t('shell.studio.aiRequest.feed.planApproved').replace('{time}', sentTime(request.approvedAt))
            : undefined
      }
    >
      {/* The author's comments that asked for another plan, oldest first, so the plan reads with why it changed. */}
      {planComments.map((step, index) => (
        <Typography.Text key={index} style={{ color: colour.textMuted }}>
          {t('shell.studio.aiRequest.planChange').replace('{comment}', step.summary ?? '')}
        </Typography.Text>
      ))}
      {request.state === 'planning' && (waitsForApp ? appWorks : nowLine(t('shell.studio.aiRequest.planning')))}
      {failed && !approved && failure}
      {request.plan !== null &&
        (awaiting ? (
          <PlanView plan={request.plan} review={request.review} resources={resources} onOpen={onOpen} />
        ) : (
          <>
            {fold(
              planOpen,
              () => setPlanOpen(!planOpen),
              'shell.studio.aiRequest.feed.showPlan',
              'shell.studio.aiRequest.feed.hidePlan',
              'studio-ai-plan-fold',
            )}
            {planOpen && <PlanView plan={request.plan} review={request.review} resources={resources} onOpen={onOpen} />}
          </>
        ))}
    </FeedItem>
  )

  const workStart = workSteps[0]?.startedAt
  const workEnd = workSteps.at(-1)?.finishedAt ?? null
  const workItem = hasWork && (
    <FeedItem
      testId="studio-ai-feed-work"
      mark={working ? 'now' : failed ? 'stopped' : dropped ? 'idle' : 'done'}
      title={t('shell.studio.aiRequest.feed.work')}
      aside={
        working
          ? fixRound
            ? t('shell.studio.aiRequest.round').replace('{round}', String(request.round))
            : undefined
          : workStart !== undefined && workEnd !== null
            ? t('shell.studio.aiRequest.feed.workDone')
                .replace('{count}', String(workSteps.length))
                .replace('{duration}', formatDuration(Date.parse(workEnd) - Date.parse(workStart), t))
            : undefined
      }
    >
      {working &&
        (waitsForApp
          ? appWorks
          : following &&
            nowLine(
              running === undefined ? (
                t('shell.studio.aiRequest.feed.starting')
              ) : (
                <FilledText text={t('shell.studio.aiRequest.now')} parts={{ step: stepWords(running) }} />
              ),
            ))}
      {failed && approved && failure}
      {request.steps.length > 0 &&
        fold(
          stepsOpen,
          () => setStepsOpen(!stepsOpen),
          'shell.studio.aiRequest.feed.showSteps',
          'shell.studio.aiRequest.feed.hideSteps',
          'studio-ai-steps-fold',
        )}
      {stepsOpen && (
        <AxisTable<HistoryRow>
          columns={stepColumns}
          rowKey={(row) => row.key}
          dataSource={historyRows(request.steps)}
          pagination={false}
          toolbar={{}}
          emptyText=""
        />
      )}
    </FeedItem>
  )

  // What the draft changes, resource by resource, as people read them; the diff names a resource only the draft adds.
  const changedRows = request.changes.map((change) => {
    const item = resources.find((resource) => resource.file === change.file)
    const loaded = Array.isArray(changes) ? changes.find((entry) => entry.file === change.file) : undefined
    const titles = loaded?.titles ?? {}
    return {
      file: change.file,
      summary: change.summary,
      resourceId: item?.id ?? null,
      name: item === undefined ? (titles[locale] ?? titles.en ?? loaded?.name ?? null) : null,
      kind: item === undefined ? (loaded?.kind ?? null) : null,
    }
  })
  const resultItem = hasResult && (
    <FeedItem
      testId="studio-ai-feed-result"
      mark={ready ? 'waiting' : request.state === 'accepted' ? 'done' : 'idle'}
      title={t('shell.studio.aiRequest.feed.result')}
    >
      {/* Each changed resource once, as the plan's details and the review's findings list them, its summary under it. */}
      {changedRows.length > 0 && (
        <StudioProblemList
          testId="studio-ai-changed"
          items={changedRows.map((row) => ({
            lead: null,
            message: row.summary,
            resourceId: row.resourceId,
            file: row.file,
            path: '',
            name: row.name,
            kind: row.kind,
          }))}
          resources={resources}
          onSelect={onOpen}
        />
      )}
      {/* Proposed: the checks and the review as lines of one shape, see the review on #3263: each its name in bold and its
          verdict as a tag, in columns so the tags start together, its summary under it, then what the review found. */}
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
          marginTop: spacing.xs,
        }}
      >
        {checksLine !== null && (
          <>
            <Typography.Text strong>{t('shell.studio.aiRequest.checksName')}</Typography.Text>
            <StatusTag tone={checksLine.passed ? 'success' : 'danger'}>
              {t(checksLine.passed ? 'shell.studio.aiRequest.checks.passed' : 'shell.studio.aiRequest.checks.failed')}
            </StatusTag>
            <span style={{ gridColumn: '1 / -1' }}>
              <Typography.Text>{checksLine.text}</Typography.Text>{' '}
              <AxisButton
                role="link"
                size="small"
                aria-expanded={checksOpen}
                onClick={() => setChecksOpen(!checksOpen)}
                data-testid="studio-ai-checks-fold"
              >
                {t(checksOpen ? 'shell.studio.aiRequest.feed.hideChecks' : 'shell.studio.aiRequest.feed.showChecks')}
              </AxisButton>
            </span>
            {checksOpen && (
              <Flex vertical gap="small" style={{ gridColumn: '1 / -1' }} data-testid="studio-ai-check-details">
                {problems.length > 0 && (
                  <StudioProblemList items={diagnosticItems(problems)} resources={resources} onSelect={onOpen} />
                )}
                {request.checks !== null && (
                  <StudioTestResults request={request.checks} resources={resources} onSelect={onOpen} nested />
                )}
              </Flex>
            )}
          </>
        )}
        {/* Proposed: an outside app's ready request has no review when its author has no AI of their own. */}
        {request.review === null && request.app !== null && ready && (
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
        {request.review !== null && <ReviewFindings review={request.review} resources={resources} onOpen={onOpen} />}
      </div>
      {draft !== null && showsDiff && (
        <Flex align="center" gap={spacing.sm} wrap style={{ marginTop: spacing.xs }}>
          {/* Proposed: the author sees the draft at work on the preview host before accepting it, see the review on
              #3263. */}
          {ready && previewHost !== null && (
            <AxisButton
              role="secondary"
              disabled={previewPending}
              onClick={() => void startPreview()}
              data-testid="studio-ai-preview"
            >
              {t('shell.studio.aiRequest.feed.preview')}
            </AxisButton>
          )}
          {fold(
            changesOpen,
            () => setChangesOpen(!changesOpen),
            'shell.studio.aiRequest.feed.showChanges',
            'shell.studio.aiRequest.feed.hideChanges',
            'studio-ai-changes-fold',
          )}
        </Flex>
      )}
      {preview !== undefined && preview.draft === draft && (
        <Flex vertical gap={spacing.xs} data-testid="studio-ai-preview-status">
          <StudioRequestStatus request={preview.request} releaseNumber={null} />
          {preview.links.map((link) => (
            <a key={link} href={link} target="_blank" rel="noreferrer" data-testid="studio-ai-preview-link">
              {link}
            </a>
          ))}
        </Flex>
      )}
      {changesOpen &&
        (changes === undefined ? (
          <Spin />
        ) : changes === 'failed' ? (
          <AxisAlert kind="error" message={t('shell.studio.aiRequest.diffFailed')} />
        ) : (
          <div data-testid="studio-ai-changes" style={{ display: 'flex', flexDirection: 'column', gap: spacing.md }}>
            <StudioChanges changes={changes} resources={resources} summaries={summaries} />
          </div>
        ))}
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
    </FeedItem>
  )
  const droppedItem = dropped && (
    <FeedItem
      testId="studio-ai-feed-dropped"
      mark="idle"
      title={stateWords(request)}
      aside={request.finishedAt === null ? undefined : sentTime(request.finishedAt)}
    />
  )
  // Proposed: the author replies to the plan or to the ready draft at the foot of the feed, as a comment on a pull
  // request asks its agent for more, see the review on #3263.
  const replyKind = mine && awaiting ? 'change-plan' : mine && ready && !acceptPending ? 'request-changes' : null
  const asksChanges = replyKind === 'request-changes'

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
      {actionError && <AxisAlert kind="error" message={t('shell.studio.aiRequest.actionFailed')} />}
      <ol style={{ listStyle: 'none', margin: 0, padding: 0 }} data-testid="studio-ai-feed">
        {[planItem, workItem, resultItem, droppedItem]
          .filter((item) => item !== false)
          .map((item, index, items) => (
            <FeedLast.Provider key={index} value={index === items.length - 1}>
              {item}
            </FeedLast.Provider>
          ))}
      </ol>
      {replyKind !== null && (
        <Form
          form={replyForm}
          layout="vertical"
          requiredMark={requiredMark}
          onFinish={(values) => void act(replyKind, values.comment.trim())}
          data-testid="studio-ai-reply"
        >
          <AxisField
            name="comment"
            label={t(asksChanges ? 'shell.studio.aiRequest.changesComment' : 'shell.studio.aiRequest.comment')}
            help={t(asksChanges ? 'shell.studio.aiRequest.changesComment.help' : 'shell.studio.aiRequest.comment.help')}
            requiredMessage={t(
              asksChanges ? 'shell.studio.aiRequest.changesCommentRequired' : 'shell.studio.aiRequest.commentRequired',
            )}
          >
            <Input.TextArea
              aria-label={t(asksChanges ? 'shell.studio.aiRequest.changesComment' : 'shell.studio.aiRequest.comment')}
              autoSize={{ minRows: 2, maxRows: 8 }}
              maxLength={2000}
            />
          </AxisField>
          <AxisButton role="secondary" htmlType="submit" disabled={acting}>
            {t(asksChanges ? 'shell.studio.aiRequest.requestChanges.ok' : 'shell.studio.aiRequest.changePlan.ok')}
          </AxisButton>
        </Form>
      )}
      {/* Proposed: approving the plan sits in the bar at the foot of the window, as accepting does, see the review on
          #3263. */}
      {awaiting && mine && (
        <ActionBar testId="studio-ai-plan-bar">
          <span>{t('shell.studio.aiRequest.bar.awaiting')}</span>
          <AxisActionRow align="center">
            <AxisButton role="secondary" disabled={acting} onClick={() => setDropOpen(true)}>
              {t('shell.studio.aiRequest.drop')}
            </AxisButton>
            <AxisButton role="finish" disabled={acting} onClick={() => void act('approve')}>
              {t('shell.studio.aiRequest.approve')}
            </AxisButton>
          </AxisActionRow>
        </ActionBar>
      )}
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

/** Whether a `FeedItem` is the last of its feed, which draws no line down from its mark. */
const FeedLast = createContext(false)

/** How a feed item's mark reads: done, in progress, waiting for a person, stopped by a failure, or ended unused. */
type FeedMark = 'done' | 'now' | 'waiting' | 'stopped' | 'idle'

/**
 * Proposed: one item of a request's feed, see the review on #3263: its mark in a circle, a line down to the next item,
 * then its title in bold with what it says about itself muted after it, and its body under them.
 */
function FeedItem({
  testId,
  mark,
  title,
  aside,
  children,
}: {
  testId: string
  mark: FeedMark
  title: ReactNode
  aside?: ReactNode
  children?: ReactNode
}) {
  const { colour, size, spacing } = useDesignTokens()
  const last = useContext(FeedLast)
  const look = {
    done: { icon: <CheckOutlined />, edge: colour.primaryText, fill: 'transparent', ink: colour.primaryText },
    now: { icon: <LoadingOutlined />, edge: colour.primaryText, fill: 'transparent', ink: colour.primaryText },
    waiting: { icon: <ClockCircleOutlined />, edge: colour.warningText, fill: 'transparent', ink: colour.warningText },
    stopped: { icon: <CloseOutlined />, edge: colour.danger, fill: colour.danger, ink: colour.onPrimary },
    idle: { icon: <MinusOutlined />, edge: colour.textMuted, fill: 'transparent', ink: colour.textMuted },
  }[mark]
  return (
    <li data-testid={testId} data-mark={mark} style={{ display: 'flex', gap: spacing.sm }}>
      <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', flex: 'none' }}>
        <span
          aria-hidden
          style={{
            display: 'inline-flex',
            alignItems: 'center',
            justifyContent: 'center',
            width: size.smallControl,
            height: size.smallControl,
            borderRadius: '50%',
            border: `1px solid ${look.edge}`,
            background: look.fill,
            color: look.ink,
          }}
        >
          {look.icon}
        </span>
        {!last && (
          <span aria-hidden style={{ flex: 1, width: 1, marginBlock: spacing.xs / 2, background: colour.border }} />
        )}
      </div>
      <Flex vertical gap={spacing.xs} style={{ flex: 1, minWidth: 0, paddingBottom: last ? 0 : spacing.lg }}>
        <Flex align="center" gap={spacing.sm} wrap style={{ minHeight: size.smallControl }}>
          <Typography.Text strong>{title}</Typography.Text>
          {aside !== undefined && <Typography.Text style={{ color: colour.textMuted }}>{aside}</Typography.Text>}
        </Flex>
        {children}
      </Flex>
    </li>
  )
}

/** The kinds of step that write or change the plan, which the feed's plan item holds. */
const planKinds = new Set<StudioAuthoringStepKind>(['plan', 'planChange', 'propose_plan'])

/**
 * Proposed: a request's plan, written as a pull request's plan is, see the review on #3263: each a group named in bold,
 * What changes, Decisions to check, each choice with its reason, muted, Effects and risks, and How it will be checked,
 * then its details, resource by resource, folded until asked for. Its approval's actions sit in the page's bar.
 * Once a review ran, each step of What changes is its checklist item, a check mark when the draft does it and a cross
 * when it does not, with the resource where, as a pull request's description ticks its own list.
 */
function PlanView({
  plan,
  review,
  resources,
  onOpen,
}: {
  plan: StudioPlan
  review: StudioReview | null
  resources: readonly StudioResourceItem[]
  onOpen: (resourceId: string, path: string) => void
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const [details, setDetails] = useState(false)
  const bullets = (items: readonly ReactNode[]) => (
    <ul style={{ margin: 0, paddingInlineStart: spacing.lg }}>
      {items.map((item, index) => (
        <li key={index}>
          <Typography.Text>{item}</Typography.Text>
        </li>
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
    </Flex>
  )
}

/**
 * Proposed: the review of a request, see the review on #3263, as cells of the columns of the overview: "Review" in bold, its
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
  if (scenarios.length === 1) {
    return { text: t('shell.studio.aiRequest.checks.oneScenarioFails'), passed: false }
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
