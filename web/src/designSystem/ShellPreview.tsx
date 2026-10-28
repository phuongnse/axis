import type { ReactNode } from 'react'
import { PlatformContext, type Platform } from '../pages/context'
import { HomePage } from '../pages/HomePage'
import { PageLayout } from '../platform/PageLayout'
import { Shell } from '../platform/Shell'
import { ShellHeightContext } from '../platform/shellHeight'
import { TableWidget } from '../platform/TableWidget'
import { locale, siteTexts, sitePath, tableWidget } from './fixtures'
import { notificationsPath, platformSite, siteNavigation } from './shellFixtures'

/** The height of the frame a shell preview draws in: a short desktop window. */
const frameHeight = '640px'

const platform: Platform = {
  site: platformSite,
  fallbackTexts: {},
  current: { locale, texts: {} },
  setCurrent: () => {},
  development: true,
}

function Frame({ children }: { children: ReactNode }) {
  return <ShellHeightContext.Provider value={frameHeight}>{children}</ShellHeightContext.Provider>
}

/** An application site's shell around its Requests page, which holds the request table. */
export function SiteShellPreview() {
  return (
    <Frame>
      <Shell
        titleKey="preview.site.title"
        navigation={siteNavigation}
        locales={['en', 'vi']}
        locale={locale}
        onLocaleChange={() => {}}
        notifications={{ path: notificationsPath, sitePath }}
      >
        <PageLayout title={siteTexts['preview.requests']}>
          <TableWidget sitePath={sitePath} widget={tableWidget} locale={locale} pageValues={{}} />
        </PageLayout>
      </Shell>
    </Frame>
  )
}

/** The platform site's shell around the home page, which signs in and lists the sites. */
export function PlatformShellPreview() {
  return (
    <Frame>
      <PlatformContext.Provider value={platform}>
        <Shell
          titleKey={platformSite.titleKey}
          navigation={platformSite.navigation}
          locales={platformSite.locales.available}
          locale={locale}
          onLocaleChange={() => {}}
        >
          <HomePage />
        </Shell>
      </PlatformContext.Provider>
    </Frame>
  )
}
