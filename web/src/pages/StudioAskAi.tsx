import { Flex, Form, Input, Typography } from 'antd'
import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisActionRow, AxisButton } from '../platform/AxisButton'
import { AxisCheckboxField, AxisField } from '../platform/AxisField'
import { QuotaRefusedError } from '../platform/quota'
import { requiredMark } from '../platform/requiredMark'
import { fetchAuthoringRequests, fetchStudioSettings, sendAuthoringRequest } from '../platform/studio'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { authoringRequestAddress } from './authoringRequests'
import { StudioSidePanel } from './StudioSidePanel'

/** The id of the request panel, which the band's Ask AI button controls. */
export const askAiPanelId = 'studio-ask-ai-panel'

/** The longest request text the send route takes, see docs/reference/authoring-requests.md#routes. */
const maxText = 4000

/**
 * Proposed: the request panel of the open application, in the shared `StudioSidePanel`. On top an author writes a
 * request in words and sends it, with "Accept when checks and review pass" when the application allows auto-accept,
 * and the requests left this month when the tenant has a quota. A sent request opens its page, where its plan shows
 * when AI has written it, and the panel stays open and empty for the next one. Under a line comes the MCP server's
 * address on the tenant's host, with a copy button, for an AI app of the author's own. The panel lists no requests:
 * the sidebar leads to AI requests, and the notification bell tells the author when one waits for them or ends. The
 * panel goes with the page and never holds a request, see docs/reference/actions-and-controls.md#opening-things.
 */
export function StudioAskAi({
  application,
  revision,
  onClose,
}: {
  application: string
  /** Counts the changes to the application's requests, each of which loads the requests left again. */
  revision: number
  onClose: () => void
}) {
  const t = useText()
  const navigate = useNavigate()
  const { colour, spacing } = useDesignTokens()
  const [form] = Form.useForm<{ text: string; autoAccept: boolean }>()
  const [left, setLeft] = useState<number | null>(null)
  const [allowsAutoAccept, setAllowsAutoAccept] = useState(false)
  const [sending, setSending] = useState(false)
  const [sendError, setSendError] = useState<string | null>(null)

  // The requests left load again when a request changes, as a sent one uses one. Without them Send stays on, and the
  // send route refuses a request over the quota.
  useEffect(() => {
    const controller = new AbortController()
    fetchAuthoringRequests(application, controller.signal).then(
      (list) => setLeft(list.left),
      () => {},
    )
    return () => controller.abort()
  }, [application, revision])

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
