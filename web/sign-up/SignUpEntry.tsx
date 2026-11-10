import { StrictMode, useCallback, useEffect, useMemo, useState } from 'react'
import { TextContext } from '../src/platform/texts'
import { ThemeModeProvider } from '../src/platform/theme'
import { useSignUpDocuments } from './documents'
import { ProgressPage } from './ProgressPage'
import { SignUpPage } from './SignUpPage'
import { signUpText, type SignUpLocale } from './texts'
import { VerifyPage } from './VerifyPage'

function browserLocale(): SignUpLocale {
  return navigator.language.toLowerCase().startsWith('vi') ? 'vi' : 'en'
}

/** The path of the verify page, see docs/reference/sign-up.md#the-verify-page. */
const verifyPath = '/sign-up/verify'

/** The path of the progress page, see docs/reference/sign-up.md#provisioning. */
const progressPath = '/sign-up/progress'

/**
 * The sign-up page, or the verify page or the progress page at its path, with the SPA's theme provider and its own two-language texts. It starts
 * in `vi` for a Vietnamese browser.
 */
export function SignUpEntry() {
  const [locale, setLocale] = useState<SignUpLocale>(browserLocale)
  useEffect(() => {
    document.documentElement.lang = locale
  }, [locale])
  const text = useMemo(() => signUpText(locale), [locale])
  const { documents, reload } = useSignUpDocuments()
  const [path, setPath] = useState(() => window.location.pathname.replace(/\/$/, ''))
  // The progress page replaces the verify page in place, so the used token leaves the history and the page keeps its language.
  const openProgress = useCallback((id: string, secret: string) => {
    window.history.replaceState(null, '', `${progressPath}#${id}.${secret}`)
    setPath(progressPath)
  }, [])
  return (
    <StrictMode>
      <ThemeModeProvider>
        <TextContext.Provider value={text}>
          {path === verifyPath ? (
            <VerifyPage locale={locale} onLocaleChange={setLocale} documents={documents} onConfirmed={openProgress} />
          ) : path === progressPath ? (
            <ProgressPage locale={locale} onLocaleChange={setLocale} documents={documents} />
          ) : (
            <SignUpPage locale={locale} onLocaleChange={setLocale} documents={documents} onDocumentsRefused={reload} />
          )}
        </TextContext.Provider>
      </ThemeModeProvider>
    </StrictMode>
  )
}
