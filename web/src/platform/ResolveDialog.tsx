import { Alert, Flex, Form, Input, Modal, Radio, Select, type FormInstance } from 'antd'
import { useState } from 'react'
import { resolveCall, type CallOutput, type InstanceCall } from './inspection'
import { buildResolveBody } from './recordBody'
import { useText } from './texts'

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

type FieldErrors = Parameters<FormInstance<ResolveValues>['setFields']>[0]

/** The name an `/output/{name}` pointer holds: `~1` is `/` and `~0` is `~`, in that order. */
function pointerName(token: string): string {
  return token.replaceAll('~1', '/').replaceAll('~0', '~')
}

/**
 * The dialog that settles an `unknown` call. It asks for a reason and an outcome, and for `succeeded`
 * a value for each declared output, with the required ones marked. A `400` shows each message under
 * the field its pointer names, and the problem title in the dialog. Any other refusal shows its
 * title, and any other failure shows a generic text. The server checks everything, so the client
 * only blocks what it can see is missing.
 */
export function ResolveDialog({ call, outputs, instanceId, instancesPath, onClose, onChanged }: ResolveDialogProps) {
  const t = useText()
  const [form] = Form.useForm<ResolveValues>()
  const outcome = Form.useWatch('outcome', form)
  const [sending, setSending] = useState(false)
  const [error, setError] = useState<string>()

  const showFieldErrors = (errors: Record<string, string[]>) => {
    const declared = new Set(outputs.map((output) => output.name))
    const fields: FieldErrors = []
    for (const [pointer, messages] of Object.entries(errors)) {
      if (pointer === '/reason') {
        fields.push({ name: 'reason', errors: messages })
      } else if (pointer === '/outcome') {
        fields.push({ name: 'outcome', errors: messages })
      } else if (pointer.startsWith('/output/')) {
        const name = pointerName(pointer.slice('/output/'.length))
        if (declared.has(name)) {
          fields.push({ name: ['output', name], errors: messages })
        }
      }
    }
    form.setFields(fields)
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
        setError(result.problem.title)
        showFieldErrors(result.problem.errors)
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
        {error !== undefined && <Alert data-testid="inspector-command-error" type="error" title={error} />}
      </Flex>
    </Modal>
  )
}
