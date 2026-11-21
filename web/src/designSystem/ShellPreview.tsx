import { useCallback, useEffect, useRef, type ReactNode } from 'react'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { PlatformContext, type Platform } from '../pages/context'
import { PlatformRoutes } from '../pages/PlatformRoutes'
import { locale, platformSite } from './fixtures'

const platform: Platform = {
  site: platformSite,
  fallbackTexts: platformEnglish,
  current: { locale, texts: platformEnglish },
  setCurrent: () => {},
  development: true,
}

/** The platform's own routes over the fixtures, so the preview draws the pages as the app does. */
export function ShellPreview() {
  return (
    <PlatformContext.Provider value={platform}>
      <PlatformRoutes />
    </PlatformContext.Provider>
  )
}

/**
 * Waits until the shell draws an element matching `selector`, then runs `act` on it once, so a state opens, collapses or
 * types into it once.
 */
function useActOnMount(selector: string, act: (target: HTMLElement) => void) {
  const wrapper = useRef<HTMLDivElement>(null)
  const acted = useRef(false)
  useEffect(() => {
    const element = wrapper.current
    if (!element) {
      return
    }
    const run = () => {
      const target = element.querySelector<HTMLElement>(selector)
      if (acted.current || !target) {
        return false
      }
      acted.current = true
      act(target)
      return true
    }
    if (run()) {
      return
    }
    const observer = new MutationObserver(() => {
      if (run()) {
        observer.disconnect()
      }
    })
    observer.observe(element, { childList: true, subtree: true })
    return () => observer.disconnect()
  }, [selector, act])
  return wrapper
}

const click = (target: HTMLElement) => target.click()

/** Clicks the first element matching `selector` once the shell draws it, so a state opens or collapses it once. */
export function ClickOnMount({ selector, children }: { selector: string; children: ReactNode }) {
  const wrapper = useActOnMount(selector, click)
  return <div ref={wrapper}>{children}</div>
}

/** Types `text` after the value of the first input matching `selector` once the shell draws it, so a state shows a change. */
export function TypedOnMount({ selector, text, children }: { selector: string; text: string; children: ReactNode }) {
  const type = useCallback(
    (target: HTMLElement) => {
      const input = target as HTMLInputElement
      // React tracks an input's value, so the native setter lets its input event read the change.
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, input.value + text)
      input.dispatchEvent(new Event('input', { bubbles: true }))
    },
    [text],
  )
  const wrapper = useActOnMount(selector, type)
  return <div ref={wrapper}>{children}</div>
}
