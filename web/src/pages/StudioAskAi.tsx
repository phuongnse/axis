import { Flex, Form, Input, Typography } from 'antd'
import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router'
import { AxisAlert } from '../platform/AxisAlert'
import { SidePanel } from '../platform/SidePanel'
import { AxisButton } from '../platform/AxisButton'
import { AxisCheckboxField } from '../platform/AxisCheckboxField'
import { AxisField } from '../platform/AxisField'
import { requiredMark } from '../platform/requiredMark'
import { studioRequestAddress } from '../platform/studioAddress'
import { fetchStudioSettings, sendAuthoringRequest, ServerUnreachableError } from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'

/** The id of the request panel, which Ask AI on the band names in `aria-controls`. */
export const askAiPanelId = 'studio-ask-ai-panel'

const maxTextLength = 4000

type Values = { text: string; autoAccept?: boolean }

/**
 * The request panel of the open application, drawn in `SidePanel`, see
 * docs/reference/ai-authoring-in-studio.md#the-request-panel. The author writes a request of up to 4,000 characters and
 * sends it. The check box "Accept when checks and review pass" shows only when the application's auto-accept setting is
 * on, which `fetchStudioSettings` reads, and a failed read hides it. Send posts the trimmed text and the check box's
 * value. After a send, the field is empty, the panel stays open and the main area opens the request's address,
 * `/studio?app=<application>&view=requests&request=<id>`. An empty request is refused on click, with a message under the
 * field. A failed send shows one error message above the form, and a server that does not answer starts the reconnect.
 * The panel has no list of requests and no count. It ends, under a rule, with "Use your own AI app" and the MCP address
 * of the page's host with a copy button, see docs/reference/ai-authoring-in-studio.md#connected-apps. `onClose` runs
 * from the close button.
 */
export function StudioAskAi({ application, onClose }: { application: string; onClose: () => void }) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const navigate = useNavigate()
  const { lost } = useStudioConnection()
  const [form] = Form.useForm<Values>()
  const [autoAcceptOn, setAutoAcceptOn] = useState(false)
  const [sending, setSending] = useState(false)
  const [sendFailed, setSendFailed] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    fetchStudioSettings(application, controller.signal).then(
      (settings) => setAutoAcceptOn(settings.autoAccept === true),
      (error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn(`Loading the settings of '${application}' failed`, error)
        }
      },
    )
    return () => controller.abort()
  }, [application])

  async function send(values: Values) {
    setSending(true)
    setSendFailed(false)
    try {
      const sent = await sendAuthoringRequest(application, {
        text: values.text.trim(),
        autoAccept: autoAcceptOn && values.autoAccept === true,
      })
      form.resetFields()
      navigate(studioRequestAddress(application, sent.id))
    } catch (error) {
      if (error instanceof ServerUnreachableError) {
        lost()
      } else {
        console.warn(`Sending the authoring request for '${application}' failed`, error)
        setSendFailed(true)
      }
    } finally {
      setSending(false)
    }
  }

  return (
    <SidePanel
      id={askAiPanelId}
      testId="studio-ask-ai-panel"
      closeTestId="studio-ask-ai-close"
      closeNameKey="shell.studio.askAi.close"
      title={t('shell.studio.askAi.title')}
      onClose={onClose}
    >
      <Flex vertical gap="middle">
        {sendFailed && (
          <AxisAlert data-testid="studio-ask-ai-error" kind="error" message={t('shell.studio.askAi.sendFailed')} />
        )}
        <Form form={form} layout="vertical" requiredMark={requiredMark} onFinish={send}>
          <Flex vertical gap="large">
            <AxisField
              name="text"
              label={t('shell.studio.askAi.text')}
              help={t('shell.studio.askAi.text.help')}
              requiredMessage={t('shell.studio.askAi.text.required')}
              rules={[{ max: maxTextLength, message: t('shell.studio.askAi.text.tooLong') }]}
            >
              <Input.TextArea rows={6} />
            </AxisField>
            {autoAcceptOn && (
              <AxisCheckboxField
                name="autoAccept"
                label={t('shell.studio.askAi.autoAccept')}
                help={t('shell.studio.askAi.autoAccept.help')}
              />
            )}
            <div>
              <AxisButton role="main" htmlType="submit" data-testid="studio-ask-ai-send" loading={sending}>
                {t('shell.studio.askAi.send')}
              </AxisButton>
            </div>
          </Flex>
        </Form>
        <Flex vertical gap="small" style={{ borderTop: `1px solid ${colour.borderSubtle}`, paddingTop: spacing.md }}>
          <Typography.Text strong>{t('shell.studio.askAi.ownApp')}</Typography.Text>
          <span style={{ color: colour.textMuted }}>
            {t('shell.studio.askAi.ownApp.help')}{' '}
            <Typography.Text code copyable data-testid="studio-mcp-address">
              {`${window.location.origin}/mcp`}
            </Typography.Text>
          </span>
        </Flex>
      </Flex>
    </SidePanel>
  )
}
