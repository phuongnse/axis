import { Flex, Form, Input, Typography } from 'antd'
import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisActionRow, AxisButton } from '../platform/AxisButton'
import { AxisCheckboxField, AxisField } from '../platform/AxisField'
import { requiredMark } from '../platform/requiredMark'
import {
  fetchAiConnection,
  fetchStudioSettings,
  sendAuthoringRequest,
  type StudioAiConnection,
} from '../platform/studio'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { authoringRequestAddress } from './authoringRequests'
import { StudioSidePanel } from './StudioSidePanel'

/** The id of the request panel, which the band's Ask AI button controls. */
export const askAiPanelId = 'studio-ask-ai-panel'

/**
 * Proposed: where an administrator connects the tenant's AI. Today the Users page holds the model key; the held design
 * issue #3926 decides the settings' place.
 */
const aiSettingsAddress = '/users'

/** The longest request text the send route takes, see docs/reference/authoring-requests.md#routes. */
const maxText = 4000

/**
 * Proposed: the request panel of the open application, in the shared `StudioSidePanel`. On top an author writes a
 * request in words and sends it, with "Accept when checks and review pass" when the application allows auto-accept.
 * Axis counts no requests: the tenant's own AI connection pays for them, and its provider's limits answer as a
 * request's failure. Until the tenant has connected its AI, the panel says so in place of the form, with a link to
 * the settings for an administrator. A sent request opens its page, where its plan shows
 * when AI has written it, and the panel stays open and empty for the next one. Under a line comes the MCP server's
 * address on the tenant's host, with a copy button, for an AI app of the author's own. The panel lists no requests:
 * the sidebar leads to AI requests, and the notification bell tells the author when one waits for them or ends. The
 * panel goes with the page and never holds a request, see docs/reference/actions-and-controls.md#opening-things.
 */
export function StudioAskAi({ application, onClose }: { application: string; onClose: () => void }) {
  const t = useText()
  const navigate = useNavigate()
  const { colour, spacing } = useDesignTokens()
  const [form] = Form.useForm<{ text: string; autoAccept: boolean }>()
  const [connection, setConnection] = useState<StudioAiConnection | 'failed'>()
  const [allowsAutoAccept, setAllowsAutoAccept] = useState(false)
  const [sending, setSending] = useState(false)
  const [sendError, setSendError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    fetchAiConnection(controller.signal).then(setConnection, () => {
      if (!controller.signal.aborted) {
        setConnection('failed')
      }
    })
    return () => controller.abort()
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    fetchStudioSettings(application, controller.signal).then(
      (settings) => setAllowsAutoAccept(settings.autoAccept),
      () => {},
    )
    return () => controller.abort()
  }, [application])

  const send = async (values: { text: string; autoAccept: boolean }) => {
    setSending(true)
    setSendError(null)
    try {
      const sent = await sendAuthoringRequest(application, values.text.trim(), values.autoAccept === true)
      form.resetFields()
      navigate(authoringRequestAddress(application, sent.id))
    } catch (error) {
      console.warn('Sending the request failed', error)
      setSendError(t('shell.studio.ai.sendFailed'))
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
        connection === undefined ? null : connection !== 'failed' && !connection.connected ? (
          // Proposed: the panel before the tenant has connected its AI, see the review on #3263.
          <Flex vertical gap={spacing.xs} data-testid="studio-ai-not-connected">
            <Typography.Text strong>{t('shell.studio.ai.notConnected')}</Typography.Text>
            <Typography.Text style={{ color: colour.textMuted }}>
              {t(connection.canConnect ? 'shell.studio.ai.notConnected.admin' : 'shell.studio.ai.notConnected.author')}
            </Typography.Text>
            {connection.canConnect && <Link to={aiSettingsAddress}>{t('shell.studio.ai.connect')}</Link>}
          </Flex>
        ) : (
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
            <AxisActionRow>
              <AxisButton role="finish" htmlType="submit" disabled={sending}>
                {t('shell.studio.ai.send')}
              </AxisButton>
            </AxisActionRow>
          </Form>
        )
      }
    >
      {/* Proposed: the MCP server's address, see docs/reference/ai-authoring-in-studio.md#connected-apps. */}
      <Flex
        vertical
        gap={spacing.xs}
        data-testid="studio-ai-mcp"
        style={{ marginTop: spacing.md, paddingTop: spacing.md, borderTop: `1px solid ${colour.borderSubtle}` }}
      >
        <Typography.Text strong>{t('shell.studio.ai.mcp')}</Typography.Text>
        <Typography.Text style={{ color: colour.textMuted }}>{t('shell.studio.ai.mcp.help')}</Typography.Text>
        <Typography.Text code copyable>{`${window.location.origin}/mcp`}</Typography.Text>
      </Flex>
    </StudioSidePanel>
  )
}
