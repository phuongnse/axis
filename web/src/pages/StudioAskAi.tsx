import { Flex, Form, Input, Typography } from 'antd'
import { useContext, useEffect, useState } from 'react'
import { Link } from 'react-router'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisActionRow, AxisButton } from '../platform/AxisButton'
import { AxisCheckboxField, AxisField } from '../platform/AxisField'
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
 * each one's text is a link to the request's page, and under it one sentence of its state and who sent it when. The
 * request whose page is open is on the selected tint. The panel goes with the page and never holds a request, see
 * docs/reference/actions-and-controls.md#opening-things.
 */
export function StudioAskAi({
  application,
  userId,
  openRequest,
  onClose,
}: {
  application: string
  /** The signed-in author, whose own requests the list words for them. */
  userId: string | null
  /** The id of the request whose page is open, which the list marks. */
  openRequest: string | null
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
      top={
        <>
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
              <AxisCheckboxField
                name="autoAccept"
                label={t('shell.studio.ai.autoAccept')}
                help={t('shell.studio.ai.autoAccept.help')}
              />
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
          <Typography.Text strong style={{ marginTop: spacing.xs }}>
            {t('shell.studio.ai.requests')}
          </Typography.Text>
        </>
      }
    >
      {list === 'failed' ? (
        <AxisAlert kind="warning" message={t('shell.studio.ai.listFailed')} />
      ) : list === undefined ? null : list.items.length === 0 ? (
        <EmptyState text={t('shell.studio.ai.noRequests')} />
      ) : (
        <Flex
          component="ul"
          vertical
          gap={4}
          style={{ listStyle: 'none', margin: 0, padding: 0 }}
          data-testid="studio-ai-requests"
        >
          {list.items.map((request) => (
            <RequestRow
              key={request.id}
              application={application}
              request={request}
              mine={request.author === userId}
              open={request.id === openRequest}
            />
          ))}
        </Flex>
      )}
    </StudioSidePanel>
  )
}

/** One request of the list: its text as a link to its page, then one sentence of its state and who sent it when. */
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
  const sentAt = new Date(request.sentAt)
  const date = new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'short' }).format(sentAt)
  const time = new Intl.DateTimeFormat(locale, { hour: 'numeric', minute: '2-digit' }).format(sentAt)
  const waiting = waitingForAuthor.includes(request.state)
  const stateColour =
    request.state === 'failed'
      ? colour.dangerText
      : request.state === 'accepted' || request.state === 'dropped'
        ? colour.textMuted
        : colour.text
  const stateKey =
    waiting && !mine ? `shell.studio.ai.state.${request.state}.other` : `shell.studio.ai.state.${request.state}`
  const sent = t(mine ? 'shell.studio.ai.sentByYou' : 'shell.studio.ai.sentBy')
    .replace('{name}', request.authorName ?? t('shell.studio.ai.someone'))
    .replace('{date}', date)
    .replace('{time}', time)

  return (
    <li
      aria-current={open ? 'page' : undefined}
      style={{
        padding: `${spacing.xs / 2}px ${spacing.sm}px`,
        marginInline: -spacing.sm,
        background: open ? selectedItemTint(mode) : undefined,
      }}
    >
      <Link
        to={authoringRequestAddress(application, request.id)}
        style={{ display: '-webkit-box', WebkitLineClamp: 2, WebkitBoxOrient: 'vertical', overflow: 'hidden' }}
      >
        {request.text}
      </Link>
      <Typography.Text style={{ color: colour.textMuted }}>
        <span style={{ color: stateColour, fontWeight: waiting && mine ? 600 : undefined }}>{t(stateKey)}</span>
        {sent}
      </Typography.Text>
    </li>
  )
}
