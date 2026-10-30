import { Alert, Checkbox, Flex, Form, Input, Modal, Typography } from 'antd'
import { useEffect, useState } from 'react'
import { EmptyDash } from '../platform/EmptyValue'
import { fetchCurrentUser } from '../platform/currentUser'
import { useText } from '../platform/texts'
import { fetchUserRoles, inviteUser, type RoleChange, type UserRoles } from '../platform/users'

/** The fields of the form. */
interface InviteFields {
  email: string
  displayName: string
}

type Problem = 'forbidden' | 'conflict' | 'failed'

function sameRole(left: RoleChange, right: RoleChange) {
  return left.applicationId === right.applicationId && left.role === right.role
}

/**
 * The form that invites a person: the email, the display name and optional roles, one section for each active application
 * and one for the platform. The roles come from the roles route, called with the signed-in administrator's own id, and
 * only the roles that can be given are used. When they cannot be loaded, the form says so and still invites without
 * roles. OK validates the form and asks for a confirmation, which names the email, the name and the roles. Confirming
 * calls the invite route. `onInvited` runs after the invitation is sent. A refusal shows its reason and keeps the form
 * open. Any other failure shows a generic text and runs `onReload`, because an invitation whose email failed leaves the
 * account as invited. The page mounts the form only while it is open.
 */
export function InviteUserModal({
  onClose,
  onInvited,
  onReload,
}: {
  onClose: () => void
  onInvited: () => void
  onReload: () => void
}) {
  const t = useText()
  const [form] = Form.useForm<InviteFields>()
  const [roles, setRoles] = useState<Pick<UserRoles, 'applications' | 'platformRoles'>>()
  const [rolesFailed, setRolesFailed] = useState(false)
  const [chosen, setChosen] = useState<RoleChange[]>([])
  const [pending, setPending] = useState<InviteFields | null>(null)
  const [problem, setProblem] = useState<Problem>()
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    async function load() {
      const me = await fetchCurrentUser(controller.signal)
      const result = me ? await fetchUserRoles(me.id, controller.signal) : null
      if (result?.kind === 'ok') {
        setRoles({ applications: result.roles.applications, platformRoles: result.roles.platformRoles })
      } else {
        setRolesFailed(true)
      }
    }
    load().catch((error: unknown) => {
      if (!controller.signal.aborted) {
        console.warn('Loading the roles to give failed', error)
        setRolesFailed(true)
      }
    })
    return () => controller.abort()
  }, [])

  const platformTitle = t('shell.users.roles.platform')
  const sections = roles
    ? [
        ...roles.applications.map((application) => ({
          key: application.id,
          title: application.name,
          applicationId: application.id as string | undefined,
          roles: application.roles,
        })),
        { key: 'platform', title: platformTitle, applicationId: undefined, roles: roles.platformRoles },
      ]
    : []

  function placeOf(change: RoleChange) {
    return change.applicationId === undefined
      ? platformTitle
      : (roles?.applications.find((application) => application.id === change.applicationId)?.name ??
          change.applicationId)
  }

  function toggle(change: RoleChange, checked: boolean) {
    setChosen((current) => (checked ? [...current, change] : current.filter((existing) => !sameRole(existing, change))))
  }

  async function send() {
    if (!pending) {
      return
    }
    setBusy(true)
    setProblem(undefined)
    try {
      const result = await inviteUser({ email: pending.email, displayName: pending.displayName, roles: chosen })
      if (result === 'ok') {
        onInvited()
        return
      }
      setProblem(result)
    } catch (error: unknown) {
      console.warn('Inviting the person failed', error)
      setProblem('failed')
      onReload()
    } finally {
      setBusy(false)
      setPending(null)
    }
  }

  return (
    <>
      <Modal
        open
        title={t('shell.users.invite.title')}
        okText={t('shell.users.invite')}
        cancelText={t('shell.users.roles.cancel')}
        onOk={() =>
          void form.validateFields().then(
            (values) => setPending({ email: values.email.trim(), displayName: values.displayName.trim() }),
            () => {},
          )
        }
        onCancel={onClose}
      >
        <Flex vertical gap="middle">
          {problem && (
            <Alert data-testid="users-invite-problem" type="error" title={t(`shell.users.invite.${problem}`)} />
          )}
          {rolesFailed && (
            <Alert data-testid="users-invite-roles-failed" type="warning" title={t('shell.users.invite.rolesFailed')} />
          )}
          <Form form={form} layout="vertical" name="invite" requiredMark={false}>
            <Form.Item
              name="email"
              label={t('shell.users.invite.email')}
              rules={[{ required: true, whitespace: true }, { type: 'email' }]}
            >
              <Input type="email" maxLength={200} />
            </Form.Item>
            <Form.Item
              name="displayName"
              label={t('shell.users.invite.displayName')}
              rules={[{ required: true, whitespace: true }]}
            >
              <Input maxLength={200} />
            </Form.Item>
          </Form>
          {sections.length > 0 && (
            <Flex vertical gap="small">
              <Typography.Text strong>{t('shell.users.invite.roles')}</Typography.Text>
              {sections.map((section) => (
                <section key={section.key} aria-label={section.title}>
                  <Typography.Title level={5}>{section.title}</Typography.Title>
                  <Flex vertical gap="small">
                    {section.roles.map((role) => {
                      const change: RoleChange =
                        section.applicationId === undefined ? { role } : { applicationId: section.applicationId, role }
                      return (
                        <Checkbox
                          key={role}
                          checked={chosen.some((existing) => sameRole(existing, change))}
                          onChange={(event) => toggle(change, event.target.checked)}
                        >
                          {role}
                        </Checkbox>
                      )
                    })}
                  </Flex>
                </section>
              ))}
            </Flex>
          )}
        </Flex>
      </Modal>
      <Modal
        open={pending !== null}
        title={t('shell.users.invite.confirm')}
        okText={t('shell.users.invite')}
        cancelText={t('shell.users.roles.cancel')}
        confirmLoading={busy}
        onOk={() => void send()}
        onCancel={() => setPending(null)}
      >
        {pending && (
          <Flex vertical>
            <Typography.Text data-testid="users-invite-confirm">
              {t('shell.users.invite.email')}: {pending.email}
            </Typography.Text>
            <Typography.Text>
              {t('shell.users.displayName')}: {pending.displayName}
            </Typography.Text>
            <Typography.Text>
              {t('shell.users.invite.roles')}:{' '}
              {chosen.length === 0 ? (
                <EmptyDash />
              ) : (
                chosen.map((change) => `${change.role} (${placeOf(change)})`).join(', ')
              )}
            </Typography.Text>
          </Flex>
        )}
      </Modal>
    </>
  )
}
