import { useCallback, useEffect, useRef, useState } from 'react'
import { TextContext } from '../platform/texts'
import { SignUpPage } from '../../sign-up/SignUpPage'
import { signUpTexts, type SignUpLocale } from '../../sign-up/texts'

/** Types `value` into the input with `id`, as a person would, so the page's own change handlers run. */
function typeInto(root: HTMLElement, id: string, value: string): boolean {
  const input = root.querySelector<HTMLInputElement>(`#${id}`)
  if (!input) {
    return false
  }
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!
  setter.call(input, value)
  input.dispatchEvent(new Event('input', { bubbles: true }))
  return true
}

/**
 * The sign-up page with its own two-language texts, as `web/sign-up/SignUpEntry.tsx` draws it. A state may type a
 * business name once the page is drawn, so the suggested subdomain and its check show.
 */
export function SignUpPreview({ businessName }: { businessName?: string }) {
  const [locale, setLocale] = useState<SignUpLocale>('en')
  const wrapper = useRef<HTMLDivElement>(null)
  const text = useCallback(
    (key: string) => signUpTexts[locale][key as keyof (typeof signUpTexts)[SignUpLocale]] ?? key,
    [locale],
  )
  useEffect(() => {
    if (businessName && wrapper.current) {
      typeInto(wrapper.current, 'sign-up_businessName', businessName)
    }
  }, [businessName])
  return (
    <div ref={wrapper}>
      <TextContext.Provider value={text}>
        <SignUpPage locale={locale} onLocaleChange={setLocale} />
      </TextContext.Provider>
    </div>
  )
}
