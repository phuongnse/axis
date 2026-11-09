import { Alert, Flex, Form, Input, Modal, Radio, Select } from 'antd'
import { useState } from 'react'
import { resolveCall, type CallOutput, type InstanceCall } from './inspection'
import { placeProblemErrors } from './problemErrors'
import type { RecordProblem } from './records'
import { buildResolveBody } from './recordBody'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'

interface ResolveDialogProps {
  call: InstanceCall
  outputs: readonly CallOutput[]
  instanceId: string
  instancesPath: string
  onClose: () => void
  /** Called after the resolve was accepted, so the page reads the instance again. */
  onChanged: () => void
}

interface ResolveValues {
  reason: string
  outcome?: 'succeeded' | 'failed'
  output?: Record<string, string | boolean | undefined>
}

const reasonMaxLength = 500

/**
 * The dialog that settles an `unknown` call. It asks for a reason and an outcome, and for `succeeded`
 * a value for each declared output, with the required ones marked. A `400` shows the messages of
 * `/reason`, `/outcome` and each declared output under their fields, and any other message after the
 * problem title in the dialog. Any other refusal shows its title, and any other failure shows a
 * generic text. The server checks everything, so the client only blocks what it can see is missing.
 */
export function ResolveDialog({ call, outputs, instanceId, instancesPath, onClose, onChanged }: ResolveDialogProps) {
  const t = useText()
  const { size } = useDesignTokens()
  const [form] = Form.useForm<ResolveValues>()
  const outcome = Form.useWatch('outcome', form)
  const [sending, setSending] = useState(false)
  const [error, setError] = useState<string>()

  const showProblem = (problem: RecordProblem, chosen: ResolveValues['outcome']) => {
    const declared = new Set(outputs.map((output) => output.name))
    const placed = placeProblemErrors<'reason' | 'outcome' | ['output', string]>(
      problem.errors,
      ([head, name, ...rest]) => {
        if (rest.length > 0) {
          return null
        }
        if (name === undefined) {
          return head === 'reason' || head === 'outcome' ? head : null
        }
        return head === 'output' && chosen === 'succeeded' && declared.has(name) ? ['output', name] : null
      },
    )
    form.setFields(placed.fields.map(({ field, messages }) => ({ name: field, errors: messages })))
    setError([problem.title, ...placed.form].join(' '))
  }

  const send = async () => {
    const values = await form.validateFields()
    setSending(true)
    setError(undefined)
    try {
      const body = buildResolveBody(values.reason, values.outcome!, outputs, values.output ?? {})
      const result = await resolveCall(instancesPath, instanceId, call.id, body)
      if (result.ok) {
        onClose()
        onChanged()
      } else {
        showProblem(result.problem, values.outcome)
      }
    } catch (failure) {
      console.warn(`Resolving the call '${call.id}' of the instance '${instanceId}' failed`, failure)
      setError(t('shell.inspector.commands.failed'))
    } finally {
      setSending(false)
    }
  }

  return (
    <Modal
      open
      width={size.dialog.medium}
      title={t('shell.inspector.commands.resolve')}
      okText={t('shell.inspector.commands.send')}
      cancelText={t('shell.inspector.commands.close')}
      confirmLoading={sending}
      onOk={() => void send().catch(() => undefined)}
      onCancel={onClose}
      destroyOnHidden
    >
      <Flex vertical gap="middle">
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
          <Form.Item
            name="outcome"
            label={t('shell.inspector.commands.outcome')}
            rules={[{ required: true, message: t('shell.inspector.commands.outcomeRequired') }]}
          >
            <Radio.Group
              options={[
                { value: 'succeeded', label: t('shell.inspector.commands.outcomeSucceeded') },
                { value: 'failed', label: t('shell.inspector.commands.outcomeFailed') },
              ]}
            />
          </Form.Item>
          {outcome === 'succeeded' && outputs.length > 0 && (
            <fieldset style={{ border: 0, margin: 0, padding: 0 }} data-testid="inspector-resolve-outputs">
              <legend>{t('shell.inspector.commands.outputs')}</legend>
              {outputs.map((output) => (
                <Form.Item
                  key={output.name}
                  name={['output', output.name]}
                  label={`${output.name} (${output.type})`}
                  required={output.required}
                  rules={
                    output.required ? [{ required: true, message: t('shell.inspector.commands.outputRequired') }] : []
                  }
                >
                  {output.type === 'boolean' ? (
                    <Select
                      allowClear
                      virtual={false}
                      options={[
                        { value: true, label: 'true' },
                        { value: false, label: 'false' },
                      ]}
                    />
                  ) : (
                    <Input />
                  )}
                </Form.Item>
              ))}
            </fieldset>
          )}
        </Form>
        {/* oxlint-disable-next-line axis/no-antd-alert -- Predates AxisAlert. */}
        {error !== undefined && <Alert data-testid="inspector-command-error" type="error" title={error} />}
      </Flex>
    </Modal>
  )
}
