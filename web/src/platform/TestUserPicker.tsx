import { Button, Dropdown, type MenuProps } from 'antd'
import type { MenuItemType } from 'antd/es/menu/interface'
import { useEffect, useState } from 'react'
import { fetchCurrentUser, fetchTestUsers, signIn, signOut, type TestUser } from './testUsers'
import { useText } from './texts'

interface Users {
  list: TestUser[]
  current: TestUser | null
}

// Ant Design's item type does not declare these attributes, but the menu passes them to the item element.
type UserItem = MenuItemType & { role: 'menuitemradio'; 'aria-checked': boolean }

/**
 * The development test user picker in the shell header: the signed-in user's name, or a sign-in
 * prompt, with a menu to switch user or sign out. Either action reloads the page. It renders
 * nothing when the server does not offer the test user list, as in Production.
 */
export function TestUserPicker({ reload = () => window.location.reload() }: { reload?: () => void }) {
  const t = useText()
  const [users, setUsers] = useState<Users | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    Promise.all([fetchTestUsers(controller.signal), fetchCurrentUser(controller.signal)])
      .then(([list, current]) => {
        if (list !== null) {
          setUsers({ list, current })
        }
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn('Loading the test users failed', error)
        }
      })
    return () => controller.abort()
  }, [])

  if (users === null) {
    return null
  }

  const act = (action: () => Promise<void>) => {
    action().then(reload, (error: unknown) => console.warn('Switching the test user failed', error))
  }

  const userItems: UserItem[] = users.list.map((user) => ({
    key: `user:${user.id}`,
    label: user.displayName,
    role: 'menuitemradio',
    'aria-checked': user.id === users.current?.id,
    onClick: () => act(() => signIn(user.id)),
  }))
  const items: MenuProps['items'] =
    users.current === null
      ? userItems
      : [
          ...userItems,
          { type: 'divider' },
          { key: 'signOut', label: t('shell.user.signOut'), onClick: () => act(signOut) },
        ]

  return (
    <Dropdown trigger={['click']} menu={{ items, 'aria-label': t('shell.user.label') }}>
      <Button type="text">{users.current?.displayName ?? t('shell.user.signIn')}</Button>
    </Dropdown>
  )
}
