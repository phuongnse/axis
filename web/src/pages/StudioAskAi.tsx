import { Checkbox, Flex, Form, Input, Typography } from 'antd'
import { useContext, useEffect, useState } from 'react'
import { Link } from 'react-router'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisActionRow, AxisButton } from '../platform/AxisButton'
import { AxisField } from '../platform/AxisField'
import { EmptyState } from '../platform/EmptyState'
import { QuotaRefusedError } from '../platform/quota'
import { requiredMark } from '../platform/requiredMark'
import {
  fetchAuthoringRequests,
  fetchStudioSettings,
  sendAuthoringRequest,
  type StudioAuthoringRequest,
  type StudioAuthoringRequestList,
  type StudioAuthoringRequestState,
} from '../platform/studio'
import { useText } from '../platform/texts'
import { selectedItemTint } from '../platform/antTheme'
import { useDesignTokens, useThemeMode } from '../platform/themeMode'
import { PlatformContext } from './context'
import { StudioSidePanel } from './StudioSidePanel'

/** The id of the request panel, which the band's Ask AI button controls. */
export const askAiPanelId = 'studio-ask-ai-panel'

/** The longest request text the send route takes, see docs/reference/authoring-requests.md#routes. */
const maxText = 4000

/** The states that wait for the author of the request. */
const waitingForAuthor: StudioAuthoringRequestState[] = ['awaitingApproval', 'ready']

/** The address of one request's page. */
export function authoringRequestAddress(application: string, id: string): string {
  return `/studio?${new URLSearchParams({ app: application, view: 'requests', request: id })}`
}

/**
 * Proposed: the request panel of the open application, in the shared `StudioSidePanel`. On top an author writes a
 * request in words and sends it, with "Accept when checks and review pass" when the application allows auto-accept,
 * and the requests left this month when the tenant has a quota. Under it the application's requests, newest first:
 * each one's text is a link to the request's page, with its state in words and who sent it when. A request that waits
 * for its author says so first. The panel goes with the page and never holds a request, see
 * docs/reference/actions-and-controls.md#opening-things. At its foot, the MCP server's address when the tenant lets
 * outside clients connect.
 */
