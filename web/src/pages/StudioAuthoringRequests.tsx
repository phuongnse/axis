import { Flex, Form, Input, Modal, Spin, Typography } from 'antd'
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
  settled,
  useRequestStateColour,
  useRequestStateWords,
  waitingForAuthor,
} from './authoringRequests'
import { StudioChanges } from './StudioHistory'
import { StudioPageHeading } from './StudioPageHeading'
import { StudioDiagnosticRow } from './StudioProblems'
import { StudioRequestStatus } from './StudioRequestStatus'
import { StudioTestResults } from './StudioTestResults'

/** Proposed: the stages of a request's loop, which a `StepsBar` shows on its page. */
const stageKeys = ['plan', 'approval', 'change', 'checks', 'review', 'ready'] as const

/**
 * The stage a state is in: fixing goes back to the checks, and an accepted request has finished every stage. A dropped
 * or failed request shows no stages.
 */
function stageOf(state: StudioAuthoringRequestState): number | null {
  switch (state) {
    case 'planning':
      return 0
    case 'awaitingApproval':
      return 1
    case 'working':
      return 2
    case 'checking':
    case 'fixing':
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
  const stateColour = useRequestStateColour()
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
          {row.text}
        </Link>
      ),
    },
    {
      key: 'state',
      kind: 'freeText',
      title: t('shell.studio.aiRequests.state'),
      render: (_, row) => {
        const mine = row.author === userId
        return (
          <span
            style={{
              color: stateColour(row.state),
              fontWeight: mine && waitingForAuthor.includes(row.state) ? 600 : undefined,
            }}
          >
            {stateWords(row, mine)}
          </span>
        )
      },
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
 * makes the change again on the folder as it is now, or drops the request. Under "Progress" a `StepsBar` shows the loop's stages, then the
 * state in words with the fix round, and while it runs "Now:" with the summary of the step that has not finished.
 * "What AI did" lists every step with its result and when it started.
 */
export function StudioAuthoringRequestPage({
  application,
  requestId,
  userId,
  resources,
  onChanged,
  onOpen,
}: {
  application: string
  requestId: string
  userId: string | null
  /** The application's resources, which name the files of the draft's diff. */
  resources: StudioResourceItem[]
  /** Runs when the request's state changes, so the request panel shows what waits now. */
  onChanged: () => void
  /** Opens a resource at a path, from a problem or a failed scenario of the checks. */
  onOpen: (resourceId: string, path: string) => void
}) {
  const t = useText()
  const { colour, radius, size, spacing } = useDesignTokens()
  const sentTime = useSentTime()
  const stateWords = useRequestStateWords()
  const stateColour = useRequestStateColour()
  const stageTitles = stageKeys.map((key) => t(`shell.studio.aiRequest.stage.${key}`))
  const { generation, lost } = useStudioConnection()
  const [loaded, setLoaded] = useState<{ id: string; request: StudioAuthoringRequestDetail | null }>()
  const [follow, setFollow] = useState(0)
  const [acting, setActing] = useState(false)
  const [actionError, setActionError] = useState(false)
  const [changeOpen, setChangeOpen] = useState(false)
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
      onChanged()
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
      onChanged()
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
  const act = async (action: 'approve' | 'change-plan' | 'drop' | 'retry', comment?: string) => {
    setActing(true)
    setActionError(false)
    try {
      const next = await actOnAuthoringRequest(application, request.id, action, comment)
      setLoaded({ id: requestId, request: { ...request, ...next } })
      setAccepting(undefined)
      onChanged()
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
  const line = (mine ? t('shell.studio.aiRequest.lineMine') : t('shell.studio.aiRequest.line'))
    .replace('{name}', request.authorName ?? t('shell.studio.ai.someone'))
    .replace('{time}', sentTime(request.sentAt))
  const section = (title: string, body: ReactNode) => (
    <Flex vertical gap={spacing.xs}>
      <Typography.Text strong>{title}</Typography.Text>
      {body}
    </Flex>
  )
  const quoted = (text: string) => (
    <div
      style={{
        whiteSpace: 'pre-wrap',
        padding: `${spacing.sm}px ${spacing.md}px`,
        background: colour.surface,
        border: `1px solid ${colour.borderSubtle}`,
        borderRadius: radius.base,
      }}
    >
      {text}
    </div>
  )
  const stage = stageOf(request.state)
  const running = request.steps.findLast((step) => step.finishedAt === null)
  const stepColumns: AxisColumn<StudioAuthoringRequestStep>[] = [
    {
      key: 'step',
      kind: 'freeText',
      title: t('shell.studio.aiRequest.step'),
      // A change of plan keeps the author's comment as its summary.
      render: (_, step) =>
        step.kind === 'planChange'
          ? t('shell.studio.aiRequest.planChange').replace('{comment}', step.summary)
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
  const summaries = new Map(request.changes.map((change) => [change.file, change.summary]))
  const problems = request.checks?.diagnostics ?? []

  return (
    <Flex vertical gap="middle" data-testid="studio-ai-request">
      <StudioPageHeading
        title={t('shell.studio.aiRequest.title')}
        line={line}
        actions={
          canDrop && !awaiting && !ready ? (
            <AxisButton role="secondary" disabled={acting} onClick={() => setDropOpen(true)}>
              {t('shell.studio.aiRequest.drop')}
            </AxisButton>
          ) : undefined
        }
      />
      {back}
      {section(t('shell.studio.aiRequest.asked'), quoted(request.text))}
      {section(
        t('shell.studio.aiRequest.progress'),
        <Flex vertical gap="small" data-testid="studio-ai-progress">
          {stage !== null && <StepsBar titles={stageTitles} current={stage} />}
          <Flex align="center" gap={spacing.sm}>
            {following && <Spin size="small" />}
            <Typography.Text style={{ color: stateColour(request.state) }}>
              {stateWords(request, mine)}
              {request.round > 1 && request.state !== 'planning' && (
                <span style={{ color: colour.textMuted }}>
                  {t('shell.studio.aiRequest.round').replace('{round}', String(request.round))}
                </span>
              )}
            </Typography.Text>
          </Flex>
          {running !== undefined && following && (
            <Typography.Text style={{ color: colour.textMuted }}>
              {t('shell.studio.aiRequest.now').replace('{step}', running.summary)}
            </Typography.Text>
          )}
          {request.state === 'planning' && (
            <Typography.Text style={{ color: colour.textMuted }}>
              {t('shell.studio.aiRequest.planning')}
            </Typography.Text>
          )}
        </Flex>,
      )}
      {request.plan !== null &&
        section(
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
      {actionError && <AxisAlert kind="error" message={t('shell.studio.aiRequest.actionFailed')} />}
      {awaiting && mine && (
        <AxisActionRow>
          <AxisButton role="finish" disabled={acting} onClick={() => void act('approve')}>
            {t('shell.studio.aiRequest.approve')}
          </AxisButton>
          <AxisButton role="secondary" disabled={acting} onClick={() => setChangeOpen(true)}>
            {t('shell.studio.aiRequest.changePlan')}
          </AxisButton>
          <AxisButton role="secondary" disabled={acting} onClick={() => setDropOpen(true)}>
            {t('shell.studio.aiRequest.drop')}
          </AxisButton>
        </AxisActionRow>
      )}
      {request.state === 'failed' && (
        <AxisAlert
          kind="error"
          message={t('shell.studio.aiRequest.failed')}
          description={request.failure ?? undefined}
          data-testid="studio-ai-failure"
        />
      )}
      {showsDiff &&
        draft !== null &&
        section(
          t('shell.studio.aiRequest.changes'),
          changes === undefined ? (
            <Spin />
          ) : changes === 'failed' ? (
            <AxisAlert kind="error" message={t('shell.studio.aiRequest.diffFailed')} />
          ) : (
            <Flex vertical gap="middle" data-testid="studio-ai-changes">
              <StudioChanges changes={changes} resources={resources} summaries={summaries} />
            </Flex>
          ),
        )}
      {showsDiff &&
        request.checks !== null &&
        section(
          t('shell.studio.aiRequest.checks'),
          <Flex vertical gap="small" data-testid="studio-ai-checks">
            {problems.length > 0 && (
              <ul style={{ listStyle: 'none', margin: 0, padding: 0 }}>
                {problems.map((diagnostic, index) => (
                  <li key={index}>
                    <StudioDiagnosticRow diagnostic={diagnostic} onSelect={onOpen} />
                  </li>
                ))}
              </ul>
            )}
            <StudioTestResults request={request.checks} onSelect={onOpen} />
            {/* The review runs only once the checks pass, so a request that failed before it has no notes. */}
            {request.review !== null && (
              <>
                <Typography.Title level={5}>{t('shell.studio.aiRequest.review')}</Typography.Title>
                {request.review.length === 0 ? (
                  <Typography.Text style={{ color: colour.textMuted }}>
                    {t('shell.studio.aiRequest.noFindings')}
                  </Typography.Text>
                ) : (
                  <ul style={{ margin: 0, paddingInlineStart: spacing.lg }}>
                    {request.review.map((note, index) => (
                      <li key={index}>{note}</li>
                    ))}
                  </ul>
                )}
              </>
            )}
          </Flex>,
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
      {ready && mine && !acceptPending && (
        <AxisActionRow>
          {conflicts.length > 0 ? (
            <AxisButton role="finish" disabled={acting} onClick={() => void act('retry')}>
              {t('shell.studio.aiRequest.retry')}
            </AxisButton>
          ) : (
            <AxisButton role="finish" disabled={acting} onClick={() => void startAccept()}>
              {t('shell.studio.aiRequest.accept')}
            </AxisButton>
          )}
          <AxisButton role="secondary" disabled={acting} onClick={() => setDropOpen(true)}>
            {t('shell.studio.aiRequest.drop')}
          </AxisButton>
        </AxisActionRow>
      )}
      {request.steps.length > 0 &&
        section(
          t('shell.studio.aiRequest.steps'),
          <AxisTable<StudioAuthoringRequestStep>
            columns={stepColumns}
            rowKey={(step) => `${step.startedAt}/${step.kind}`}
            dataSource={request.steps}
            pagination={false}
            toolbar={{}}
            emptyText=""
          />,
        )}
      <Modal
        forceRender
        open={changeOpen}
        width={size.dialog.medium}
        title={t('shell.studio.aiRequest.changePlan.title')}
        okText={t('shell.studio.aiRequest.changePlan.ok')}
        cancelText={t('shell.studio.cancel')}
        confirmLoading={acting}
        onOk={() => changeForm.submit()}
        onCancel={() => setChangeOpen(false)}
      >
        <Form
          form={changeForm}
          layout="vertical"
          requiredMark={requiredMark}
          onFinish={(values) => void act('change-plan', values.comment.trim())}
        >
          <AxisField
            name="comment"
            label={t('shell.studio.aiRequest.comment')}
            help={t('shell.studio.aiRequest.comment.help')}
            requiredMessage={t('shell.studio.aiRequest.commentRequired')}
          >
            <Input.TextArea
              aria-label={t('shell.studio.aiRequest.comment')}
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
