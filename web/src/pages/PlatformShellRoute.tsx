import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { fetchCurrentUser, type CurrentUser } from '../platform/currentUser'
import { storeLocale } from '../platform/locale'
import { Shell } from '../platform/Shell'
import type { NavigationItem } from '../platform/site'
import { TextProvider } from '../platform/TextProvider'
import { fetchTexts } from '../platform/texts'
import { LocaleLoadFailed } from './LocaleLoadFailed'
import { usePlatform } from './context'

// A tenant notice has no page to open, so the site path is never used.
const tenantNotices = { path: '/api/notifications', sitePath: '/' }

// The console's title and navigation are fixed here, as the server's site metadata on its host has only Home.
const cellConsoleTitleKey = 'shell.cell.title'
const cellConsoleNavigation: NavigationItem[] = [
  { key: 'cell', path: '/', labelKey: 'shell.cell.nav.cell', icon: 'dashboard' },
  { key: 'jobs', path: '/jobs', labelKey: 'shell.cell.nav.jobs', icon: 'list' },
  { key: 'tenants', path: '/tenants', labelKey: 'shell.cell.nav.tenants', icon: 'people' },
]

/**
 * Renders a page in the platform shell, with the platform texts, navigation and locales. The header has
 * the notification bell for an administrator, who sees the tenant notices. On the cell console's host the
 * shell has the console's title and its three entries, no bell, and shows `account` in place of the app switcher
 * and the account button.
 */
export function PlatformShellRoute({ children, account }: { children: ReactNode; account?: ReactNode }) {
  const { site, fallbackTexts, current, setCurrent, development } = usePlatform()
  const [localeFailed, setLocaleFailed] = useState(false)
  const localeRequest = useRef<AbortController>(undefined)
  const [user, setUser] = useState<CurrentUser | null>(null)
  const cellConsole = site.cellConsole === true

  useEffect(() => () => localeRequest.current?.abort(), [])

  useEffect(() => {
    if (cellConsole) {
      return
    }
    const controller = new AbortController()
    fetchCurrentUser(controller.signal)
      .then(setUser)
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn('Loading the current user failed', error)
        }
      })
    return () => controller.abort()
  }, [cellConsole])

  // Keeps the current texts until the new locale has loaded, and drops a superseded switch.
  const changeLocale = useCallback(
    (locale: string) => {
      localeRequest.current?.abort()
      const controller = new AbortController()
      localeRequest.current = controller
      setLocaleFailed(false)
      fetchTexts(locale, controller.signal)
        .then((texts) => {
          storeLocale(locale)
          setCurrent({ locale, texts })
        })
        .catch((error: unknown) => {
          if (!controller.signal.aborted) {
            console.warn(`Loading the texts of '${locale}' failed`, error)
            setLocaleFailed(true)
          }
        })
    },
    [setCurrent],
  )

  const catalogs = useMemo(() => [{ texts: current.texts, fallbackTexts }], [current.texts, fallbackTexts])

  return (
    <TextProvider catalogs={catalogs} development={development}>
      <Shell
        titleKey={cellConsole ? cellConsoleTitleKey : site.titleKey}
        navigation={cellConsole ? cellConsoleNavigation : site.navigation}
        locales={site.locales.available}
        locale={current.locale}
        onLocaleChange={changeLocale}
        legal={site.legal}
        notifications={!cellConsole && user?.users ? tenantNotices : undefined}
        account={cellConsole ? (account ?? null) : undefined}
      >
        {localeFailed && <LocaleLoadFailed onClose={() => setLocaleFailed(false)} />}
        {children}
      </Shell>
    </TextProvider>
  )
}
