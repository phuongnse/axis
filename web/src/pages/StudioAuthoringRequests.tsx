import { Flex, Form, Input, Modal, Spin, Typography } from 'antd'
import {
  createContext,
  useContext,
  useEffect,
  useState,
  type CSSProperties,
  type HTMLAttributes,
  type ReactNode,
} from 'react'
import { Link, useNavigate } from 'react-router'
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
  fetchAuthoringRequestDiff,
  fetchStudioRequest,
  findPreviewLinks,
  previewAuthoringDraft,
  sendAuthoringRequest,
  ServerUnreachableError,
  type StudioAuthoringRequest,
  type StudioAuthoringRequestDetail,
  type StudioAuthoringRequestStep,
  type StudioAuthoringStepKind,
  type StudioDiagnostic,
  type StudioPlan,
  type StudioReview,
  type StudioReviewFinding,
  type StudioReviewOutcome,
  type StudioRequest,
  type StudioResourceItem,
  type StudioRevisionChange,
  type StudioPreviewLink,
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
  needsActionFilter,
  useRequestStateWords,
} from './authoringRequests'
import { recordPageSize, tablePageSizes } from '../platform/tableQuery'
import { useTableView } from '../platform/useTableView'
import { StatusTag } from '../platform/StatusTag'
import { StudioChanges } from './StudioHistory'
import { ActionBar } from './SaveBar'
import { StudioPageHeading } from './StudioPageHeading'
import { useBulletList } from '../platform/bulletList'
import { StudioProblemList } from './StudioProblems'
import { ResourceReference } from './ResourceReference'
import {
  BulbOutlined,
  CheckCircleOutlined,
  CheckOutlined,
  ClockCircleOutlined,
  CloseCircleOutlined,
  CloseOutlined,
  ExclamationCircleOutlined,
  LoadingOutlined,
  MinusCircleOutlined,
  MinusOutlined,
} from '@ant-design/icons'
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
  const [reload, setReload] = useState(0)
  const { view, setView } = useTableView(requestFields, recordPageSize, [], true, true, true)
  // Proposed: the Needs action view is only its filter, see the review on #3263.
  const needsAction =
    userId !== null &&
    view.filter !== undefined &&
    JSON.stringify(view.filter) === JSON.stringify(needsActionFilter(userId))

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
  }, [application, generation, lost, reload])

  const who = (row: StudioAuthoringRequest) =>
    row.author === userId ? t('shell.studio.aiRequests.you') : (row.authorName ?? t('shell.studio.ai.someone'))
  // A fix round reads as working, so the filter offers the states as the tags name them.
  const shownState = (row: StudioAuthoringRequest) => (row.state === 'fixing' ? 'working' : row.state)
  const rows = Array.isArray(requests) ? requests : []
  const people = Array.isArray(requests)
    ? [...new Map(requests.map((row) => [row.author, who(row)])).entries()].map(([value, label]) => ({ value, label }))
    : []

  const columns: AxisColumn<StudioAuthoringRequest>[] = [
    {
      key: 'request',
      kind: 'freeText',
      title: t('shell.studio.aiRequests.request'),
      searchText: (row) => row.text,
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
      sortValue: (row) => stateOrder.indexOf(shownState(row)),
      filter: {
        kind: 'enum',
        value: shownState,
        options: stateOrder.map((state) => ({ value: state, label: t(`shell.studio.ai.state.${state}`) })),
      },
      render: (_, row) => <StatusTag tone={requestStateTone(row.state)}>{stateWords(row)}</StatusTag>,
    },
    {
      key: 'who',
      kind: 'freeText',
      title: t('shell.studio.aiRequests.who'),
      sortValue: who,
      filter: { kind: 'enum', value: (row) => row.author, options: people },
      render: (_, row) => who(row),
    },
    {
      key: 'when',
      kind: 'fixed',
      title: t('shell.studio.aiRequests.when'),
      sortValue: (row) => row.sentAt,
      filter: { kind: 'date', value: (row) => row.sentAt },
      render: (_, row) => sentTime(row.sentAt),
    },
  ]

  return (
    <Flex vertical gap="middle">
      <StudioPageHeading title={t('shell.studio.aiRequests')} />
      {/* Proposed: every table whose rows grow has the whole toolbar, see the step 20 rules of the review on #3263:
          search, filter, columns, sort, paging and refresh, each in the address. The sidebar's Needs action is a view
          of it, its filter in the address. */}
      <AxisTable<StudioAuthoringRequest>
        testId="studio-ai-requests"
        columns={columns}
        rowKey={(row) => row.id}
        dataSource={rows}
        loading={requests === undefined}
        emptyText={t(needsAction ? 'shell.studio.aiRequests.noneWaiting' : 'shell.studio.aiRequests.empty')}
        view={{
          query: view,
          onChange: setView,
          pageSizes: tablePageSizes(recordPageSize),
          search: true,
          columns: true,
        }}
        toolbar={{
          onRefresh: () => {
            setRequests(undefined)
            setReload((value) => value + 1)
          },
        }}
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

/** The fields of the AI requests table, which the address may sort and filter on. */
const requestFields = [
  { name: 'request', type: 'text' },
  { name: 'state', type: 'text' },
  { name: 'who', type: 'text' },
  { name: 'when', type: 'date-time' },
] as const

/** The states in the order a request goes through them, as the State column sorts and filters them. */
const stateOrder = [
  'planning',
  'awaitingApproval',
  'working',
  'checking',
  'reviewing',
  'ready',
  'accepted',
  'failed',
  'dropped',
] as const

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
  const navigate = useNavigate()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const { colour, size, spacing } = useDesignTokens()
  const bulletList = useBulletList()
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
  const [replyOpen, setReplyOpen] = useState(false)
  const [anywayOpen, setAnywayOpen] = useState(false)
  const [stepsOpen, setStepsOpen] = useState(false)
  const [checksOpen, setChecksOpen] = useState(false)
  const [dropOpen, setDropOpen] = useState(false)
  const [replyForm] = Form.useForm<{ comment: string }>()
  // Proposed: the preview of the ready draft, as a preview request, then the links to its sites.
  const [preview, setPreview] = useState<{ draft: string; request: StudioRequest; links: StudioPreviewLink[] }>()
  const [diff, setDiff] = useState<{ key: string; changes: StudioRevisionChange[] | 'failed' }>()
  const [accepting, setAccepting] = useState<{ id: string; accept: StudioRequest }>()

  const request = loaded?.id === requestId ? loaded.request : undefined
  const following = request !== undefined && request !== null && !settled.includes(request.state)
  const draft = request?.draft ?? null
  const accept = accepting?.id === requestId ? accepting.accept : (request?.accept ?? null)
  const acceptPending = accept !== null && (accept.status === 'queued' || accept.status === 'running')

  // Proposed: the request's diff, its draft's while it is ready or failed and its accept's once accepted, read again
  // when the request moves on, see the review on #3263.
  const showsDiff =
    request?.state === 'accepted' || ((request?.state === 'ready' || request?.state === 'failed') && draft !== null)
  const diffKey = showsDiff ? `${requestId}/${request?.state}/${draft ?? ''}` : null
  useEffect(() => {
    if (diffKey === null) {
      return
    }
    const controller = new AbortController()
    fetchAuthoringRequestDiff(application, requestId, controller.signal).then(
      (changes) => setDiff({ key: diffKey, changes }),
      (error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        if (error instanceof ServerUnreachableError) {
          lost()
          return
        }
        setDiff({ key: diffKey, changes: 'failed' })
      },
    )
    return () => controller.abort()
  }, [application, requestId, diffKey, generation, lost])

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
      setReplyOpen(false)
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
    // A step that runs reads as what it does now, "Checking the draft", and a finished one as what it did.
    const words = step.finishedAt === null ? 'stepNow' : 'step'
    switch (step.kind) {
      case 'planChange':
      case 'changeRequest':
        return t(`shell.studio.aiRequest.${step.kind}`).replace('{comment}', step.summary ?? '')
      case 'write_resource':
      case 'write_layer':
      case 'delete_resource':
        return (
          <FilledText
            text={t(`shell.studio.aiRequest.${words}.${step.kind === 'delete_resource' ? 'delete' : 'write'}`)}
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
        return t(`shell.studio.aiRequest.${words}.${stepKeys[step.kind]}`)
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
  // Proposed: a failed request's author sends its words again as a new request and goes to it, see the review on #3263.
  const sendAgain = async () => {
    setActing(true)
    setActionError(false)
    try {
      const next = await sendAuthoringRequest(application, request.text, request.autoAccept)
      navigate(authoringRequestAddress(application, next.id))
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
  const changes = diff !== undefined && diff.key === diffKey ? diff.changes : undefined
  const summaries = new Map(request.changes.map((change) => [change.file, change.summary]))
  // Each resource only the draft adds, by its file, named as its diff names it, so a scenario it adds reads by its name.
  const drafted = new Map(
    (Array.isArray(changes) ? changes : [])
      .filter((change) => change.change === 'added' && change.kind !== null)
      .map((change) => [
        change.file,
        { name: change.titles?.[locale] ?? change.titles?.en ?? change.name ?? change.file, kind: change.kind! },
      ]),
  )
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
  // Proposed: what an item folds opens from a link at the end of the line that sums it up, the plan's summary, the
  // work's steps, the checks' result, a space after its words, so every fold of the feed reads the same way.
  const fold = (
    open: boolean,
    toggle: () => void,
    show: string,
    hide: string,
    testId: string,
    lead?: ReactNode,
    style?: CSSProperties,
  ) => (
    <span style={style}>
      {lead !== undefined && <>{lead} </>}
      <AxisButton role="link" size="small" aria-expanded={open} onClick={toggle} data-testid={testId}>
        {t(open ? hide : show)}
      </AxisButton>
    </span>
  )
  // Proposed: a failure is one line, as every error of Studio is, its words and why in one message, see the review on
  // #3263.
  const failure = (
    <AxisAlert
      kind="error"
      message={[t('shell.studio.aiRequest.failed'), request.failure].filter((part) => part).join(' ')}
      data-testid="studio-ai-failure"
    />
  )
  // What AI does now, muted after a spinner, then what else its line holds, such as the fold of the steps.
  const nowLine = (words: ReactNode, rest?: (lead: ReactNode) => ReactNode) => {
    const lead = <Typography.Text style={{ color: colour.textMuted }}>{words}</Typography.Text>
    return (
      <Flex align="center" gap={spacing.sm} wrap data-testid="studio-ai-now">
        <Spin size="small" />
        {rest === undefined ? lead : rest(lead)}
      </Flex>
    )
  }

  const planComments = request.steps.filter((step) => step.kind === 'planChange')
  const planItem = (
    <FeedItem
      testId="studio-ai-feed-plan"
      mark={
        request.state === 'planning' ? 'now' : awaiting ? 'waiting' : approved ? 'done' : failed ? 'stopped' : 'idle'
      }
      title={t('shell.studio.aiRequest.plan')}
      // Its blocks, the summary, the plan's groups and the fold, are `spacing-md` apart, as the result's are.
      gap={spacing.md}
      // Proposed: beside its title only when it was approved; that it waits, its mark and the bar say, see the review on
      // #3263.
      aside={
        request.approvedAt !== null
          ? t('shell.studio.aiRequest.feed.planApproved').replace('{time}', sentTime(request.approvedAt))
          : undefined
      }
    >
      {/* The author's comments that asked for another plan, oldest first, so the plan reads with why it changed. */}
      {planComments.map((step, index) => (
        <Typography.Text key={index}>
          {t('shell.studio.aiRequest.planChange').replace('{comment}', step.summary ?? '')}
        </Typography.Text>
      ))}
      {request.state === 'planning' && !waitsForApp && nowLine(t('shell.studio.aiRequest.planning'))}
      {failed && !approved && failure}
      {/* Proposed: the plan's summary shows even while the plan is folded, so the feed says what it does, with the fold
          right after it, as See the checks follows the checks' summary. */}
      {request.plan !== null && (
        <span data-testid="studio-ai-plan-summary">
          {awaiting ? (
            <Typography.Text>{request.plan.summary}</Typography.Text>
          ) : (
            fold(
              planOpen,
              () => setPlanOpen(!planOpen),
              'shell.studio.aiRequest.feed.showPlan',
              'shell.studio.aiRequest.feed.hidePlan',
              'studio-ai-plan-fold',
              request.plan.summary !== '' ? <Typography.Text>{request.plan.summary}</Typography.Text> : undefined,
            )
          )}
        </span>
      )}
      {request.plan !== null && (awaiting || planOpen) && (
        <PlanView plan={request.plan} resources={resources} onOpen={onOpen} />
      )}
    </FeedItem>
  )

  const workStart = workSteps[0]?.startedAt
  const workEnd = workSteps.at(-1)?.finishedAt ?? null
  const workDone =
    !working && workStart !== undefined && workEnd !== null ? (
      <Typography.Text>
        {t('shell.studio.aiRequest.feed.workDone')
          .replace('{count}', String(workSteps.length))
          .replace('{duration}', formatDuration(Date.parse(workEnd) - Date.parse(workStart), t))}
      </Typography.Text>
    ) : undefined
  const stepsFold = (lead: ReactNode) =>
    fold(
      stepsOpen,
      () => setStepsOpen(!stepsOpen),
      'shell.studio.aiRequest.feed.showSteps',
      'shell.studio.aiRequest.feed.hideSteps',
      'studio-ai-steps-fold',
      lead,
    )
  const workItem = hasWork && (
    <FeedItem
      testId="studio-ai-feed-work"
      mark={working ? 'now' : failed ? 'stopped' : dropped ? 'idle' : 'done'}
      title={t('shell.studio.aiRequest.feed.work')}
      // Proposed: beside the title, how the item stands, as the plan's approval does; what it did reads under it.
      aside={
        working && fixRound ? t('shell.studio.aiRequest.round').replace('{round}', String(request.round)) : undefined
      }
    >
      {failed && approved && failure}
      {/* Proposed: under the title, what AI does now or what it did, with the fold of its steps after it on its line,
          as the plan's summary and the checks' result have theirs. */}
      {working && !waitsForApp && following
        ? nowLine(
            running === undefined ? t('shell.studio.aiRequest.feed.starting') : stepWords(running),
            (lead) => request.steps.length > 0 && stepsFold(lead),
          )
        : request.steps.length > 0 && stepsFold(workDone)}
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

  // Proposed: a line's summary reads under it across the columns, flush with it, as a heading's paragraph does, and what
  // the summary opens or lists sits inside the line, indented by spacing-md, as layout shows structure, see the review on
  // #3263.
  const under: CSSProperties = { gridColumn: '1 / -1' }
  const inside: CSSProperties = { ...under, paddingInlineStart: spacing.md }
  const resultItem = hasResult && (
    <FeedItem
      testId="studio-ai-feed-result"
      mark={ready ? 'waiting' : request.state === 'accepted' ? 'done' : 'idle'}
      title={t('shell.studio.aiRequest.feed.result')}
      // Its blocks, the checks and the review, what changes, and the actions, are `spacing-md` apart.
      gap={spacing.md}
    >
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
        }}
      >
        {checksLine !== null && (
          <>
            <Typography.Text strong>{t('shell.studio.aiRequest.checksName')}</Typography.Text>
            <StatusTag tone={checksLine.passed ? 'success' : 'danger'}>
              {t(checksLine.passed ? 'shell.studio.aiRequest.checks.passed' : 'shell.studio.aiRequest.checks.failed')}
            </StatusTag>
            {fold(
              checksOpen,
              () => setChecksOpen(!checksOpen),
              'shell.studio.aiRequest.feed.showChecks',
              'shell.studio.aiRequest.feed.hideChecks',
              'studio-ai-checks-fold',
              <Typography.Text>{checksLine.text}</Typography.Text>,
              under,
            )}
            {checksOpen && (
              <Flex vertical gap="small" style={inside} data-testid="studio-ai-check-details">
                {problems.length > 0 && (
                  <StudioProblemList items={diagnosticItems(problems)} resources={resources} onSelect={onOpen} />
                )}
                {request.checks !== null && (
                  <StudioTestResults
                    request={request.checks}
                    resources={resources}
                    drafted={drafted}
                    onSelect={onOpen}
                    nested
                  />
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
            <Typography.Text style={under}>
              {t(mine ? 'shell.studio.aiRequest.notReviewedMine' : 'shell.studio.aiRequest.notReviewed')}
            </Typography.Text>
          </div>
        )}
        {/* The review runs only once the checks pass, so a request that failed before it has no findings. */}
        {request.review !== null && <ReviewFindings review={request.review} resources={resources} onOpen={onOpen} />}
      </div>
      {/* Proposed: what the request changes is its diff, after the checks and the review, which sum it up, each resource a
          block that starts folded under its name, kind and change, with the summary its request wrote, and opens to its
          JSON diff, see the review on #3263. */}
      {showsDiff &&
        (changes === undefined ? (
          <Spin />
        ) : changes === 'failed' ? (
          <AxisAlert kind="error" message={t('shell.studio.aiRequest.diffFailed')} />
        ) : (
          changes.length > 0 && (
            <Flex vertical gap={spacing.xs} data-testid="studio-ai-changes">
              <StudioChanges changes={changes} resources={resources} summaries={summaries} folded />
            </Flex>
          )
        ))}
      {/* Proposed: the author sees the draft at work on the preview host before accepting it, see the review on #3263. */}
      {ready && draft !== null && previewHost !== null && (
        <span>
          <AxisButton
            role="secondary"
            disabled={previewPending}
            onClick={() => void startPreview()}
            data-testid="studio-ai-preview"
          >
            {t('shell.studio.aiRequest.feed.preview')}
          </AxisButton>
        </span>
      )}
      {preview !== undefined && preview.draft === draft && (
        <div data-testid="studio-ai-preview-status">
          <StudioRequestStatus request={preview.request} releaseNumber={null} links={preview.links} />
        </div>
      )}
      {conflicts.length > 0 ? (
        <AxisAlert
          kind="warning"
          data-testid="studio-ai-conflict"
          message={t('shell.studio.aiRequest.conflict')}
          description={
            <ul style={bulletList}>
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
  // Proposed: the author replies to the plan or to the ready draft under the feed, as a comment on a pull request asks
  // its agent for more, see the review on #3263; it opens from its link, so the bar keeps the one main action.
  const replyKind =
    mine && awaiting
      ? 'change-plan'
      : mine && ready && !acceptPending && conflicts.length === 0
        ? 'request-changes'
        : null
  const asksChanges = replyKind === 'request-changes'
  const questions = awaiting ? (request.plan?.questions.length ?? 0) : 0
  // The reply answers the plan's questions while it has some.
  const replyLabel = asksChanges
    ? 'shell.studio.aiRequest.changesComment'
    : questions > 0
      ? 'shell.studio.aiRequest.answers'
      : 'shell.studio.aiRequest.comment'
  const blockingTexts = (request.review?.findings ?? [])
    .filter((finding) => finding.kind === 'blocking')
    .map((finding) => finding.text)
  const button = (key: string, role: 'secondary' | 'finish', words: string, onClick: () => void) => (
    <AxisButton key={key} role={role} disabled={acting} onClick={onClick} data-testid={`studio-ai-bar-${key}`}>
      {words}
    </AxisButton>
  )
  const dropButton = canDrop
    ? button('drop', 'secondary', t('shell.studio.aiRequest.drop'), () => setDropOpen(true))
    : null
  // Proposed: asking for another plan or other changes is an action of the bar, secondary beside the main one, which
  // opens the reply under the feed, see the review on #3263. While the plan has questions, Answer… opens it.
  const replyButton =
    replyKind !== null && questions === 0
      ? button(
          'reply',
          'secondary',
          t(asksChanges ? 'shell.studio.aiRequest.askOther' : 'shell.studio.aiRequest.changePlan'),
          () => setReplyOpen(true),
        )
      : null
  // Proposed: the bar at the foot of the window guides in every state, see the review on #3263: one sentence of what
  // comes next in plain words, then at most one main action for it, so no one needs to read how the page works.
  const guide = ((): { words: string; actions: ReactNode[] } => {
    if (acceptPending) {
      return { words: t('shell.studio.aiRequest.stepNow.accept'), actions: [] }
    }
    switch (request.state) {
      case 'planning':
      case 'working':
      case 'checking':
      case 'fixing':
      case 'reviewing':
        return {
          words: waitsForApp
            ? t(mine ? 'shell.studio.aiRequest.appWorksMine' : 'shell.studio.aiRequest.appWorks').replaceAll(
                '{app}',
                request.app ?? '',
              )
            : t(
                request.state === 'planning'
                  ? 'shell.studio.aiRequest.guide.planning'
                  : 'shell.studio.aiRequest.guide.working',
              ),
          actions: [dropButton],
        }
      case 'awaitingApproval':
        // Proposed: a plan with questions waits on its author's answers before it can be approved.
        if (mine && questions > 0) {
          return {
            words: t(
              questions === 1 ? 'shell.studio.aiRequest.guide.questionOne' : 'shell.studio.aiRequest.guide.questions',
            ).replace('{count}', String(questions)),
            actions: [
              dropButton,
              button('answer', 'finish', t('shell.studio.aiRequest.answer'), () => setReplyOpen(true)),
            ],
          }
        }
        return mine
          ? {
              words: t('shell.studio.aiRequest.guide.awaiting'),
              actions: [
                dropButton,
                replyButton,
                button('approve', 'finish', t('shell.studio.aiRequest.approve'), () => void act('approve')),
              ],
            }
          : { words: t('shell.studio.aiRequest.guide.awaitingOther'), actions: [] }
      case 'ready':
        if (!mine) {
          return { words: t('shell.studio.aiRequest.guide.readyOther'), actions: [] }
        }
        if (conflicts.length > 0) {
          return {
            words: t('shell.studio.aiRequest.guide.conflict'),
            actions: [
              dropButton,
              button('retry', 'finish', t('shell.studio.aiRequest.retry'), () => void act('retry')),
            ],
          }
        }
        if (blockingTexts.length > 0) {
          return {
            words: t(
              blockingTexts.length === 1
                ? 'shell.studio.aiRequest.guide.mustChangeOne'
                : 'shell.studio.aiRequest.guide.mustChange',
            ).replace('{count}', String(blockingTexts.length)),
            actions: [
              dropButton,
              replyButton,
              // Proposed: accepting past an open must-change finding asks first, and lists what it leaves out, as
              // merging past a required review does, see the review on #3263.
              button('accept', 'secondary', t('shell.studio.aiRequest.acceptAnyway'), () => setAnywayOpen(true)),
              // The review's must-change findings become the comment that asks AI for changes.
              button(
                'fix',
                'finish',
                t('shell.studio.aiRequest.fixIt'),
                () =>
                  void act(
                    'request-changes',
                    [t('shell.studio.aiRequest.fixComment'), ...blockingTexts.map((text) => `- ${text}`)].join('\n'),
                  ),
              ),
            ],
          }
        }
        return {
          words: t('shell.studio.aiRequest.guide.passed'),
          actions: [
            dropButton,
            replyButton,
            button('accept', 'finish', t('shell.studio.aiRequest.accept'), () => void startAccept()),
          ],
        }
      case 'failed':
        return mine
          ? {
              words: t('shell.studio.aiRequest.guide.failed'),
              actions: [
                dropButton,
                button('send-again', 'finish', t('shell.studio.aiRequest.sendAgain'), () => void sendAgain()),
              ],
            }
          : { words: t('shell.studio.aiRequest.failed'), actions: [] }
      case 'accepted':
        return { words: t('shell.studio.aiRequest.guide.accepted'), actions: [] }
      case 'dropped':
        return { words: t('shell.studio.aiRequest.guide.dropped'), actions: [] }
    }
  })()

  return (
    <Flex vertical gap="middle" data-testid="studio-ai-request">
      {/* Proposed: the request's name as the heading with its state beside it, then, close under it, its words and who
          sent it with the AIs that made it, see the review on #3263. */}
      <Flex vertical gap={spacing.xs} data-testid="studio-ai-asked">
        <StudioPageHeading
          title={requestTitle(request)}
          tags={<StatusTag tone={requestStateTone(request.state)}>{stateWords(request)}</StatusTag>}
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
      {/* Proposed: asking for another plan, answering the plan's questions and asking for other changes each open a
          dialog, as Drop… and Accept anyway… do, see the review on #3263. */}
      <Modal
        open={replyKind !== null && replyOpen}
        width={size.dialog.medium}
        title={t(
          asksChanges
            ? 'shell.studio.aiRequest.reply.askOther'
            : questions > 0
              ? 'shell.studio.aiRequest.reply.answer'
              : 'shell.studio.aiRequest.reply.changePlan',
        )}
        okText={t(asksChanges ? 'shell.studio.aiRequest.requestChanges.ok' : 'shell.studio.aiRequest.changePlan.ok')}
        cancelText={t('shell.studio.cancel')}
        confirmLoading={acting}
        onOk={() => replyForm.submit()}
        onCancel={() => setReplyOpen(false)}
        destroyOnHidden
      >
        <Form
          form={replyForm}
          layout="vertical"
          requiredMark={requiredMark}
          onFinish={(values) => replyKind !== null && void act(replyKind, values.comment.trim())}
          data-testid="studio-ai-reply"
        >
          <AxisField
            name="comment"
            label={t(replyLabel)}
            help={t(`${replyLabel}.help`)}
            requiredMessage={t(
              asksChanges ? 'shell.studio.aiRequest.changesCommentRequired' : 'shell.studio.aiRequest.commentRequired',
            )}
          >
            <Input.TextArea
              aria-label={t(replyLabel)}
              autoSize={{ minRows: 3, maxRows: 8 }}
              maxLength={2000}
              autoFocus
            />
          </AxisField>
        </Form>
      </Modal>
      <ActionBar testId="studio-ai-bar">
        <span data-testid="studio-ai-guide">{guide.words}</span>
        {guide.actions.some((action) => action !== null) && (
          <AxisActionRow align="center" fold>
            {guide.actions}
          </AxisActionRow>
        )}
      </ActionBar>
      <Modal
        open={anywayOpen}
        width={size.dialog.medium}
        title={t('shell.studio.aiRequest.acceptAnyway.title')}
        okText={t('shell.studio.aiRequest.acceptAnyway.ok')}
        cancelText={t('shell.studio.cancel')}
        confirmLoading={acting}
        onOk={() => {
          setAnywayOpen(false)
          void startAccept()
        }}
        onCancel={() => setAnywayOpen(false)}
      >
        <Flex vertical gap={spacing.md}>
          <Typography.Text>{t('shell.studio.aiRequest.acceptAnyway.text')}</Typography.Text>
          {/* The blocking findings it leaves out, listed as the review lists them; a suggestion asks for no change. */}
          <FindingList
            testId="studio-ai-anyway-findings"
            only="blocking"
            findings={request.review?.findings ?? []}
            resources={resources}
            onOpen={(resourceId, path) => {
              setAnywayOpen(false)
              onOpen(resourceId, path)
            }}
          />
        </Flex>
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
  gap,
  children,
}: {
  testId: string
  mark: FeedMark
  title: ReactNode
  aside?: ReactNode
  /** The gap between the blocks of its body, `spacing-xs` unless it holds blocks of different kinds. */
  gap?: number
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
        {children !== undefined && (
          <Flex vertical gap={gap ?? spacing.xs}>
            {children}
          </Flex>
        )}
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
 */
function PlanView({
  plan,
  resources,
  onOpen,
}: {
  plan: StudioPlan
  resources: readonly StudioResourceItem[]
  onOpen: (resourceId: string, path: string) => void
}) {
  const t = useText()
  const { spacing } = useDesignTokens()
  const bulletList = useBulletList()
  const [details, setDetails] = useState(false)
  const bullets = (items: readonly ReactNode[]) => (
    <ul style={bulletList}>
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
      {/* What AI cannot decide alone comes first, as the plan waits on its answers, under a heading as every group's. */}
      {plan.questions.length > 0 && (
        <Flex vertical gap={spacing.xs} data-testid="studio-ai-plan-questions">
          <Typography.Text strong>{t('shell.studio.aiRequest.plan.questions')}</Typography.Text>
          {bullets(plan.questions)}
        </Flex>
      )}
      {group('shell.studio.aiRequest.plan.changes', 'studio-ai-plan-changes', plan.changes)}
      {group(
        'shell.studio.aiRequest.decisions',
        'studio-ai-plan-decisions',
        // Proposed: each choice, then why, a sentence of its own in the text colour, as words a person reads to decide
        // are never muted, see the step 20 rules of the review on #3263.
        plan.decisions.map(({ decision, reason }) => `${decision} ${reason}`),
      )}
      {group('shell.studio.aiRequest.plan.risks', 'studio-ai-plan-risks', plan.risks)}
      {group('shell.studio.aiRequest.plan.checks', 'studio-ai-plan-checks', plan.checks)}
      {group(
        'shell.studio.aiRequest.plan.checklist',
        'studio-ai-plan-checklist',
        // Proposed: each item of Axis's checklist, a statement that can be met or not, then how the plan meets it, as a
        // decision reads with its reason.
        plan.checklist.map(({ item, answer }) => `${item} ${answer}`),
      )}
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
 * Proposed: the review of a request, see the review on #3263, as cells of the columns of the result: "Review" in bold,
 * its verdict as a `StatusTag`, "Changes requested" while a finding is blocking and "No changes requested" otherwise,
 * never "Approved", as only its author approves, by accepting; its summary, a sentence or two, reads under them across
 * the columns, flush with them. What it found follows inside the line, `spacing-md` apart: "Things to look at", its
 * findings in one list; then the plan's "Acceptance criteria" and Axis's "Checklist", each item with how the review
 * found it. A group with nothing in it does not show.
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
  const { spacing } = useDesignTokens()
  const under: CSSProperties = { gridColumn: '1 / -1' }
  const inside: CSSProperties = { ...under, paddingInlineStart: spacing.md }
  const blocking = review.findings.filter((finding) => finding.kind === 'blocking')
  const found = review.findings.length > 0 || review.criteria.length > 0 || review.checklist.length > 0
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
      {/* Its tag closes the line, and its summary, a sentence or two, reads under it across the columns, flush with it. */}
      <Typography.Text style={under}>{review.summary}</Typography.Text>
      {found && (
        // Proposed: what it found follows the summary `spacing-sm` below it, the grid's `spacing-xs` and this margin,
        // nearer than the review's line is to the checks' and farther than a heading is from its list.
        <Flex vertical gap={spacing.md} style={{ ...inside, marginTop: spacing.sm - spacing.xs }}>
          {review.findings.length > 0 && (
            <Flex vertical gap={spacing.xs} data-testid="studio-ai-findings">
              <Typography.Text strong>{t('shell.studio.aiRequest.review.thingsToLookAt')}</Typography.Text>
              <FindingList findings={review.findings} resources={resources} onOpen={onOpen} />
            </Flex>
          )}
          <ReviewOutcomes
            heading="shell.studio.aiRequest.plan.checks"
            items={review.criteria}
            testId="studio-ai-review-criteria"
          />
          <ReviewOutcomes
            heading="shell.studio.aiRequest.plan.checklist"
            items={review.checklist}
            testId="studio-ai-review-checklist"
          />
        </Flex>
      )}
    </div>
  )
}

/**
 * Proposed: what leads an item of what a review found, see the review on #3263: an icon in the colour of what it asks
 * of the reader, which says its kind or its outcome at a glance, then its word and a colon in the text colour, as
 * docs/reference/actions-and-controls.md#buttons lets an icon go with text for a status or a severity. A finding is
 * Blocking, an exclamation mark in dangerText, or Suggestion, a bulb in warningText; a criterion or a checklist item is
 * Met, a check in successText, Not met, a cross in dangerText, or Skipped, a dash in textMuted. The icon is hidden
 * from assistive technology, which reads the word. The icons are outlined, as the feed's marks are.
 */
function ReviewMark({ mark, words }: { mark: ReviewMarkKind; words: string }) {
  const { colour, spacing } = useDesignTokens()
  const look = {
    blocking: { icon: <ExclamationCircleOutlined />, ink: colour.dangerText },
    suggestion: { icon: <BulbOutlined />, ink: colour.warningText },
    met: { icon: <CheckCircleOutlined />, ink: colour.successText },
    notMet: { icon: <CloseCircleOutlined />, ink: colour.dangerText },
    skipped: { icon: <MinusCircleOutlined />, ink: colour.textMuted },
  }[mark]
  return (
    <span data-mark={mark} style={{ whiteSpace: 'nowrap' }}>
      <span aria-hidden style={{ color: look.ink, marginInlineEnd: spacing.xs }}>
        {look.icon}
      </span>
      {words}:
    </span>
  )
}

type ReviewMarkKind = StudioReviewFinding['kind'] | StudioReviewOutcome['outcome']

/**
 * Proposed: how the review found each acceptance criterion of the plan or each item of Axis's checklist, see the review
 * on #3263: under its heading in bold, each item led by its outcome as a `ReviewMark`, "Met:", "Not met:" or "Skipped:",
 * as a finding is led by its kind, then its words, with the review's short why after them on their line for one not met
 * or skipped, in the text colour, as a decision of the plan reads with its reason. Each item is a bullet, the mark after it, as
 * every list of sentences is.
 */
function ReviewOutcomes({
  heading,
  items,
  testId,
}: {
  heading: string
  items: readonly StudioReviewOutcome[]
  testId: string
}) {
  const t = useText()
  const { spacing } = useDesignTokens()
  const bulletList = useBulletList()
  if (items.length === 0) {
    return null
  }
  return (
    <Flex vertical gap={spacing.xs} data-testid={testId}>
      <Typography.Text strong>{t(heading)}</Typography.Text>
      <ul style={bulletList}>
        {items.map((item, index) => (
          <li key={index} data-outcome={item.outcome}>
            <ReviewMark mark={item.outcome} words={t(`shell.studio.aiRequest.review.outcome.${item.outcome}`)} />{' '}
            <Typography.Text>{item.text}</Typography.Text>
            {item.note !== null && <> {item.note}</>}
          </li>
        ))}
      </ul>
    </Flex>
  )
}

/**
 * Proposed: a review's findings in one flat list, see the review on #3263, each a bullet led by its kind as a
 * `ReviewMark`, "Blocking:" or "Suggestion:", then its words and the resource it is about at the end of its line, as a
 * `ResourceReference`, its title the link that opens it at the finding's path. The blocking ones come first, so a glance
 * tells what holds the draft back. The review's result shows them all, and the Accept anyway dialog the blocking ones
 * alone.
 */
function FindingList({
  findings,
  resources,
  onOpen,
  testId,
  only,
}: {
  findings: readonly StudioReviewFinding[]
  resources: readonly StudioResourceItem[]
  onOpen: (resourceId: string, path: string) => void
  testId?: string
  /** Shows the blocking findings alone. */
  only?: 'blocking'
}) {
  const t = useText()
  const bulletList = useBulletList()
  const listed = [
    ...findings.filter((finding) => finding.kind === 'blocking'),
    ...(only === undefined ? findings.filter((finding) => finding.kind !== 'blocking') : []),
  ]
  return (
    <ul style={bulletList} data-testid={testId}>
      {listed.map((finding, index) => (
        <li key={index} data-testid="studio-ai-finding" data-kind={finding.kind}>
          <ReviewMark mark={finding.kind} words={t(`shell.studio.aiRequest.review.kind.${finding.kind}`)} />{' '}
          <Typography.Text>{finding.text}</Typography.Text>{' '}
          <ResourceReference
            resourceId={finding.resourceId}
            file={finding.file}
            path={finding.path}
            resources={resources}
            onSelect={onOpen}
          />
        </li>
      ))}
    </ul>
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
    // Proposed: when every scenario passes the count adds nothing, which Show the checks lists, see the review on #3263.
    return { text: t('shell.studio.aiRequest.checks.allScenarios'), passed: true }
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

/**
 * The name of the text of each step kind whose words name nothing else: under `step.` what it did, and, proposed, under
 * `stepNow.` what it does while it runs, see the review on #3263.
 */
const stepKeys: Record<
  Exclude<
    StudioAuthoringStepKind,
    'planChange' | 'changeRequest' | 'write_resource' | 'write_layer' | 'delete_resource'
  >,
  string
> = {
  plan: 'plan',
  work: 'work',
  check: 'check',
  fix: 'fix',
  review: 'review',
  accept: 'accept',
  propose_plan: 'proposePlan',
  validate_draft: 'validateDraft',
  run_scenarios: 'runScenarios',
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
