import { Alert, Button, Flex, Input, Modal, Result, Table, Tag, Typography } from 'antd'
import { useEffect, useState } from 'react'
import { EmptyDash } from '../platform/EmptyValue'
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
import { usePlatform } from './context'
import { InviteUserModal } from './InviteUserModal'
import { NotFoundPage } from './NotFoundPage'
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
 * resend gives the person a new id.
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
          <Flex gap="small">
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
          </Flex>
          {done && <Alert data-testid="users-done" type="success" title={t(`shell.users.${done}.done`)} />}
          {problem && <Alert data-testid="users-command-problem" type="error" title={t(problem)} />}
          <Table<UserListItem>
            style={{ width: '100%' }}
            rowKey="id"
            dataSource={list.items}
            onRow={(row) => ({ onClick: () => setSelected(row), style: { cursor: 'pointer' } })}
            pagination={{ current: page, pageSize, total: list.totalCount, showSizeChanger: false, onChange: setPage }}
            columns={[
              { title: t('shell.users.displayName'), dataIndex: 'displayName' },
              {
                title: t('shell.users.email'),
                dataIndex: 'email',
                render: (value: string | null) => value ?? <EmptyDash />,
              },
              {
                title: t('shell.users.signIn'),
                dataIndex: 'provider',
                render: (value: string | null) => value ?? t('shell.users.account'),
              },
              {
                title: t('shell.users.status'),
                dataIndex: 'status',
                render: (value: UserListItem['status']) => (
                  <Tag color={statusColors[value]}>{t(`shell.users.status.${value}`)}</Tag>
                ),
              },
              {
                title: t('shell.users.lastSignedInAt'),
                dataIndex: 'lastSignedInAt',
                render: (value: string | null) => (value ? new Date(value).toLocaleString(locale) : <EmptyDash />),
              },
              {
                // Brokered users get no Resend and no Reset: those routes answer 409 for them.
                render: (_: unknown, row: UserListItem) => {
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
