import { ConfigProvider } from 'antd'
import type { ReactNode, RefObject } from 'react'
import { antLocaleFor } from '../src/platform/antLocale'
import { CardPage } from '../src/platform/CardPage'
import { useText } from '../src/platform/texts'
import type { SignUpDocuments } from './documents'
import { signUpLocales, type SignUpLocale } from './texts'

/**
 * The frame of every sign-up page: the shared card page in the page's language, with the legal line's Privacy, Terms and
 * Licences addresses from the region's documents, see docs/reference/sign-up.md#the-page. The title takes the focus
 * through `titleRef`, as a page moves to its next step.
 */
export function SignUpFrame({
  locale,
  onLocaleChange,
  title,
  titleRef,
  documents,
  children,
}: {
  locale: SignUpLocale
  onLocaleChange: (locale: SignUpLocale) => void
  title: string
  titleRef: RefObject<HTMLElement | null>
  documents: SignUpDocuments | undefined
  children: ReactNode
}) {
  const t = useText()
  return (
    <ConfigProvider locale={antLocaleFor(locale)}>
      <CardPage
        title={title}
        titleRef={titleRef}
        preferences={{
          locales: signUpLocales,
          locale,
          onLocaleChange: (next) => onLocaleChange(next as SignUpLocale),
        }}
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
      >
        {children}
      </CardPage>
    </ConfigProvider>
  )
}
