import { ConfigProvider } from 'antd'
import type { ReactNode } from 'react'
import { antLocaleFor } from '../src/platform/antLocale'
import { CardPage } from '../src/platform/CardPage'
import { PreferencesMenu } from '../src/platform/PreferencesMenu'
import { useText } from '../src/platform/texts'
import type { SignUpDocuments } from './signUpDocuments'
import { signUpLocales, type SignUpLocale } from './texts'

/**
 * Proposed: the frame of every sign-up page: `CardPage` in the page's language, with the "Language and theme" gear and
 * the legal line, whose Privacy, Terms and Licences addresses come from the region's documents.
 */
export function SignUpFrame({
  title,
  locale,
  onLocaleChange,
  documents,
  children,
}: {
  title: string
  locale: SignUpLocale
  onLocaleChange: (locale: SignUpLocale) => void
  documents: SignUpDocuments | null
  children: ReactNode
}) {
  const t = useText()
  return (
    <ConfigProvider locale={antLocaleFor(locale)}>
      <CardPage
        title={title}
        legal={{
          privacyUrl: documents?.privacy.urls[locale],
          termsUrl: documents?.terms.urls[locale],
          licencesUrl: documents?.licencesUrl,
          texts: {
            legal: t('shell.legal.label'),
            privacy: t('shell.legal.privacy'),
            terms: t('shell.legal.terms'),
            licences: t('shell.legal.licences'),
          },
        }}
        preferences={
          <PreferencesMenu
            locales={signUpLocales}
            locale={locale}
            onLocaleChange={(next) => onLocaleChange(next as SignUpLocale)}
          />
        }
      >
        {children}
      </CardPage>
    </ConfigProvider>
  )
}
