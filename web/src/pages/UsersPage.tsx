import { Alert, Button, Flex, Input, Modal, Result, Table, Tag, Typography } from 'antd'
import { useEffect, useState } from 'react'
import { EmptyDash } from '../platform/EmptyValue'
import { PageLayout } from '../platform/PageLayout'
import { useText } from '../platform/texts'
import { fetchUsers, resendInvitation, type UserList, type UserListItem, type UserListResult } from '../platform/users'
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

const resendProblemKeys = {
  forbidden: 'shell.users.forbidden',
  conflict: 'shell.users.resend.conflict',
  failed: 'shell.users.invite.failed',
}

/**
 * The tenant's users for an administrator: a table that is searched by name or email and paged by the
 * server. A search starts on Enter or on the search button and goes back to page 1. Clicking a row opens a side panel
 * with the person's roles. The Invite button opens the form that invites a person. An invited account has a Resend
 * invitation button, which asks for a confirmation. Both reload the list, because a resend gives the person a new id.
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
  const [resending, setResending] = useState<UserListItem | null>(null)
  const [busy, setBusy] = useState(false)
  const [done, setDone] = useState<'invite' | 'resend'>()
  const [problem, setProblem] = useState<'forbidden' | 'conflict' | 'failed'>()
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

  async function resend() {
    if (!resending) {
      return
    }
    setBusy(true)
    setDone(undefined)
    setProblem(undefined)
    try {
      const result = await resendInvitation(resending.id)
      if (result === 'ok') {
        setDone('resend')
      } else {
        setProblem(result)
      }
    } catch (error: unknown) {
      console.warn('Resending the invitation failed', error)
      setProblem('failed')
    } finally {
      setBusy(false)
      setResending(null)
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
          {problem && <Alert data-testid="users-command-problem" type="error" title={t(resendProblemKeys[problem])} />}
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
                // Brokered users get no button: the resend route answers 409 for them.
                render: (_: unknown, row: UserListItem) =>
                  row.status === 'invited' &&
                  row.provider === null && (
                    <Button
                      size="small"
                      onClick={(event) => {
                        event.stopPropagation()
                        setResending(row)
                      }}
                    >
                      {t('shell.users.resend')}
                    </Button>
                  ),
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
        open={resending !== null}
        title={t('shell.users.resend.confirm')}
        okText={t('shell.users.resend.ok')}
        cancelText={t('shell.users.roles.cancel')}
        confirmLoading={busy}
        onOk={() => void resend()}
        onCancel={() => setResending(null)}
      >
        {resending && (
          <Flex vertical>
            <Typography.Text data-testid="users-resend-confirm">
              {t('shell.users.roles.person')}: {resending.displayName}
            </Typography.Text>
            <Typography.Text>
              {t('shell.users.email')}: {resending.email}
            </Typography.Text>
          </Flex>
        )}
      </Modal>
    </PageLayout>
  )
}
