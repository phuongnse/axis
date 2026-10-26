import { Alert, Flex, Form, Input, Modal } from 'antd'
import { useState, type ReactNode } from 'react'
import type { CommandResult } from './inspection'
import { useText } from './texts'

const reasonMaxLength = 500

interface ReasonDialogProps {
  title: string
  /** Sends the command with the reason the user typed. */
  onSend: (reason: string) => Promise<CommandResult>
  onClose: () => void
  /** Called after the command was accepted, so the caller reads its data again. */
  onChanged: () => void
  /** What the dialog shows above the reason, such as a warning. */
  children?: ReactNode
}

/**
 * The dialog that asks for the reason of an operator command and sends it. A refusal shows the
 * server's problem title, and any other failure shows a generic text.
 */
export function ReasonDialog({ title, onSend, onClose, onChanged, children }: ReasonDialogProps) {
  const t = useText()
  const [form] = Form.useForm<{ reason: string }>()
  const [sending, setSending] = useState(false)
  const [error, setError] = useState<string>()

  const send = async () => {
    const { reason } = await form.validateFields()
    setSending(true)
    setError(undefined)
    try {
      const result = await onSend(reason)
      if (result.ok) {
        onClose()
        onChanged()
      } else {
        setError(result.problem.title)
      }
    } catch (failure) {
      console.warn(`Sending '${title}' failed`, failure)
      setError(t('shell.inspector.commands.failed'))
    } finally {
      setSending(false)
    }
  }

  return (
    <Modal
      open
      title={title}
      okText={t('shell.inspector.commands.send')}
      cancelText={t('shell.inspector.commands.close')}
      confirmLoading={sending}
      onOk={() => void send().catch(() => undefined)}
      onCancel={onClose}
      destroyOnHidden
    >
      <Flex vertical gap="middle">
        {children}
        <Form form={form} layout="vertical">
          <Form.Item
            name="reason"
            label={t('shell.inspector.commands.reason')}
            rules={[
              { required: true, whitespace: true, message: t('shell.inspector.commands.reasonRequired') },
              { max: reasonMaxLength, message: t('shell.inspector.commands.reasonTooLong') },
            ]}
          >
            <Input.TextArea rows={3} showCount maxLength={reasonMaxLength} />
          </Form.Item>
        </Form>
        {error !== undefined && <Alert data-testid="inspector-command-error" type="error" title={error} />}
      </Flex>
    </Modal>
  )
}
