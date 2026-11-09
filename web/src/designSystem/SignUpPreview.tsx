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

/** What a state fills in before it presses Sign up: nothing, or every field with this email. */
export type SignUpSend = { email?: string }

/**
 * The sign-up page with its own two-language texts, as `web/sign-up/SignUpEntry.tsx` draws it. A state may type a
 * business name once the page is drawn, so the suggested subdomain and its check show, and may press Sign up, with
 * the fields empty or filled in, so a refusal shows.
 */
export function SignUpPreview({ businessName, send }: { businessName?: string; send?: SignUpSend }) {
  const [locale, setLocale] = useState<SignUpLocale>('en')
  const wrapper = useRef<HTMLDivElement>(null)
  const text = useCallback(
    (key: string) => signUpTexts[locale][key as keyof (typeof signUpTexts)[SignUpLocale]] ?? key,
    [locale],
  )
  useEffect(() => {
    const root = wrapper.current
    if (!root) {
      return
    }
    if (businessName) {
      typeInto(root, 'sign-up_businessName', businessName)
    }
    if (!send) {
      return
    }
    if (send.email) {
      typeInto(root, 'sign-up_fullName', 'Nguyễn Thị Lan')
      typeInto(root, 'sign-up_email', send.email)
      root.querySelector<HTMLInputElement>('#sign-up_terms')?.click()
    }
    // After the subdomain's check and the documents have answered.
    const timer = setTimeout(() => root.querySelector<HTMLButtonElement>('button[type="submit"]')?.click(), 600)
    return () => clearTimeout(timer)
  }, [businessName, send])
  return (
    <div ref={wrapper}>
      <TextContext.Provider value={text}>
        <SignUpPage locale={locale} onLocaleChange={setLocale} />
      </TextContext.Provider>
    </div>
  )
}
