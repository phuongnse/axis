import { StrictMode, useCallback, useEffect, useState } from 'react'
import { TextContext } from '../src/platform/texts'
import { ThemeModeProvider } from '../src/platform/theme'
import { SignUpPage } from './SignUpPage'
import { signUpTexts, type SignUpLocale } from './texts'
import { VerifyPage } from './VerifyPage'

function browserLocale(): SignUpLocale {
  return navigator.language.toLowerCase().startsWith('vi') ? 'vi' : 'en'
}

/** The path of the verify page, see docs/reference/sign-up.md#the-verify-page. */
const verifyPath = '/sign-up/verify'

/**
 * The sign-up page, or the verify page at its path, with the SPA's theme provider and its own two-language texts. It starts
 * in `vi` for a Vietnamese browser.
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
          {window.location.pathname.replace(/\/$/, '') === verifyPath ? (
            <VerifyPage locale={locale} onLocaleChange={setLocale} />
          ) : (
            <SignUpPage locale={locale} onLocaleChange={setLocale} />
          )}
        </TextContext.Provider>
      </ThemeModeProvider>
    </StrictMode>
  )
}
