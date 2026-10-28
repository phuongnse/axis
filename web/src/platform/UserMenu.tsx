import { Button, Dropdown, type MenuProps } from 'antd'
import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { startSignIn } from './api'
import { fetchCurrentUser, signOutSession, type CurrentUser } from './currentUser'
import { assignLocation } from './navigation'
import { useText } from './texts'

/**
 * The account button in the shell header. With nobody signed in, it is a sign-in button that goes
 * to the identity provider and back to the current page. With someone signed in, it shows their
 * name, with a menu. Its Sessions entry opens the account screen. Its sign-out entry ends the
 * session, then sends the browser to the identity provider's sign-out page. It renders nothing while
 * the user loads, or when loading fails.
 */
export function UserMenu() {
  const t = useText()
  const [user, setUser] = useState<CurrentUser | null | undefined>(undefined)

  useEffect(() => {
    const controller = new AbortController()
    fetchCurrentUser(controller.signal).then(setUser, (error: unknown) => {
      if (!controller.signal.aborted) {
        console.warn('Loading the current user failed', error)
      }
    })
    return () => controller.abort()
  }, [])

  if (user === undefined) {
    return null
  }

  if (user === null) {
    return (
      <Button type="text" onClick={startSignIn}>
        {t('shell.account.signIn')}
      </Button>
    )
  }

  const signOut = () => {
    signOutSession().then(assignLocation, (error: unknown) => console.warn('Signing out failed', error))
  }
  const items: MenuProps['items'] = [
    { key: 'sessions', label: <Link to="/account">{t('shell.account.sessions')}</Link> },
    { key: 'signOut', label: t('shell.account.signOut'), onClick: signOut },
  ]

  return (
    <Dropdown trigger={['click']} menu={{ items, 'aria-label': t('shell.account.label') }}>
      <Button type="text">{user.displayName}</Button>
    </Dropdown>
  )
}
