import { Flex, Form, Input, Modal } from 'antd'
import { AxisForm } from './AxisForm'
import { AxisAlert } from './AxisAlert'
import { AxisField } from './AxisField'
import { DialogFooter } from './DialogFooter'
import { useState, type ReactNode } from 'react'
import type { CommandResult } from './inspection'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'

interface ReasonDialogProps {
  title: string
  /** Sends the command with the reason the user typed. */
  onSend: (reason: string) => Promise<CommandResult>
  onClose: () => void
  /** Called after the command was accepted, so the caller reads its data again. */
  onChanged: () => void
  /** What the dialog shows above the reason, such as a warning. */
  children?: ReactNode
  /** The field's label. Defaults to the operator's "Reason". */
  label?: string
  /** The field's help. Defaults to the operator's help. */
  help?: string
  /** The words of the send button. Defaults to the operator's "Send". */
  finishText?: string
  /** The words of the button that closes the dialog. Defaults to the operator's "Close". */
  cancelText?: string
  /** What the field says when it is empty. */
  requiredMessage?: string
  /** What the field says when the text is longer than `maxLength`. */
  tooLongMessage?: string
  /** The most characters the text takes. Defaults to 500. */
  maxLength?: number
  /** The text the field starts with, such as the current name in a rename. Defaults to empty. */
  initialValue?: string
  /** The width of the dialog, as a size token of the dialog. Defaults to `small`. */
  size?: 'small' | 'medium'
  /** What the dialog shows when the command fails with no answer from the server. */
  failedText?: string
}

/**
 * The dialog that asks for one required text, such as the reason of an operator command, and sends it. A refusal shows
 * the server's problem title, and any other failure shows a generic text, inside the dialog. Without its optional
 * props it asks for an operator's reason, and a reply on a request's page gives its own words, see
 * docs/reference/ai-authoring-in-studio.md#replies.
 */
export function ReasonDialog({
  title,
  onSend,
  onClose,
  onChanged,
  children,
  label,
  help,
  finishText,
  cancelText,
  requiredMessage,
  tooLongMessage,
  maxLength = 500,
  initialValue,
  size: dialogSize = 'small',
  failedText,
}: ReasonDialogProps) {
  const t = useText()
  const { size } = useDesignTokens()
  const [form] = Form.useForm<{ reason: string }>()
  const [sending, setSending] = useState(false)
  const [error, setError] = useState<string>()

  const send = async (reason: string) => {
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
      setError(failedText ?? t('shell.inspector.commands.failed'))
    } finally {
      setSending(false)
    }
  }

  return (
    <Modal
      open
      width={dialogSize === 'medium' ? size.dialog.medium : size.dialog.small}
      title={title}
      footer={
        <DialogFooter
          finishText={finishText ?? t('shell.inspector.commands.send')}
          cancelText={cancelText ?? t('shell.inspector.commands.close')}
          finishing={sending}
          onFinish={() => form.submit()}
          onCancel={onClose}
        />
      }
      onCancel={onClose}
      destroyOnHidden
    >
      <Flex vertical gap="middle">
        {children}
        <AxisForm form={form} initialValues={{ reason: initialValue }} onFinish={({ reason }) => void send(reason)}>
          <AxisField
            name="reason"
            label={label ?? t('shell.inspector.commands.reason')}
            help={help ?? t('shell.inspector.commands.reason.help')}
            requiredMessage={requiredMessage ?? t('shell.inspector.commands.reasonRequired')}
            rules={[{ max: maxLength, message: tooLongMessage ?? t('shell.inspector.commands.reasonTooLong') }]}
          >
            <Input.TextArea rows={3} showCount maxLength={maxLength} />
          </AxisField>
        </AxisForm>
        {error !== undefined && <AxisAlert data-testid="inspector-command-error" kind="error" message={error} />}
      </Flex>
    </Modal>
  )
}
