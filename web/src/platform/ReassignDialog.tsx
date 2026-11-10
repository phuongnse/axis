import { Flex, Form, Input, Modal, Radio, Select } from 'antd'
import { AxisAlert } from './AxisAlert'
import { AxisField } from './AxisField'
import { useCallback, useEffect, useRef, useState } from 'react'
import { placeProblemErrors } from './problemErrors'
import type { RecordProblem } from './records'
import { requiredMark } from './requiredMark'
import { searchUsers, type ReassignBody, type ReassignRole, type UserOption } from './tasks'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'

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

/**
 * The dialog that reassigns a task. It asks for a role of `roles` or a person found through the user
 * picker, and for a reason. The picker lists the first people when it opens and searches again as
 * the user types. A `400` shows each message under the field its pointer names, and any other message
 * above the form, such as one for the target that is not chosen. If the picker cannot load, its list
 * stays empty and the role choice still works. Any other failure shows a generic text. The server
 * checks everything, so the client only blocks what it can see is missing.
 */
export function ReassignDialog({ roles, usersPath, onSend, onClose }: ReassignDialogProps) {
  const t = useText()
  const { size } = useDesignTokens()
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

  const showProblem = (problem: RecordProblem, kind: ReassignValues['kind']) => {
    const placed = placeProblemErrors<'reason' | 'role' | 'user'>(problem.errors, ([name, ...rest]) =>
      rest.length === 0 && (name === 'reason' || name === kind) ? name : null,
    )
    form.setFields(placed.fields.map(({ field, messages }) => ({ name: field, errors: messages })))
    setErrors(placed.form)
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
        showProblem(problem, values.kind)
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
      width={size.dialog.medium}
      title={t('shell.task.reassignTitle')}
      okText={t('shell.task.reassignSend')}
      cancelText={t('shell.task.reassignCancel')}
      confirmLoading={sending}
      onOk={() => void send().catch(() => undefined)}
      onCancel={onClose}
      destroyOnHidden
    >
      <Flex vertical gap="middle">
        {errors.length > 0 && <AxisAlert data-testid="task-reassign-error" kind="error" message={errors.join(' ')} />}
        <Form form={form} layout="vertical" requiredMark={requiredMark} initialValues={{ kind: 'role' }}>
          <AxisField name="kind" label={t('shell.task.reassignTarget')} help={t('shell.task.reassignTarget.help')}>
            <Radio.Group
              role="radiogroup"
              aria-label={t('shell.task.reassignTarget')}
              options={[
                { value: 'role', label: t('shell.task.reassignRole') },
                { value: 'user', label: t('shell.task.reassignPerson') },
              ]}
            />
          </AxisField>
          {kind === 'user' ? (
            <AxisField
              name="user"
              label={t('shell.task.reassignPerson')}
              help={t('shell.task.reassignPerson.help')}
              requiredMessage={t('shell.task.reassignTargetRequired')}
            >
              <Select
                showSearch={{ filterOption: false, onSearch: search }}
                labelInValue
                virtual={false}
                options={users.map((user) => ({ value: user.id, label: user.displayName }))}
              />
            </AxisField>
          ) : (
            <AxisField
              name="role"
              label={t('shell.task.reassignRole')}
              help={t('shell.task.reassignRole.help')}
              requiredMessage={t('shell.task.reassignTargetRequired')}
            >
              <Select
                virtual={false}
                options={roles.map((role) => ({
                  value: role.name,
                  label: role.labelKey ? t(role.labelKey) : role.name,
                }))}
              />
            </AxisField>
          )}
          <AxisField
            name="reason"
            label={t('shell.task.reassignReason')}
            help={t('shell.task.reassignReason.help')}
            requiredMessage={t('shell.task.reassignReasonRequired')}
            rules={[{ max: reasonMaxLength, message: t('shell.task.reassignReasonTooLong') }]}
          >
            <Input.TextArea rows={3} showCount maxLength={reasonMaxLength} />
          </AxisField>
        </Form>
      </Flex>
    </Modal>
  )
}
