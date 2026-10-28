import { Button, Dropdown, type MenuProps } from 'antd'
import { useEffect, useState } from 'react'
import { useLocation } from 'react-router'
import { startSignIn } from './api'
import { fetchCurrentUser, signOutSession, type CurrentUser } from './currentUser'
import { assignLocation } from './navigation'
import { useText } from './texts'

/**
 * The account button in the shell header. With nobody signed in, it is a sign-in button that goes
 * to the identity provider and back to the current page. With someone signed in, it shows their
 * name, with a menu to sign out: that ends the session, then sends the browser to the identity
 * provider's sign-out page. It
 * renders nothing while the user loads, or when loading fails.
 */
export function UserMenu() {
  const t = useText()
  const [user, setUser] = useState<CurrentUser | null | undefined>(undefined)
  const { pathname } = useLocation()

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

  // Proposed: the platform home page is the sign-in screen with nobody signed in, so the header shows no second button.
  if (user === null && pathname === '/') {
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
  const items: MenuProps['items'] = [{ key: 'signOut', label: t('shell.account.signOut'), onClick: signOut }]

  // Proposed: Studio is reached from the app switcher and the home page, so the account button stands alone.
  return (
    <Dropdown trigger={['click']} menu={{ items, 'aria-label': t('shell.account.label') }}>
      <Button type="text">{user.displayName}</Button>
    </Dropdown>
  )
}
