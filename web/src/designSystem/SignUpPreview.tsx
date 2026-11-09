import { useCallback, useEffect, useRef, useState } from 'react'
import { TextContext } from '../platform/texts'
import { ProgressPage } from '../../sign-up/ProgressPage'
import { SignUpPage } from '../../sign-up/SignUpPage'
import { VerifyPage } from '../../sign-up/VerifyPage'
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
 * What a state fills in before it presses Sign up, nothing or every field with this email, and whether it then presses
 * Send again.
 */
export type SignUpSend = { email?: string; resend?: boolean }

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
      typeInto(root, 'sign-up_fullName', 'Alex Morgan')
      typeInto(root, 'sign-up_email', send.email)
      root.querySelector<HTMLInputElement>('#sign-up_terms')?.click()
    }
    // After the subdomain's check and the documents have answered.
    const timer = setTimeout(() => root.querySelector<HTMLButtonElement>('button[type="submit"]')?.click(), 600)
    // Once the inbox step shows, a state may press Send again.
    const again = send.resend
      ? setTimeout(() => root.querySelector<HTMLButtonElement>('#sign-up-resend')?.click(), 1200)
      : undefined
    return () => {
      clearTimeout(timer)
      clearTimeout(again)
    }
  }, [businessName, send])
  return (
    <div ref={wrapper}>
      <TextContext.Provider value={text}>
        <SignUpPage locale={locale} onLocaleChange={setLocale} />
      </TextContext.Provider>
    </div>
  )
}

/**
 * The verify page with the sign-up texts, on a sample token. A state may press Confirm once the link's check answers,
 * so a failed confirm shows. Confirm's success goes nowhere here, as the progress page is a step of its own.
 */
export function VerifyPreview({ confirm }: { confirm?: boolean }) {
  const [locale, setLocale] = useState<SignUpLocale>('en')
  const wrapper = useRef<HTMLDivElement>(null)
  const text = useCallback(
    (key: string) => signUpTexts[locale][key as keyof (typeof signUpTexts)[SignUpLocale]] ?? key,
    [locale],
  )
  useEffect(() => {
    if (!confirm) {
      return
    }
    const timer = setTimeout(
      () => wrapper.current?.querySelector<HTMLButtonElement>('button.ant-btn-primary')?.click(),
      600,
    )
    return () => clearTimeout(timer)
  }, [confirm])
  return (
    <div ref={wrapper}>
      <TextContext.Provider value={text}>
        <VerifyPage token="t-5b2f" locale={locale} onLocaleChange={setLocale} onConfirmed={() => {}} />
      </TextContext.Provider>
    </div>
  )
}

/** The progress page with the sign-up texts, on a sample id and secret. */
export function ProgressPreview() {
  const [locale, setLocale] = useState<SignUpLocale>('en')
  const text = useCallback(
    (key: string) => signUpTexts[locale][key as keyof (typeof signUpTexts)[SignUpLocale]] ?? key,
    [locale],
  )
  return (
    <TextContext.Provider value={text}>
      <ProgressPage id="p-3d9a" secret="s-81c4" locale={locale} onLocaleChange={setLocale} />
    </TextContext.Provider>
  )
}
