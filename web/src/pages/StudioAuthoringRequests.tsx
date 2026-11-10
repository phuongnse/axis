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
  actOnAuthoringRequest,
  fetchAuthoringRequest,
  fetchAuthoringRequests,
  ServerUnreachableError,
  type StudioAuthoringRequest,
  type StudioAuthoringRequestDetail,
  type StudioAuthoringRequestState,
  type StudioAuthoringRequestStep,
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
import { StudioPageHeading } from './StudioPageHeading'

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
      title: t('shell.studio.requests.request'),
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
      title: t('shell.studio.requests.state'),
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
      title: t('shell.studio.requests.who'),
      render: (_, row) =>
        row.author === userId ? t('shell.studio.requests.you') : (row.authorName ?? t('shell.studio.ai.someone')),
    },
    {
      key: 'when',
      kind: 'fixed',
      title: t('shell.studio.requests.when'),
      render: (_, row) => sentTime(row.sentAt),
    },
  ]

  return (
    <Flex vertical gap="middle">
      <StudioPageHeading title={t('shell.studio.requests')} />
      <AxisTable<StudioAuthoringRequest>
        columns={columns}
        rowKey={(row) => row.id}
        dataSource={Array.isArray(requests) ? requests : []}
        loading={requests === undefined}
        emptyText={t('shell.studio.requests.empty')}
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
 * with a comment or drops the request; anyone else reads that it waits for its author. The
 * diff and Accept are step 4 of the review on #3263. Under "Progress" a `StepsBar` shows the loop's stages, then the
 * state in words with the fix round, and while it runs "Now:" with the summary of the step that has not finished.
 * "What AI did" lists every step with its result and when it started.
 */
export function StudioAuthoringRequestPage({
  application,
  requestId,
  userId,
  onChanged,
}: {
  application: string
  requestId: string
  userId: string | null
  /** Runs when the request's state changes, so the request panel shows what waits now. */
  onChanged: () => void
}) {
  const t = useText()
  const { colour, radius, size, spacing } = useDesignTokens()
  const sentTime = useSentTime()
  const stateWords = useRequestStateWords()
  const stateColour = useRequestStateColour()
  const stageTitles = stageKeys.map((key) => t(`shell.studio.request.stage.${key}`))
  const { generation, lost } = useStudioConnection()
  const [loaded, setLoaded] = useState<{ id: string; request: StudioAuthoringRequestDetail | null }>()
  const [follow, setFollow] = useState(0)
  const [acting, setActing] = useState(false)
  const [actionError, setActionError] = useState(false)
  const [changeOpen, setChangeOpen] = useState(false)
  const [dropOpen, setDropOpen] = useState(false)
  const [changeForm] = Form.useForm<{ comment: string }>()

  const request = loaded?.id === requestId ? loaded.request : undefined
  const following = request !== undefined && request !== null && !settled.includes(request.state)

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

  const back = <Link to={authoringRequestAddress(application)}>{t('shell.studio.requests.back')}</Link>
  if (request === undefined) {
    return <Spin />
  }
  if (request === null) {
    return (
      <Flex vertical gap="small">
        <Typography.Text>{t('shell.studio.requests.notFound')}</Typography.Text>
        {back}
      </Flex>
    )
  }

  const mine = request.author === userId
  const act = async (action: 'approve' | 'change-plan' | 'drop', comment?: string) => {
    setActing(true)
    setActionError(false)
    try {
      const next = await actOnAuthoringRequest(application, request.id, action, comment)
      setLoaded({ id: requestId, request: { ...request, ...next } })
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
  const line = (mine ? t('shell.studio.request.lineMine') : t('shell.studio.request.line'))
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
      title: t('shell.studio.request.step'),
      // A change of plan keeps the author's comment as its summary.
      render: (_, step) =>
        step.kind === 'planChange'
          ? t('shell.studio.request.planChange').replace('{comment}', step.summary)
          : step.summary,
    },
    {
      key: 'result',
      kind: 'freeText',
      title: t('shell.studio.request.result'),
      render: (_, step) =>
        step.finishedAt === null ? (
          <span style={{ color: colour.textMuted }}>{t('shell.studio.request.stepRunning')}</span>
        ) : (
          (step.outcome ?? <EmptyDash />)
        ),
    },
    {
      key: 'started',
      kind: 'fixed',
      title: t('shell.studio.request.started'),
      render: (_, step) => sentTime(step.startedAt),
    },
  ]
  const canDrop = mine && request.state !== 'accepted' && request.state !== 'dropped'
  const awaiting = request.state === 'awaitingApproval'

  return (
    <Flex vertical gap="middle" data-testid="studio-ai-request">
      <StudioPageHeading
        title={t('shell.studio.request.title')}
        line={line}
        actions={
          canDrop && !awaiting ? (
            <AxisButton role="secondary" disabled={acting} onClick={() => setDropOpen(true)}>
              {t('shell.studio.request.drop')}
            </AxisButton>
          ) : undefined
        }
      />
      {back}
      {section(t('shell.studio.request.asked'), quoted(request.text))}
      {section(
        t('shell.studio.request.progress'),
        <Flex vertical gap="small" data-testid="studio-ai-progress">
          {stage !== null && <StepsBar titles={stageTitles} current={stage} />}
          <Flex align="center" gap={spacing.sm}>
            {following && <Spin size="small" />}
            <Typography.Text style={{ color: stateColour(request.state) }}>
              {stateWords(request, mine)}
              {request.round > 1 && request.state !== 'planning' && (
                <span style={{ color: colour.textMuted }}>
                  {t('shell.studio.request.round').replace('{round}', String(request.round))}
                </span>
              )}
            </Typography.Text>
          </Flex>
          {running !== undefined && following && (
            <Typography.Text style={{ color: colour.textMuted }}>
              {t('shell.studio.request.now').replace('{step}', running.summary)}
            </Typography.Text>
          )}
          {request.state === 'planning' && (
            <Typography.Text style={{ color: colour.textMuted }}>{t('shell.studio.request.planning')}</Typography.Text>
          )}
        </Flex>,
      )}
      {request.plan !== null &&
        section(
          t('shell.studio.request.plan'),
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
                {t('shell.studio.request.notYours')}
              </Typography.Text>
            )}
          </Flex>,
        )}
      {actionError && <AxisAlert kind="error" message={t('shell.studio.request.actionFailed')} />}
      {awaiting && mine && (
        <AxisActionRow>
          <AxisButton role="finish" disabled={acting} onClick={() => void act('approve')}>
            {t('shell.studio.request.approve')}
          </AxisButton>
          <AxisButton role="secondary" disabled={acting} onClick={() => setChangeOpen(true)}>
            {t('shell.studio.request.changePlan')}
          </AxisButton>
          <AxisButton role="secondary" disabled={acting} onClick={() => setDropOpen(true)}>
            {t('shell.studio.request.drop')}
          </AxisButton>
        </AxisActionRow>
      )}
      {request.steps.length > 0 &&
        section(
          t('shell.studio.request.steps'),
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
        title={t('shell.studio.request.changePlan.title')}
        okText={t('shell.studio.request.changePlan.ok')}
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
            label={t('shell.studio.request.comment')}
            help={t('shell.studio.request.comment.help')}
            requiredMessage={t('shell.studio.request.commentRequired')}
          >
            <Input.TextArea
              aria-label={t('shell.studio.request.comment')}
              autoSize={{ minRows: 3, maxRows: 8 }}
              maxLength={2000}
            />
          </AxisField>
        </Form>
      </Modal>
      <Modal
        open={dropOpen}
        width={size.dialog.small}
        title={t('shell.studio.request.dropConfirm')}
        okText={t('shell.studio.request.dropOk')}
        okButtonProps={{ danger: true }}
        cancelText={t('shell.studio.cancel')}
        confirmLoading={acting}
        onOk={() => void act('drop')}
        onCancel={() => setDropOpen(false)}
      >
        {t('shell.studio.request.dropHelp')}
      </Modal>
    </Flex>
  )
}
