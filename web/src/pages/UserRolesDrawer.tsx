import { Alert, Button, Drawer, Flex, Modal, Tag, Typography } from 'antd'
import { useEffect, useState } from 'react'
import { useText } from '../platform/texts'
import { changeUserRole, fetchUserRoles, type RoleChange, type UserListItem, type UserRoles } from '../platform/users'

/** One role of a section: given or not, and declared by the active release or not. */
interface RoleRow {
  role: string
  given: boolean
  declared: boolean
}

/** A place where roles are given: an application or the platform. `applicationId` is `null` for the platform. */
interface Section {
  key: string
  title: string
  applicationId: string | null
  rows: RoleRow[]
}

/** A role the administrator asked to give or remove, waiting for the confirmation. */
interface Pending {
  give: boolean
  section: Section
  role: string
}

type Problem = 'forbidden' | 'conflict' | 'loadFailed' | 'changeFailed'

/**
 * The side panel of one user: the roles given to the person and the roles that can be given, one section for each
 * active application and one for the platform. A role the active release no longer declares is tagged. Give and
 * Remove ask for a confirmation, then call the role routes and load the roles again. A refusal shows its reason.
 */
export function UserRolesDrawer({ user, onClose }: { user: UserListItem | null; onClose: () => void }) {
  return (
    <Drawer open={user !== null} title={user?.displayName} size={480} onClose={onClose} data-testid="user-roles">
      {user && <UserRolesPanel key={user.id} user={user} />}
    </Drawer>
  )
}

function UserRolesPanel({ user }: { user: UserListItem }) {
  const t = useText()
  const [roles, setRoles] = useState<UserRoles>()
  const [problem, setProblem] = useState<Problem>()
  const [pending, setPending] = useState<Pending | null>(null)
  const [busy, setBusy] = useState(false)
  // Counts the changes, so each one loads the roles again.
  const [changes, setChanges] = useState(0)
  const userId = user.id

  useEffect(() => {
    const controller = new AbortController()
    fetchUserRoles(userId, controller.signal).then(
      (result) => {
        if (result.kind === 'ok') {
          setRoles(result.roles)
        } else {
          setProblem('loadFailed')
        }
      },
      (error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn('Loading the roles failed', error)
          setProblem('loadFailed')
        }
      },
    )
    return () => controller.abort()
  }, [userId, changes])

  async function confirm() {
    if (!pending) {
      return
    }
    const change: RoleChange =
      pending.section.applicationId === null
        ? { role: pending.role }
        : { applicationId: pending.section.applicationId, role: pending.role }
    setBusy(true)
    setProblem(undefined)
    try {
      const result = await changeUserRole(user.id, pending.give, change)
      if (result !== 'ok') {
        setProblem(result)
      }
    } catch (error: unknown) {
      console.warn('Changing the role failed', error)
      setProblem('changeFailed')
    } finally {
      setBusy(false)
      setPending(null)
    }
    setChanges((count) => count + 1)
  }

  const sections = roles ? sectionsOf(roles, t('shell.users.roles.platform')) : []

  return (
    <>
      <Flex vertical gap="middle">
        {problem && <Alert data-testid="user-roles-problem" type="error" title={t(`shell.users.roles.${problem}`)} />}
        {sections.map((section) => (
          <section key={section.key} data-testid="user-roles-section" aria-label={section.title}>
            <Typography.Title level={5}>{section.title}</Typography.Title>
            <Flex vertical gap="small">
              {section.rows.map((row) => (
                <Flex key={row.role} align="center" justify="space-between" gap="small" data-testid="user-role">
                  <Flex align="center" gap="small">
                    <Typography.Text>{row.role}</Typography.Text>
                    {row.given && <Tag color="green">{t('shell.users.roles.given')}</Tag>}
                    {!row.declared && <Tag color="orange">{t('shell.users.roles.notDeclared')}</Tag>}
                  </Flex>
                  {(row.given || row.declared) && (
                    <Button
                      size="small"
                      danger={row.given}
                      disabled={busy}
                      onClick={() => setPending({ give: !row.given, section, role: row.role })}
                    >
                      {t(row.given ? 'shell.users.roles.remove' : 'shell.users.roles.give')}
                    </Button>
                  )}
                </Flex>
              ))}
            </Flex>
          </section>
        ))}
      </Flex>
      <Modal
        open={pending !== null}
        title={pending && t(pending.give ? 'shell.users.roles.confirmGive' : 'shell.users.roles.confirmRemove')}
        okText={pending && t(pending.give ? 'shell.users.roles.give' : 'shell.users.roles.remove')}
        cancelText={t('shell.users.roles.cancel')}
        confirmLoading={busy}
        onOk={() => void confirm()}
        onCancel={() => setPending(null)}
      >
        {pending && (
          <Flex vertical>
            <Typography.Text data-testid="user-roles-confirm">
              {t('shell.users.roles.role')}: {pending.role}
            </Typography.Text>
            <Typography.Text>
              {t('shell.users.roles.place')}: {pending.section.title}
            </Typography.Text>
            <Typography.Text>
              {t('shell.users.roles.person')}: {user.displayName}
            </Typography.Text>
          </Flex>
        )}
      </Modal>
    </>
  )
}

/**
 * One section for each active application, one for the given roles of an application that is not active, and one for the
 * platform. A section lists the declared roles and then the given roles that are not declared.
 */
function sectionsOf(roles: UserRoles, platformTitle: string): Section[] {
  const sections: Section[] = roles.applications.map((application) => ({
    key: application.id,
    title: application.name,
    applicationId: application.id,
    rows: rowsOf(
      application.roles,
      roles.given.filter((row) => row.applicationId === application.id),
    ),
  }))
  const unknown = new Set(
    roles.given
      .map((row) => row.applicationId)
      .filter((id): id is string => id !== null && !roles.applications.some((application) => application.id === id)),
  )
  for (const id of unknown) {
    sections.push({
      key: id,
      title: id,
      applicationId: id,
      rows: rowsOf(
        [],
        roles.given.filter((row) => row.applicationId === id),
      ),
    })
  }
  sections.push({
    key: 'platform',
    title: platformTitle,
    applicationId: null,
    rows: rowsOf(
      roles.platformRoles,
      roles.given.filter((row) => row.applicationId === null),
    ),
  })
  return sections
}

function rowsOf(declared: string[], given: UserRoles['given']): RoleRow[] {
  const rows: RoleRow[] = declared.map((role) => ({
    role,
    given: given.some((row) => row.role === role),
    declared: true,
  }))
  for (const row of given) {
    if (!rows.some((existing) => existing.role === row.role)) {
      rows.push({ role: row.role, given: true, declared: row.declared })
    }
  }
  return rows
}
