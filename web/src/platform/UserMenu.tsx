import { Avatar, Button, Dropdown, type MenuProps } from 'antd'
import { useEffect, useState } from 'react'
import { Link, useLocation } from 'react-router'
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
 * initials on an avatar, their name and a caret, with a menu. Its Your account entry opens the account page. Its sign-out entry ends the
 * session, then sends the browser to the identity provider's sign-out page. It renders nothing while
 * the user loads, when loading fails, or with nobody signed in on the home page, which starts sign-in or shows a sign-in card.
 * Proposed, #4934: the shell has no footer, so the menu holds the legal links in a Legal group between Your account and
 * Sign out: Privacy and Terms when `legal` gives their addresses, and Licences always. A signed-out person meets them on
 * the card pages that sign them in, under the card, see docs/reference/legal.md#legal-line.
 */
export function UserMenu({ legal }: { legal?: SiteLegal | null }) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const { pathname } = useLocation()
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
  const legalLinks = [
    { key: 'privacy', href: legal?.privacyUrl, label: t('shell.legal.privacy') },
    { key: 'terms', href: legal?.termsUrl, label: t('shell.legal.terms') },
  ].filter((link): link is { key: string; href: string; label: string } => Boolean(link.href))
  const items: MenuProps['items'] = [
    { key: 'account', label: <Link to="/account">{t('shell.account.title')}</Link> },
    { type: 'divider' },
    {
      type: 'group',
      key: 'legal',
      label: t('shell.legal.label'),
      children: [
        ...legalLinks.map(({ key, href, label }) => ({ key, label: <a href={href}>{label}</a> })),
        { key: 'licences', label: <Link to="/licences">{t('shell.legal.licences')}</Link> },
      ],
    },
    { type: 'divider' },
    { key: 'signOut', label: t('shell.account.signOut'), onClick: signOut },
  ]

  return (
    <Dropdown trigger={['click']} menu={{ items, 'aria-label': t('shell.account.label') }}>
      <AxisButton
        role="plain"
        caret
        data-testid="account-menu"
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
