import { StrictMode, useCallback, useEffect, useState } from 'react'
import { TextContext } from '../src/platform/texts'
import { ThemeModeProvider } from '../src/platform/theme'
import { ProgressPage } from './ProgressPage'
import { SignUpPage } from './SignUpPage'
import { VerifyPage } from './VerifyPage'
import { signUpTexts, type SignUpLocale } from './texts'

function browserLocale(): SignUpLocale {
  return navigator.language.toLowerCase().startsWith('vi') ? 'vi' : 'en'
}

/**
 * The sign-up pages with the SPA's theme provider and their own two-language texts. It starts in `vi` for a Vietnamese
 * browser. Proposed: `/sign-up/verify#<token>` draws the verify page, `/sign-up/progress#<id>.<secret>` the progress
 * page, and any other path the sign-up page.
 */
export function SignUpEntry() {
  const [locale, setLocale] = useState<SignUpLocale>(browserLocale)
  useEffect(() => {
    document.documentElement.lang = locale
  }, [locale])
  const text = useCallback(
    (key: string) => signUpTexts[locale][key as keyof (typeof signUpTexts)[SignUpLocale]] ?? key,
    [locale],
  )
  return (
    <StrictMode>
      <ThemeModeProvider>
        <TextContext.Provider value={text}>
          {window.location.pathname.endsWith('/sign-up/progress') ? (
            <ProgressPage
              id={window.location.hash.slice(1).split('.')[0] ?? ''}
              secret={window.location.hash.slice(1).split('.')[1] ?? ''}
              locale={locale}
              onLocaleChange={setLocale}
            />
          ) : window.location.pathname.endsWith('/sign-up/verify') ? (
            <VerifyPage
              token={window.location.hash.slice(1)}
              locale={locale}
              onLocaleChange={setLocale}
              // Proposed: the progress page takes the id and its secret in the fragment too, so no log carries them.
              onConfirmed={({ id, secret }) => window.location.assign(`/sign-up/progress#${id}.${secret}`)}
            />
          ) : (
            <SignUpPage locale={locale} onLocaleChange={setLocale} />
          )}
        </TextContext.Provider>
      </ThemeModeProvider>
    </StrictMode>
  )
}
