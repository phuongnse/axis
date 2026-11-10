import { useMemo, useState } from 'react'
import { TextContext } from '../platform/texts'
import { useSignUpDocuments } from '../../sign-up/documents'
import { SignUpPage } from '../../sign-up/SignUpPage'
import { signUpText, type SignUpLocale } from '../../sign-up/texts'

/** The sign-up page in its own two-language texts, as `web/sign-up/SignUpEntry.tsx` draws it, over the fixtures. */
export function SignUpPreview() {
  const [locale, setLocale] = useState<SignUpLocale>('en')
  const text = useMemo(() => signUpText(locale), [locale])
  const documents = useSignUpDocuments()
  return (
    <TextContext.Provider value={text}>
      <SignUpPage locale={locale} onLocaleChange={setLocale} documents={documents} />
    </TextContext.Provider>
  )
}
