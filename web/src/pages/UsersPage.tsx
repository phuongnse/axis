import { Alert, Button, Flex, Input, Modal, Result, Tag, Typography } from 'antd'
import { useEffect, useState } from 'react'
import { AxisTable } from '../platform/AxisTable'
import { PageLayout } from '../platform/PageLayout'
import { useText } from '../platform/texts'
import {
  fetchUsers,
  resendInvitation,
  runUserCommand,
  type UserCommand,
  type UserList,
  type UserListItem,
  type UserListResult,
} from '../platform/users'
import { visuallyHidden } from '../platform/visuallyHidden'
import { usePlatform } from './context'
import { InviteUserModal } from './InviteUserModal'
import { NotFoundPage } from './NotFoundPage'
import { TenantExportSection } from './TenantExportSection'
import { UserRolesDrawer } from './UserRolesDrawer'

const pageSize = 20

const statusColors: Record<UserListItem['status'], string> = {
  invited: 'blue',
  active: 'green',
  disabled: 'default',
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
 * The tenant's users for an administrator: a table that is searched by name or email and paged by the
 * server. A search starts on Enter or on the search button and goes back to page 1. Clicking a row opens a side panel
 * with the person's roles. The Invite button opens the form that invites a person. An invited account has a Resend
 * invitation button. A row that is not disabled has a Disable button, a disabled row has an Enable button, and an
 * account has a Reset authenticator button. Each of these asks for a confirmation, then reloads the list, because a
 * resend gives the person a new id. Below the list an administrator in a cell tenant has the Export data section.
 */
export function UsersPage() {
  const t = useText()
  const platform = usePlatform()
  const { locale } = platform.current
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [list, setList] = useState<UserList>()
  const [refusal, setRefusal] = useState<Exclude<UserListResult['kind'], 'ok'>>()
  const [loadFailed, setLoadFailed] = useState(false)
  const [selected, setSelected] = useState<UserListItem | null>(null)
  const [inviting, setInviting] = useState(false)
  const [pending, setPending] = useState<{ command: Command; user: UserListItem } | null>(null)
  const [busy, setBusy] = useState(false)
  const [done, setDone] = useState<'invite' | Command>()
  const [problem, setProblem] = useState<string>()
  // Counts the commands, so each one loads the list again.
  const [reloads, setReloads] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    fetchUsers({ search, page, pageSize }, controller.signal).then(
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
  }, [search, page, reloads])

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
      {loadFailed && <Alert data-testid="users-error" type="error" title={t('shell.users.loadFailed')} />}
      {list && !refusal && (
        <>
          <AxisTable<UserListItem>
            rowKey={(row) => row.id}
            dataSource={list.items}
            emptyText={t('shell.users.empty')}
            pagination={{ current: page, pageSize, total: list.totalCount }}
            onChange={(pagination) => setPage(pagination.current ?? 1)}
            rowAttributes={(row) => ({ onClick: () => setSelected(row), style: { cursor: 'pointer' } })}
            toolbar={{
              view: (
                <Input.Search
                  allowClear
                  maxLength={200}
                  aria-label={t('shell.users.search')}
                  placeholder={t('shell.users.search')}
                  style={{ maxWidth: 400 }}
                  onSearch={(value) => {
                    setSearch(value.trim())
                    setPage(1)
                  }}
                />
              ),
              main: (
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
                {done && <Alert data-testid="users-done" type="success" title={t(`shell.users.${done}.done`)} />}
                {problem && <Alert data-testid="users-command-problem" type="error" title={t(problem)} />}
              </>
            }
            columns={[
              { key: 'displayName', kind: 'freeText', title: t('shell.users.displayName'), dataIndex: 'displayName' },
              { key: 'email', kind: 'freeText', title: t('shell.users.email'), render: (_value, row) => row.email },
              {
                key: 'provider',
                kind: 'fixed',
                title: t('shell.users.signIn'),
                render: (_value, row) => row.provider ?? t('shell.users.account'),
              },
              {
                key: 'status',
                kind: 'fixed',
                title: t('shell.users.status'),
                render: (_value, row) => (
                  <Tag color={statusColors[row.status]}>{t(`shell.users.status.${row.status}`)}</Tag>
                ),
              },
              {
                key: 'lastSignedInAt',
                kind: 'fixed',
                title: t('shell.users.lastSignedInAt'),
                render: (_value, row) =>
                  row.lastSignedInAt ? new Date(row.lastSignedInAt).toLocaleString(locale) : null,
              },
              {
                key: 'commands',
                kind: 'fixed',
                title: <span style={visuallyHidden}>{t('shell.table.rowActions')}</span>,
                render: (_value, row) => {
                  // Brokered users get no Resend and no Reset: those routes answer 409 for them.
                  const buttons: Command[] = []
                  if (row.status === 'invited' && row.provider === null) {
                    buttons.push('resend')
                  }
                  buttons.push(row.status === 'disabled' ? 'enable' : 'disable')
                  if (row.provider === null) {
                    buttons.push('reset')
                  }
                  return (
                    <Flex gap="small">
                      {buttons.map((command) => (
                        <Button
                          key={command}
                          size="small"
                          onClick={(event) => {
                            event.stopPropagation()
                            setPending({ command, user: row })
                          }}
                        >
                          {t(`shell.users.${command}`)}
                        </Button>
                      ))}
                    </Flex>
                  )
                },
              },
            ]}
          />
          <TenantExportSection />
        </>
      )}
      <UserRolesDrawer user={selected} onClose={() => setSelected(null)} />
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
