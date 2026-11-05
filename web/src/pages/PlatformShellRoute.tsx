import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { fetchCurrentUser, type CurrentUser } from '../platform/currentUser'
import { storeLocale } from '../platform/locale'
import { Shell } from '../platform/Shell'
import { TextProvider } from '../platform/TextProvider'
import { fetchTexts } from '../platform/texts'
import { LocaleLoadFailed } from './LocaleLoadFailed'
import { usePlatform } from './context'
import { LeaveGuardProvider } from '../platform/LeaveGuardProvider'

// A tenant notice has no page to open, so the site path is never used.
const tenantNotices = { path: '/api/notifications', sitePath: '/' }

/**
 * Renders a page in the platform shell, with the platform texts, navigation and locales. The header has
 * the notification bell for an administrator, who sees the tenant notices.
 */
export function PlatformShellRoute({ children }: { children: ReactNode }) {
  const { site, fallbackTexts, current, setCurrent, development } = usePlatform()
  const [localeFailed, setLocaleFailed] = useState(false)
  const localeRequest = useRef<AbortController>(undefined)
  const [user, setUser] = useState<CurrentUser | null>(null)

  useEffect(() => () => localeRequest.current?.abort(), [])

  useEffect(() => {
    const controller = new AbortController()
    fetchCurrentUser(controller.signal)
      .then(setUser)
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn('Loading the current user failed', error)
        }
      })
    return () => controller.abort()
  }, [])

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
      <LeaveGuardProvider>
        <Shell
          titleKey={site.titleKey}
          navigation={site.navigation}
          locales={site.locales.available}
          locale={current.locale}
          onLocaleChange={changeLocale}
          legal={site.legal}
          notifications={user?.users ? tenantNotices : undefined}
        >
          {localeFailed && <LocaleLoadFailed onClose={() => setLocaleFailed(false)} />}
          {children}
        </Shell>
      </LeaveGuardProvider>
    </TextProvider>
  )
}
