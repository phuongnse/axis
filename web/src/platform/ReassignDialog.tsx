import { Alert, Flex, Form, Input, Modal, Radio, Select, type FormInstance } from 'antd'
import { useCallback, useEffect, useRef, useState } from 'react'
import type { RecordProblem } from './records'
import { searchUsers, type ReassignBody, type ReassignRole, type UserOption } from './tasks'
import { useText } from './texts'

interface ReassignDialogProps {
  roles: readonly ReassignRole[]
  /** The address of the user picker. */
  usersPath: string
  /**
   * Sends the reassign. It returns the problem of a `400` to show in the dialog, or `null` when the
   * page took over, by leaving the task or closing the dialog. A failure it throws is shown as a
   * generic text.
   */
  onSend: (body: ReassignBody) => Promise<RecordProblem | null>
  onClose: () => void
}

interface ReassignValues {
  kind: 'role' | 'user'
  role?: string
  /** The picked person, with the name shown, so it stays after a search drops it from the options. */
  user?: { value: string; label: string }
  reason: string
}

const reasonMaxLength = 500

type FieldErrors = Parameters<FormInstance<ReassignValues>['setFields']>[0]

/**
 * The dialog that reassigns a task. It asks for a role of `roles` or a person found through the user
 * picker, and for a reason. The picker lists the first people when it opens and searches again as
 * the user types. A `400` shows each message under the field its pointer names, and a message on
 * the whole body above the form. If the picker cannot load, its list stays empty and the role
 * choice still works. Any other failure shows a generic text. The server checks everything, so the
 * client only blocks what it can see is missing.
 */
export function ReassignDialog({ roles, usersPath, onSend, onClose }: ReassignDialogProps) {
  const t = useText()
  const [form] = Form.useForm<ReassignValues>()
  const kind = Form.useWatch('kind', form)
  const [users, setUsers] = useState<UserOption[]>([])
  const [sending, setSending] = useState(false)
  const [errors, setErrors] = useState<string[]>([])
  const searching = useRef<AbortController>(null)

  const search = useCallback(
    (text: string) => {
      searching.current?.abort()
      const controller = new AbortController()
      searching.current = controller
      searchUsers(usersPath, text, controller.signal)
        .then(setUsers)
        .catch((failure: unknown) => {
          if (!controller.signal.aborted) {
            console.warn(`Searching the users of '${usersPath}' failed`, failure)
            setUsers([])
          }
        })
    },
    [usersPath],
  )

  useEffect(() => {
    if (kind === 'user') {
      search('')
    }
  }, [kind, search])

  useEffect(() => () => searching.current?.abort(), [])

  const showProblem = (problem: RecordProblem) => {
    const fields: FieldErrors = []
    const general: string[] = []
    for (const [pointer, messages] of Object.entries(problem.errors)) {
      if (pointer === '/role') {
        fields.push({ name: 'role', errors: messages })
      } else if (pointer === '/user') {
        fields.push({ name: 'user', errors: messages })
      } else if (pointer === '/reason') {
        fields.push({ name: 'reason', errors: messages })
      } else if (pointer === '') {
        general.push(...messages)
      }
    }
    form.setFields(fields)
    setErrors(general)
  }

  const send = async () => {
    const values = await form.validateFields()
    const body: ReassignBody =
      values.kind === 'role'
        ? { role: values.role!, reason: values.reason }
        : { user: values.user!.value, reason: values.reason }
    setSending(true)
    setErrors([])
    try {
      const problem = await onSend(body)
      if (problem !== null) {
        showProblem(problem)
      }
    } catch (failure) {
      console.warn('Reassigning the task failed', failure)
      setErrors([t('shell.task.commandFailed')])
    } finally {
      setSending(false)
    }
  }

  return (
    <Modal
      open
      title={t('shell.task.reassignTitle')}
      okText={t('shell.task.reassignSend')}
      cancelText={t('shell.task.reassignCancel')}
      confirmLoading={sending}
      onOk={() => void send().catch(() => undefined)}
      onCancel={onClose}
      destroyOnHidden
    >
      <Flex vertical gap="middle">
        {errors.length > 0 && <Alert data-testid="task-reassign-error" type="error" title={errors.join(' ')} />}
        <Form form={form} layout="vertical" initialValues={{ kind: 'role' }}>
          <Form.Item name="kind" label={t('shell.task.reassignTarget')}>
            <Radio.Group
              options={[
                { value: 'role', label: t('shell.task.reassignRole') },
                { value: 'user', label: t('shell.task.reassignPerson') },
              ]}
            />
          </Form.Item>
          {kind === 'user' ? (
            <Form.Item
              name="user"
              label={t('shell.task.reassignPerson')}
              rules={[{ required: true, message: t('shell.task.reassignTargetRequired') }]}
            >
              <Select
                showSearch={{ filterOption: false, onSearch: search }}
                labelInValue
                virtual={false}
                placeholder={t('shell.task.reassignPersonPlaceholder')}
                options={users.map((user) => ({ value: user.id, label: user.displayName }))}
              />
            </Form.Item>
          ) : (
            <Form.Item
              name="role"
              label={t('shell.task.reassignRole')}
              rules={[{ required: true, message: t('shell.task.reassignTargetRequired') }]}
            >
              <Select
                virtual={false}
                options={roles.map((role) => ({
                  value: role.name,
                  label: role.labelKey ? t(role.labelKey) : role.name,
                }))}
              />
            </Form.Item>
          )}
          <Form.Item
            name="reason"
            label={t('shell.task.reassignReason')}
            rules={[
              { required: true, whitespace: true, message: t('shell.task.reassignReasonRequired') },
              { max: reasonMaxLength, message: t('shell.task.reassignReasonTooLong') },
            ]}
          >
            <Input.TextArea rows={3} showCount maxLength={reasonMaxLength} />
          </Form.Item>
        </Form>
      </Flex>
    </Modal>
  )
}
