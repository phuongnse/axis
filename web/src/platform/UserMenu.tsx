import { Avatar, Button, Dropdown, type MenuProps } from 'antd'
import { useEffect, useState } from 'react'
import { Link, useLocation } from 'react-router'
import { useAboutVersion } from './about'
import { AxisButton } from './AxisButton'
import { startSignIn } from './api'
import { fetchCurrentUser, signOutSession, type CurrentUser } from './currentUser'
import { assignLocation } from './navigation'
import type { SiteLegal } from './site'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'

/** Up to two initials of a name: the first letters of its first and last words, in upper case. */
function initials(displayName: string): string {
  const words = displayName.split(/\s+/).filter((word) => word !== '')
  const letters = [words[0], words.length > 1 ? words[words.length - 1] : undefined].map((word) =>
    word === undefined ? '' : (Array.from(word)[0] ?? ''),
  )
  return letters.join('').toUpperCase()
}

/**
 * The account button in the shell header. With nobody signed in, it is a sign-in button that goes
 * to the identity provider and back to the current page. With someone signed in, it shows their
 * initials on an avatar, their name and a caret, with a menu. Its Sessions entry opens the account screen. Its sign-out entry ends the
 * session, then sends the browser to the identity provider's sign-out page. It renders nothing while
 * the user loads, when loading fails, or with nobody signed in on the home page, which starts sign-in or shows a sign-in card.
 * Proposed: the legal line, because a page with a sidebar has no footer: before Sign out, Privacy and Terms when
 * `legal` gives their addresses and Licences always, and after it "© <year> Axis, v<version>" as a muted line.
 */
export function UserMenu({ legal }: { legal?: SiteLegal | null }) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const { pathname } = useLocation()
  const [user, setUser] = useState<CurrentUser | null | undefined>(undefined)
  const version = useAboutVersion()
  const [year] = useState(() => new Date().getFullYear())

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
    // The home page starts sign-in at once, or shows a card with its own button.
    if (pathname === '/') {
      return null
    }
    return (
      // oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role.
      <Button type="text" onClick={() => startSignIn()}>
        {t('shell.account.signIn')}
      </Button>
    )
  }

  const signOut = () => {
    signOutSession().then(assignLocation, (error: unknown) => console.warn('Signing out failed', error))
  }
  // The same links as the legal line, see docs/reference/legal.md#legal-line.
  const legalLink = (href: string, label: string) => <a href={href}>{label}</a>
  const items: MenuProps['items'] = [
    { key: 'sessions', label: <Link to="/account">{t('shell.account.sessions')}</Link> },
    { type: 'divider' },
    ...(legal?.privacyUrl ? [{ key: 'privacy', label: legalLink(legal.privacyUrl, t('shell.legal.privacy')) }] : []),
    ...(legal?.termsUrl ? [{ key: 'terms', label: legalLink(legal.termsUrl, t('shell.legal.terms')) }] : []),
    { key: 'licences', label: legalLink('/licences', t('shell.legal.licences')) },
    { type: 'divider' },
    { key: 'signOut', label: t('shell.account.signOut'), onClick: signOut },
    // The copyright and the version close the menu as a muted line that takes no focus.
    { type: 'group', key: 'about', label: version != null ? `© ${year} Axis, v${version}` : `© ${year} Axis` },
  ]

  return (
    <Dropdown trigger={['click']} menu={{ items, 'aria-label': t('shell.account.label') }}>
      <AxisButton
        role="plain"
        caret
        data-testid="user-menu"
        style={{ display: 'inline-flex', alignItems: 'center', gap: spacing.xs, minWidth: 0, maxWidth: 200 }}
      >
        {/* The avatar is hidden from assistive technology, so the name alone names the button. */}
        <Avatar aria-hidden size={24} style={{ background: colour.primary, color: colour.onPrimary, flex: 'none' }}>
          {initials(user.displayName)}
        </Avatar>
        <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{user.displayName}</span>
      </AxisButton>
    </Dropdown>
  )
}
