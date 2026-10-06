import { PageContainer, ProCard } from '@ant-design/pro-components'
import { Alert, Badge } from 'antd'
import { useCallback, useEffect, useRef, useState } from 'react'
import { readStoredLocale, storeLocale } from './platform/locale'
import { useServerStatus, type ServerStatus } from './platform/serverStatus'
import { Shell } from './platform/Shell'
import { fetchSite, type SiteMetadata } from './platform/site'
import { TextProvider } from './platform/TextProvider'
import { fetchTexts, useText, type TextMap } from './platform/texts'

const statusBadge: Record<ServerStatus, 'processing' | 'success' | 'error'> = {
  checking: 'processing',
  ready: 'success',
  unavailable: 'error',
}

interface LoadedShell {
  site: SiteMetadata
  fallbackTexts: TextMap
  locale: string
  texts: TextMap
}

function HomePage() {
  const t = useText()
  const serverStatus = useServerStatus()

  return (
    <PageContainer title={t('shell.home.title')}>
      <ProCard title={t('shell.serverStatus.title')}>
        <Badge
          data-testid="server-status"
          status={statusBadge[serverStatus]}
          text={t(`shell.serverStatus.${serverStatus}`)}
        />
      </ProCard>
    </PageContainer>
  )
}

export function App({ development }: { development: boolean }) {
  const [shell, setShell] = useState<LoadedShell>()
  const [failed, setFailed] = useState(false)
  const localeRequest = useRef<AbortController>(undefined)

  useEffect(() => {
    const controller = new AbortController()
    async function load() {
      const site = await fetchSite(controller.signal)
      const locale = readStoredLocale(site.locales.available, site.locales.default)
      const [fallbackTexts, texts] = await Promise.all([
        fetchTexts(site.locales.fallback, controller.signal),
        locale === site.locales.fallback ? undefined : fetchTexts(locale, controller.signal),
      ])
      setShell({ site, fallbackTexts, locale, texts: texts ?? fallbackTexts })
    }
    load().catch((error: unknown) => {
      if (!controller.signal.aborted) {
        console.warn('Loading the site failed', error)
        setFailed(true)
      }
    })
    return () => {
      controller.abort()
      localeRequest.current?.abort()
    }
  }, [])

  // Keeps the current texts until the new locale has loaded, and drops a superseded switch.
  const changeLocale = useCallback((locale: string) => {
    localeRequest.current?.abort()
    const controller = new AbortController()
    localeRequest.current = controller
    fetchTexts(locale, controller.signal)
      .then((texts) => {
        storeLocale(locale)
        setShell((current) => current && { ...current, locale, texts })
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn(`Loading the texts of '${locale}' failed`, error)
        }
      })
  }, [])

  if (failed) {
    // There are no texts to render, so this is the one fixed string in the shell.
    return <Alert data-testid="shell-error" type="error" banner message="Could not load the site." />
  }
  if (!shell) {
    return null
  }

  return (
    <TextProvider texts={shell.texts} fallbackTexts={shell.fallbackTexts} development={development}>
      <Shell site={shell.site} locale={shell.locale} onLocaleChange={changeLocale}>
        <HomePage />
      </Shell>
    </TextProvider>
  )
}
