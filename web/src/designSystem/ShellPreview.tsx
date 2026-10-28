import { useState, type ReactNode } from 'react'
import { PlatformContext, type Platform } from '../pages/context'
import { HomePage } from '../pages/HomePage'
import { PageLayout } from '../platform/PageLayout'
import { Shell } from '../platform/Shell'
import { ShellHeightContext } from '../platform/shellHeight'
import { SignInPrompt } from '../platform/SignInPrompt'
import { FormWidget } from '../platform/FormWidget'
import { TableWidget } from '../platform/TableWidget'
import { formWidget, locale, siteTexts, sitePath, tableWidget } from './fixtures'
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

/** An application site's shell around a page: its Requests page, which holds the request table, unless one is given. */
export function SiteShellPreview({ collapsed = false, children }: { collapsed?: boolean; children?: ReactNode }) {
  return (
    <Frame>
      <Shell
        titleKey="preview.site.title"
        navigation={siteNavigation}
        locales={['en', 'vi']}
        locale={locale}
        onLocaleChange={() => {}}
        notifications={{ path: notificationsPath, sitePath }}
        initiallyCollapsed={collapsed}
      >
        {children ?? (
          <PageLayout
            title={siteTexts['preview.requests']}
            breadcrumb={[
              { label: siteTexts['preview.site.title'], to: `/${sitePath}` },
              { label: siteTexts['preview.requests'] },
            ]}
          >
            <TableWidget sitePath={sitePath} widget={tableWidget} locale={locale} pageValues={{}} />
          </PageLayout>
        )}
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

/** The shell around the form page that the Requests table's New request action opens. */
export function FormPageShellPreview() {
  return (
    <SiteShellPreview>
      <PageLayout
        title={siteTexts['request.new']}
        breadcrumb={[
          { label: siteTexts['preview.site.title'], to: `/${sitePath}` },
          { label: siteTexts['preview.requests'], to: `/${sitePath}/requests` },
          { label: siteTexts['request.new'] },
        ]}
      >
        <FormWidget widget={formWidget} sitePath={sitePath} pages={[]} recordId={null} returnTo="/ds" locale={locale} />
      </PageLayout>
    </SiteShellPreview>
  )
}

/**
 * An application site whose session has ended: the sign-in dialog over the Requests page. The dialog renders inside
 * this state's own frame, which its transform makes the containing block of the dialog's fixed layers.
 */
export function SessionEndedPreview() {
  const [frame, setFrame] = useState<HTMLDivElement | null>(null)
  return (
    <div ref={setFrame} style={{ position: 'relative', transform: 'translateZ(0)', overflow: 'hidden' }}>
      <SiteShellPreview />
      {frame && <SignInPrompt open getContainer={() => frame} />}
    </div>
  )
}
