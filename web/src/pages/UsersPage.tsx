import { Button, Flex, Modal, Result, Tabs, Typography } from 'antd'
import { AxisAlert } from '../platform/AxisAlert'
import { useEffect, useMemo, useState } from 'react'
import { AxisTable, type AxisColumn, type AxisRowAction } from '../platform/AxisTable'
import { PageLayout } from '../platform/PageLayout'
import { PersonName } from '../platform/PersonName'
import { SectionParts } from '../platform/SectionParts'
import { recordPageSize, recordQuery, tablePageSizes } from '../platform/tableQuery'
import { useText } from '../platform/texts'
import { fetchTenantOutsideClients, type TenantOutsideClients } from '../platform/tenantOutsideClients'
import { StatusTag, type StatusTone } from '../platform/StatusTag'
import { useDesignTokens } from '../platform/themeMode'
import { useTableView } from '../platform/useTableView'
import {
  fetchUsers,
  resendInvitation,
  runUserCommand,
  type UserCommand,
  type UserList,
  type UserListItem,
  type UserListResult,
} from '../platform/users'
import { ConnectedAppsTab } from './ConnectedAppsTab'
import { usePlatform } from './context'
import { InviteUserModal } from './InviteUserModal'
import { NotFoundPage } from './NotFoundPage'
import { TenantApplicationPackagesSection } from './TenantApplicationPackagesSection'
import { TenantExportSection } from './TenantExportSection'
import { TenantHostsSection } from './TenantHostsSection'
import { TenantModelKeySection } from './TenantModelKeySection'
import { TenantUsageSection } from './TenantUsageSection'

/** The columns the view's filter and sort can name, in table order, see `docs/reference/user-administration.md#read-routes`. */
const userFields = [
  { name: 'displayName', type: 'text' },
  { name: 'email', type: 'text' },
  { name: 'provider', type: 'text' },
  { name: 'status', type: 'enum' },
  { name: 'lastSignedInAt', type: 'date' },
] as const
// The people table has no parameters of its own. A stable empty array keeps useTableView from recomputing on each render.
const noParameters: readonly string[] = []

const userStatuses: UserListItem['status'][] = ['invited', 'active', 'disabled']

const statusTones: Record<UserListItem['status'], StatusTone> = {
  invited: 'attention',
  active: 'neutral',
  disabled: 'muted',
}

type Command = 'resend' | 'disable' | 'enable' | 'reset'

const commandFailedKey = 'shell.users.command.failed'

const resendProblemKeys = {
  forbidden: 'shell.users.forbidden',
  conflict: 'shell.users.resend.conflict',
}

// Only disable names its 403 and 409. Enable and reset have no row that can reach either one.
const disableProblemKeys = {
  forbidden: 'shell.users.disable.forbidden',
  conflict: 'shell.users.disable.conflict',
}

const userCommands: Record<Exclude<Command, 'resend'>, UserCommand> = {
  disable: 'disable',
  enable: 'enable',
  reset: 'reset-authenticator',
}

/**
 * The tenant's users for an administrator: a table that the server searches, filters, sorts and pages. It has Search, which
 * looks in the name and the email, Filter, Columns, a sort on every column, and a pager with a page size. All of them live in
 * the address, so reload and Back keep them, and a new search, filter or sort goes back to page 1, see
 * docs/reference/platform-pages.md#the-list and docs/reference/actions-and-controls.md#tables. The display name is drawn by `PersonName`: a link
 * to the person's page, which shows their roles, followed by (you) on the administrator's own row. The Invite button opens the form that invites a person. A row's commands
 * are entries of its "…" row-actions menu. An invited account has Resend invitation. A row that is not disabled has Disable,
 * a disabled row has Enable, and an account has Reset authenticator. Each of these asks for a confirmation, then reloads
 * the list, because a resend gives the person a new id. The page has two tabs. People holds the list and, below it, the
 * Export data and Usage sections for an administrator in a cell tenant and the Model key section where the installation
 * allows tenant keys. Connected apps holds the outside MCP clients switch and the apps with consent, and shows only when
 * `GET /api/tenant/outside-clients` answers `200`. See docs/reference/platform-pages.md#connected-apps.
 */
