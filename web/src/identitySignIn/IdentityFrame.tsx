import type { ReactNode } from 'react'
import { useMemo } from 'react'
import { CardPage } from '../platform/CardPage'
import { TextContext } from '../platform/texts'
import type { IdentityFrameContext } from './signInContext'

/**
 * The frame of the identity provider's pages. It feeds the shared `CardPage` the identity provider's context: the
 * realm's name, its languages and its legal links.
 */
export function IdentityFrame({
  title,
  frame,
  children,
}: {
  title: string
  frame: IdentityFrameContext
  children: ReactNode
}) {
  // Keycloak pages have no text catalogue, so the gear's keys resolve from the page's context.
  const frameText = useMemo(() => {
    const { frameTexts, locales } = frame
    const texts = new Map<string, string>([
      ['shell.preferences.label', frameTexts.languageAndTheme],
      ['shell.locale.label', frameTexts.language],
      ['shell.theme.label', frameTexts.theme],
      ['shell.theme.system', frameTexts.system],
      ['shell.theme.light', frameTexts.light],
      ['shell.theme.dark', frameTexts.dark],
      ...locales.map(({ tag, label }): [string, string] => [`shell.locale.${tag}`, label]),
    ])
    return (key: string) => texts.get(key) ?? key
  }, [frame])
  return (
    <TextContext.Provider value={frameText}>
      <CardPage
        organization={frame.realmName}
        title={title}
        locales={frame.locales.map((locale) => locale.tag)}
        locale={frame.locale}
        onLocaleChange={(tag) => {
          const target = frame.locales.find((locale) => locale.tag === tag)
          if (target) {
            window.location.assign(target.url)
          }
        }}
        legal={frame.legal}
      >
        {children}
      </CardPage>
    </TextContext.Provider>
  )
}
