import { Flex, Modal, Result, Tag, Typography } from 'antd'
import { AxisAlert } from '../platform/AxisAlert'
import { useEffect, useState } from 'react'
import { useParams } from 'react-router'
import { AxisButton } from '../platform/AxisButton'
import { PageLayout } from '../platform/PageLayout'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { changeUserRole, fetchUserRoles, type RoleChange, type UserRoles } from '../platform/users'
import { usePlatform } from './context'
import { NotFoundPage } from './NotFoundPage'

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
 * The page of one user, opened from the name on the users page. Its title is the person's display name, with the email
 * under it. Below are the roles given to the person and the roles that can be given, one section for each active
 * application and one for the platform. A role the active release no longer declares is tagged. Give and Remove ask for
 * a confirmation, then call the role routes and load the roles again. A refusal shows its reason. The page loads
 * everything from the roles route, so a reload or a shared link shows it. A person without user administration sees that
 * the page is for administrators, and an unknown user shows the not-found page.
 */
export function UserPage() {
  const t = useText()
  const platform = usePlatform()
  const { colour, size } = useDesignTokens()
  const { id = '' } = useParams()
  const [roles, setRoles] = useState<UserRoles>()
  const [refusal, setRefusal] = useState<'forbidden' | 'notFound'>()
  const [problem, setProblem] = useState<Problem>()
  const [pending, setPending] = useState<Pending | null>(null)
  const [busy, setBusy] = useState(false)
  // Counts the changes, so each one loads the roles again.
  const [changes, setChanges] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    fetchUserRoles(id, controller.signal).then(
      (result) => {
        if (result.kind === 'ok') {
          setRefusal(undefined)
          setRoles(result.roles)
        } else {
          setRefusal(result.kind)
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
  }, [id, changes])

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
      const result = await changeUserRole(id, pending.give, change)
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

  if (refusal === 'notFound') {
    return <NotFoundPage />
  }

  const sections = roles ? sectionsOf(roles, t('shell.users.roles.platform')) : []

  return (
    <PageLayout
      title={roles?.displayName ?? t('shell.users.title')}
      breadcrumb={[
        { label: t(platform.site.titleKey), to: '/' },
        { label: t('shell.users.title'), to: '/users' },
        { label: roles?.displayName ?? id },
      ]}
    >
      {refusal === 'forbidden' && (
        <div data-testid="users-forbidden">
          <Result status="403" title={t('shell.users.forbidden')} />
        </div>
      )}
      {!refusal && (
        <Flex vertical gap="middle" data-testid="user-roles">
          {roles && (
            <Typography.Text data-testid="user-email">
              {t('shell.users.email')}: {roles.email ?? t('shell.value.notSet')}
            </Typography.Text>
          )}
          {problem && (
            <AxisAlert data-testid="user-roles-problem" kind="error" message={t(`shell.users.roles.${problem}`)} />
          )}
          {sections.map((section) => (
            <section key={section.key} data-testid="user-roles-section" aria-label={section.title}>
              <Typography.Title level={5}>{section.title}</Typography.Title>
              <Flex vertical gap="small">
                {section.rows.map((row) => (
                  <Flex key={row.role} align="center" justify="space-between" gap="small" data-testid="user-role">
                    <Flex align="center" gap="small">
                      <Typography.Text>{row.role}</Typography.Text>
                      {row.given && <Tag style={{ color: colour.successText }}>{t('shell.users.roles.given')}</Tag>}
                      {!row.declared && (
                        <Tag style={{ color: colour.warningText }}>{t('shell.users.roles.notDeclared')}</Tag>
                      )}
                    </Flex>
                    {(row.given || row.declared) && (
                      <AxisButton
                        role={row.given ? 'destructive' : 'secondary'}
                        disabled={busy}
                        onClick={() => setPending({ give: !row.given, section, role: row.role })}
                      >
                        {t(row.given ? 'shell.users.roles.remove' : 'shell.users.roles.give')}
                      </AxisButton>
                    )}
                  </Flex>
                ))}
              </Flex>
            </section>
          ))}
        </Flex>
      )}
      <Modal
        open={pending !== null}
        width={size.dialog.small}
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
              {t('shell.users.roles.person')}: {roles?.displayName}
            </Typography.Text>
          </Flex>
        )}
      </Modal>
    </PageLayout>
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