export function UsersPage() {
  const t = useText()
  const { size } = useDesignTokens()
  const platform = usePlatform()
  const { locale } = platform.current
  const { view, setView, saved } = useTableView(userFields, recordPageSize, noParameters, true, true, true, '', 'users')
  const query = recordQuery(view)
  const [list, setList] = useState<UserList>()
  const [refusal, setRefusal] = useState<Exclude<UserListResult['kind'], 'ok'>>()
  const [loadFailed, setLoadFailed] = useState(false)
  const [inviting, setInviting] = useState(false)
  const [pending, setPending] = useState<{ command: Command; user: UserListItem } | null>(null)
  const [busy, setBusy] = useState(false)
  const [done, setDone] = useState<'invite' | Command>()
  const [problem, setProblem] = useState<string>()
  // Counts the commands, so each one loads the list again.
  const [reloads, setReloads] = useState(0)
  const [outsideClients, setOutsideClients] = useState<TenantOutsideClients | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    fetchTenantOutsideClients(controller.signal).then(
      (answer) => {
        if (!controller.signal.aborted) {
          setOutsideClients(answer)
        }
      },
      (error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn('Loading the outside clients setting failed', error)
        }
      },
    )
    return () => controller.abort()
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    fetchUsers(query, controller.signal).then(
      (result) => {
        setLoadFailed(false)
        if (result.kind === 'ok') {
          setRefusal(undefined)
          setList(result.list)
        } else {
          setRefusal(result.kind)
        }
      },
      (error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn('Loading the users failed', error)
          setLoadFailed(true)
        }
      },
    )
    return () => controller.abort()
  }, [query, reloads])

  async function run() {
    if (!pending) {
      return
    }
    const { command, user } = pending
    setBusy(true)
    setDone(undefined)
    setProblem(undefined)
    try {
      const result =
        command === 'resend' ? await resendInvitation(user.id) : await runUserCommand(user.id, userCommands[command])
      if (result === 'ok') {
        setDone(command)
      } else if (command === 'resend') {
        setProblem(resendProblemKeys[result])
      } else {
        setProblem(command === 'disable' ? disableProblemKeys[result] : commandFailedKey)
      }
    } catch (error: unknown) {
      console.warn(`Running ${command} on a person failed`, error)
      setProblem(command === 'resend' ? 'shell.users.invite.failed' : commandFailedKey)
    } finally {
      setBusy(false)
      setPending(null)
    }
    setReloads((count) => count + 1)
  }

  const columns = useMemo<AxisColumn<UserListItem>[]>(() => {
    const statusOptions = userStatuses.map((value) => ({ value, label: t(`shell.users.status.${value}`) }))
    return [
      {
        key: 'displayName',
        kind: 'freeText',
        title: t('shell.users.displayName'),
        sorter: true,
        filter: { kind: 'text' },
        render: (_value, row) => <PersonName id={row.id} name={row.displayName} />,
      },
      {
        key: 'email',
        kind: 'freeText',
        title: t('shell.users.email'),
        sorter: true,
        filter: { kind: 'text' },
        render: (_value, row) => row.email,
      },
      {
        key: 'provider',
        kind: 'fixed',
        title: t('shell.users.signIn'),
        sorter: true,
        filter: { kind: 'text' },
        render: (_value, row) => row.provider ?? t('shell.users.account'),
      },
      {
        key: 'status',
        kind: 'fixed',
        title: t('shell.users.status'),
        sorter: true,
        filter: { kind: 'enum', options: statusOptions },
        render: (_value, row) => (
          <StatusTag tone={statusTones[row.status]}>{t(`shell.users.status.${row.status}`)}</StatusTag>
        ),
      },
      {
        key: 'lastSignedInAt',
        kind: 'fixed',
        title: t('shell.users.lastSignedInAt'),
        sorter: true,
        filter: { kind: 'date' },
        render: (_value, row) => (row.lastSignedInAt ? new Date(row.lastSignedInAt).toLocaleString(locale) : null),
      },
    ]
  }, [locale, t])

  // Brokered users get no Resend and no Reset: those routes answer 409 for them.
  function rowActions(row: UserListItem): AxisRowAction[] {
    const commands: Command[] = []
    if (row.status === 'invited' && row.provider === null) {
      commands.push('resend')
    }
    commands.push(row.status === 'disabled' ? 'enable' : 'disable')
    if (row.provider === null) {
      commands.push('reset')
    }
    return commands.map((command) => ({
      key: command,
      labelKey: `shell.users.${command}`,
      onClick: () => setPending({ command, user: row }),
    }))
  }

  if (refusal === 'notFound') {
    return <NotFoundPage />
  }

  return (
    <PageLayout
      title={t('shell.users.title')}
      breadcrumb={[{ label: t(platform.site.titleKey), to: '/' }, { label: t('shell.users.title') }]}
    >
      {refusal === 'forbidden' && (
        <div data-testid="users-forbidden">
          <Result status="403" title={t('shell.users.forbidden')} />
        </div>
      )}
      {loadFailed && <AxisAlert data-testid="users-error" kind="error" message={t('shell.users.loadFailed')} />}
      {list && !refusal && (
        <Tabs
          defaultActiveKey="people"
          items={[
            {
              key: 'people',
              label: t('shell.users.people'),
              children: (
                <SectionParts>
                  <AxisTable<UserListItem>
                    rowKey={(row) => row.id}
                    dataSource={list.items}
                    emptyText={t('shell.users.empty')}
                    view={{
                      query: view,
                      onChange: setView,
                      saved,
                      pageSizes: tablePageSizes(),
                      total: list.totalCount,
                      search: true,
                      columns: true,
                    }}
                    toolbar={{
                      main: (
                        // oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role.
                        <Button
                          type="primary"
                          onClick={() => {
                            setDone(undefined)
                            setProblem(undefined)
                            setInviting(true)
                          }}
                        >
                          {t('shell.users.invite')}
                        </Button>
                      ),
                    }}
                    below={
                      <>
                        {done && (
                          <AxisAlert data-testid="users-done" kind="success" message={t(`shell.users.${done}.done`)} />
                        )}
                        {problem && <AxisAlert data-testid="users-command-problem" kind="error" message={t(problem)} />}
                      </>
                    }
                    columns={columns}
                    rowActions={{ configured: 3, actions: rowActions }}
                  />
                  <TenantExportSection />
                  <TenantUsageSection />
                  <TenantHostsSection />
                  <TenantModelKeySection />
                  <TenantApplicationPackagesSection />
                </SectionParts>
              ),
            },
            ...(outsideClients
              ? [
                  {
                    key: 'connectedApps',
                    label: t('shell.users.connectedApps.title'),
                    children: <ConnectedAppsTab initial={outsideClients} />,
                  },
                ]
              : []),
          ]}
        />
      )}
      {inviting && (
        <InviteUserModal
          onClose={() => setInviting(false)}
          onInvited={() => {
            setInviting(false)
            setDone('invite')
            setReloads((count) => count + 1)
          }}
          onReload={() => setReloads((count) => count + 1)}
        />
      )}
      <Modal
        open={pending !== null}
        width={size.dialog.small}
        title={pending && t(`shell.users.${pending.command}.confirm`)}
        okText={pending && t(`shell.users.${pending.command}.ok`)}
        cancelText={t('shell.users.roles.cancel')}
        confirmLoading={busy}
        onOk={() => void run()}
        onCancel={() => setPending(null)}
      >
        {pending && (
          <Flex vertical>
            <Typography.Text
              data-testid={pending.command === 'resend' ? 'users-resend-confirm' : 'users-command-confirm'}
            >
              {t('shell.users.roles.person')}: {pending.user.displayName}
            </Typography.Text>
            <Typography.Text>
              {t('shell.users.email')}: {pending.user.email}
            </Typography.Text>
            {(pending.command === 'disable' || pending.command === 'reset') && (
              <Typography.Text>{t(`shell.users.${pending.command}.effect`)}</Typography.Text>
            )}
          </Flex>
        )}
      </Modal>
    </PageLayout>
  )
}
