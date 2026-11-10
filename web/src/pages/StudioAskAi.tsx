import { Flex, Form, Input, Typography } from 'antd'
import { useContext, useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisActionRow, AxisButton } from '../platform/AxisButton'
import { AxisCheckboxField, AxisField } from '../platform/AxisField'
import { QuotaRefusedError } from '../platform/quota'
import { requiredMark } from '../platform/requiredMark'
import {
  fetchAuthoringRequests,
  fetchStudioSettings,
  sendAuthoringRequest,
  type StudioAuthoringRequest,
  type StudioAuthoringRequestList,
} from '../platform/studio'
import { useText } from '../platform/texts'
import { selectedItemTint } from '../platform/antTheme'
import { useDesignTokens, useThemeMode } from '../platform/themeMode'
import { PlatformContext } from './context'
import { authoringRequestAddress, useRequestStateWords, waitingForAuthor } from './authoringRequests'
import { StudioSidePanel } from './StudioSidePanel'

/** The id of the request panel, which the band's Ask AI button controls. */
export const askAiPanelId = 'studio-ask-ai-panel'

/** The longest request text the send route takes, see docs/reference/authoring-requests.md#routes. */
const maxText = 4000

/**
 * Proposed: the request panel of the open application, in the shared `StudioSidePanel`. On top an author writes a
 * request in words and sends it, with "Accept when checks and review pass" when the application allows auto-accept,
 * and the requests left this month when the tenant has a quota. A sent request opens its page, where its plan shows
 * when AI has written it, and the panel stays open and empty for the next one. Under the form only the author's
 * requests that wait for them show, under "Waiting for you": each one's text is a link to its page, and under it its
 * state and when it was sent. The request whose page is open is on the selected tint. Last, "All AI requests" leads to
 * the page that lists every request. The panel goes with the page and never holds a request, see
 * docs/reference/actions-and-controls.md#opening-things.
 */
export function StudioAskAi({
  application,
  userId,
  openRequest,
  revision,
  onClose,
}: {
  application: string
  /** The signed-in author, whose requests that wait for them the panel shows. */
  userId: string | null
  /** The id of the request whose page is open, which the panel marks. */
  openRequest: string | null
  /** Counts the changes to the application's requests, each of which loads them again. */
  revision: number
  onClose: () => void
}) {
  const t = useText()
  const navigate = useNavigate()
  const { colour, spacing } = useDesignTokens()
  const [form] = Form.useForm<{ text: string; autoAccept: boolean }>()
  const [list, setList] = useState<StudioAuthoringRequestList | 'failed'>()
  const [allowsAutoAccept, setAllowsAutoAccept] = useState(false)
  const [sending, setSending] = useState(false)
  const [sendError, setSendError] = useState<string | null>(null)

  // The list loads again when another request's page opens or a request changes, as that changes what waits.
  useEffect(() => {
    const controller = new AbortController()
    fetchAuthoringRequests(application, controller.signal).then(setList, () => {
      if (!controller.signal.aborted) {
        setList('failed')
      }
    })
    return () => controller.abort()
  }, [application, openRequest, revision])

  useEffect(() => {
    const controller = new AbortController()
    fetchStudioSettings(application, controller.signal).then(
      (settings) => setAllowsAutoAccept(settings.autoAccept),
      () => {},
    )
    return () => controller.abort()
  }, [application])

  const left = list !== undefined && list !== 'failed' ? list.left : null
  const waiting =
    list !== undefined && list !== 'failed'
      ? list.items.filter((request) => request.author === userId && waitingForAuthor.includes(request.state))
      : []
  const send = async (values: { text: string; autoAccept: boolean }) => {
    setSending(true)
    setSendError(null)
    try {
      const sent = await sendAuthoringRequest(application, values.text.trim(), values.autoAccept === true)
      form.resetFields()
      navigate(authoringRequestAddress(application, sent.id))
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
      }
    >
      <Flex vertical gap={spacing.sm} style={{ marginTop: spacing.sm }}>
        {list === 'failed' && <AxisAlert kind="warning" message={t('shell.studio.ai.listFailed')} />}
        {waiting.length > 0 && (
          <Flex vertical gap={4}>
            <Typography.Text strong>{t('shell.studio.ai.waiting')}</Typography.Text>
            <Flex
              component="ul"
              vertical
              gap={4}
              style={{ listStyle: 'none', margin: 0, padding: 0 }}
              data-testid="studio-ai-waiting"
            >
              {waiting.map((request) => (
                <WaitingRow
                  key={request.id}
                  application={application}
                  request={request}
                  open={request.id === openRequest}
                />
              ))}
            </Flex>
          </Flex>
        )}
        <Link to={authoringRequestAddress(application)} data-testid="studio-ai-all-requests">
          {t('shell.studio.ai.allRequests')}
        </Link>
      </Flex>
    </StudioSidePanel>
  )
}

/** One request that waits for its author: its text as a link to its page, then its state and when it was sent. */
function WaitingRow({
  application,
  request,
  open,
}: {
  application: string
  request: StudioAuthoringRequest
  open: boolean
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const { mode } = useThemeMode()
  const stateWords = useRequestStateWords()
  const locale = useContext(PlatformContext)?.current.locale
  const sentAt = new Date(request.sentAt)
  const date = new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'short' }).format(sentAt)
  const time = new Intl.DateTimeFormat(locale, { hour: 'numeric', minute: '2-digit' }).format(sentAt)

  return (
    <li
      aria-current={open ? 'page' : undefined}
      style={{
        padding: `${spacing.xs / 2}px ${spacing.md}px`,
        marginInline: -spacing.md,
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
        <span style={{ color: colour.text, fontWeight: 600 }}>{stateWords(request, true)}</span>
        {t('shell.studio.ai.sentByYou').replace('{date}', date).replace('{time}', time)}
      </Typography.Text>
    </li>
  )
}