export function StudioAskAi({
  application,
  userId,
  openRequest,
  mcpServer,
  onClose,
}: {
  application: string
  /** The signed-in author, whose own requests the list words for them. */
  userId: string | null
  /** The id of the request whose page is open, which the list marks. */
  openRequest: string | null
  mcpServer: string | null
  onClose: () => void
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const [form] = Form.useForm<{ text: string; autoAccept: boolean }>()
  const [list, setList] = useState<StudioAuthoringRequestList | 'failed'>()
  const [allowsAutoAccept, setAllowsAutoAccept] = useState(false)
  const [sending, setSending] = useState(false)
  const [sendError, setSendError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    fetchAuthoringRequests(application, controller.signal).then(setList, () => {
      if (!controller.signal.aborted) {
        setList('failed')
      }
    })
    fetchStudioSettings(application, controller.signal).then(
      (settings) => setAllowsAutoAccept(settings.autoAccept),
      () => {},
    )
    return () => controller.abort()
  }, [application])

  const left = list !== undefined && list !== 'failed' ? list.left : null
  const send = async (values: { text: string; autoAccept: boolean }) => {
    setSending(true)
    setSendError(null)
    try {
      const sent = await sendAuthoringRequest(application, values.text.trim(), values.autoAccept === true)
      setList((current) =>
        current === undefined || current === 'failed'
          ? current
          : { items: [sent, ...current.items], left: current.left === null ? null : current.left - 1 },
      )
      form.resetFields()
    } catch (error) {
      setSendError(error instanceof QuotaRefusedError ? error.message : t('shell.studio.ai.sendFailed'))
    } finally {
      setSending(false)
    }
  }

  return (
    <StudioSidePanel
      id={askAiPanelId}
      title={t('shell.studio.ai.title')}
      closeKey="shell.studio.ai.close"
      onClose={onClose}
      testId="studio-ask-ai"
    >
      <Form
        form={form}
        layout="vertical"
        requiredMark={requiredMark}
        initialValues={{ text: '', autoAccept: false }}
        onFinish={send}
      >
        <AxisField
          name="text"
          label={t('shell.studio.ai.text')}
          help={t('shell.studio.ai.text.help')}
          requiredMessage={t('shell.studio.ai.textRequired')}
        >
          <Input.TextArea
            aria-label={t('shell.studio.ai.text')}
            autoSize={{ minRows: 4, maxRows: 10 }}
            maxLength={maxText}
          />
        </AxisField>
        {allowsAutoAccept && (
          <AxisField
            name="autoAccept"
            valuePropName="checked"
            label={t('shell.studio.ai.autoAccept')}
            help={t('shell.studio.ai.autoAccept.help')}
          >
            <Checkbox aria-label={t('shell.studio.ai.autoAccept')} />
          </AxisField>
        )}
        {sendError !== null && <AxisAlert kind="error" message={sendError} style={{ marginBottom: spacing.sm }} />}
        <AxisActionRow align="center">
          <AxisButton role="finish" htmlType="submit" disabled={sending || left === 0}>
            {t('shell.studio.ai.send')}
          </AxisButton>
          {left !== null && (
            <Typography.Text style={{ color: left === 0 ? colour.warningText : colour.textMuted }}>
              {left === 0
                ? t('shell.studio.ai.noneLeft')
                : left === 1
                  ? t('shell.studio.ai.oneLeft')
                  : t('shell.studio.ai.left').replace('{count}', String(left))}
            </Typography.Text>
          )}
        </AxisActionRow>
      </Form>
      <Typography.Title level={5} style={{ margin: `${spacing.sm}px 0 0` }}>
        {t('shell.studio.ai.requests')}
      </Typography.Title>
      {list === 'failed' ? (
        <AxisAlert kind="warning" message={t('shell.studio.ai.listFailed')} />
      ) : list === undefined ? null : list.items.length === 0 ? (
        <EmptyState text={t('shell.studio.ai.noRequests')} />
      ) : (
        <ul style={{ listStyle: 'none', margin: 0, padding: 0 }} data-testid="studio-ai-requests">
          {list.items.map((request) => (
            <RequestRow
              key={request.id}
              application={application}
              request={request}
              mine={request.author === userId}
              open={request.id === openRequest}
            />
          ))}
        </ul>
      )}
      {mcpServer !== null && (
        <Flex
          vertical
          gap={4}
          style={{ marginTop: spacing.sm, paddingTop: spacing.sm, borderTop: `1px solid ${colour.borderSubtle}` }}
        >
          <Typography.Text strong>{t('shell.studio.ai.mcp')}</Typography.Text>
          <Typography.Text style={{ color: colour.textMuted }}>{t('shell.studio.ai.mcp.help')}</Typography.Text>
          <Typography.Text copyable={{ text: mcpServer }} code>
            {mcpServer}
          </Typography.Text>
        </Flex>
      )}
    </StudioSidePanel>
  )
}

/** One request of the list: its text as a link to its page, its state in words, and who sent it when. */
function RequestRow({
  application,
  request,
  mine,
  open,
}: {
  application: string
  request: StudioAuthoringRequest
  mine: boolean
  open: boolean
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const { mode } = useThemeMode()
  const locale = useContext(PlatformContext)?.current.locale
  const time = new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short' }).format(
    new Date(request.sentAt),
  )
  const waiting = waitingForAuthor.includes(request.state)
  const stateColour =
    request.state === 'failed'
      ? colour.dangerText
      : request.state === 'accepted' || request.state === 'dropped'
        ? colour.textMuted
        : colour.text
  const stateKey =
    waiting && !mine ? `shell.studio.ai.state.${request.state}.other` : `shell.studio.ai.state.${request.state}`

  return (
    <li
      aria-current={open ? 'page' : undefined}
      style={{
        padding: `${spacing.xs}px ${spacing.sm}px`,
        marginInline: -spacing.sm,
        background: open ? selectedItemTint(mode) : undefined,
        borderTop: `1px solid ${colour.borderSubtle}`,
      }}
    >
      <Flex vertical gap={2}>
        <Link
          to={authoringRequestAddress(application, request.id)}
          style={{
            display: '-webkit-box',
            WebkitLineClamp: 2,
            WebkitBoxOrient: 'vertical',
            overflow: 'hidden',
          }}
        >
          {request.text}
        </Link>
        <Typography.Text strong={waiting && mine} style={{ color: stateColour }}>
          {t(stateKey)}
        </Typography.Text>
        <Typography.Text style={{ color: colour.textMuted }}>
          {t(mine ? 'shell.studio.ai.sentByYou' : 'shell.studio.ai.sentBy')
            .replace('{name}', request.authorName ?? t('shell.studio.ai.someone'))
            .replace('{time}', time)}
        </Typography.Text>
      </Flex>
    </li>
  )
}
