import { useCallback, useEffect, useRef, type ReactNode } from 'react'
import { Route, Routes } from 'react-router'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { AccountPage } from '../pages/AccountPage'
import { HomePage } from '../pages/HomePage'
import { PageRoute, RecordPageRoute } from '../pages/PageRoute'
import { PlatformContext, type Platform } from '../pages/context'
import { PlatformShellRoute } from '../pages/PlatformShellRoute'
import { SiteRoute } from '../pages/SiteRoute'
import { StudioPage } from '../pages/StudioPage'
import { locale, platformSite } from './fixtures'

const platform: Platform = {
  site: platformSite,
  fallbackTexts: platformEnglish,
  current: { locale, texts: platformEnglish },
  setCurrent: () => {},
  development: true,
}

/** The shell's own routes over the fixtures: the platform home page, Studio, the account page, and the pages of an application site. */
export function ShellPreview() {
  return (
    <PlatformContext.Provider value={platform}>
      <Routes>
        <Route
          index
          element={
            <PlatformShellRoute>
              <HomePage />
            </PlatformShellRoute>
          }
        />
        <Route
          path="studio"
          element={
            <PlatformShellRoute>
              <StudioPage />
            </PlatformShellRoute>
          }
        />
        <Route
          path="account"
          element={
            <PlatformShellRoute>
              <AccountPage />
            </PlatformShellRoute>
          }
        />
        <Route path=":site" element={<SiteRoute />}>
          <Route path="records/:entity/:id" element={<RecordPageRoute />} />
          <Route path="records/:module/:entity/:id" element={<RecordPageRoute />} />
          <Route path=":page" element={<PageRoute />} />
          <Route path=":page/new" element={<PageRoute form="new" />} />
          <Route path=":page/:id" element={<PageRoute form="edit" />} />
          <Route path=":module/:page/new" element={<PageRoute form="new" />} />
          <Route path=":module/:page/:id" element={<PageRoute form="edit" />} />
        </Route>
      </Routes>
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
