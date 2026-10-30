import { Alert, Input, Result, Table, Tag } from 'antd'
import { useEffect, useState } from 'react'
import { EmptyDash } from '../platform/EmptyValue'
import { PageLayout } from '../platform/PageLayout'
import { useText } from '../platform/texts'
import { fetchUsers, type UserList, type UserListItem, type UserListResult } from '../platform/users'
import { usePlatform } from './context'
import { NotFoundPage } from './NotFoundPage'
import { UserRolesDrawer } from './UserRolesDrawer'

const pageSize = 20

const statusColors: Record<UserListItem['status'], string> = {
  invited: 'blue',
  active: 'green',
  disabled: 'default',
}

/**
 * The tenant's users for an administrator: a table that is searched by name or email and paged by the
 * server. A search starts on Enter or on the search button and goes back to page 1. Clicking a row opens a side panel
 * with the person's roles.
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
  }, [search, page])

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
            ]}
          />
        </>
      )}
      <UserRolesDrawer user={selected} onClose={() => setSelected(null)} />
    </PageLayout>
  )
}
