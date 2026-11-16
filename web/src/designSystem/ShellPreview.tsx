import { useEffect, useRef, type ReactNode } from 'react'
import { Route, Routes } from 'react-router'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { HomePage } from '../pages/HomePage'
import { PageRoute } from '../pages/PageRoute'
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

/** The shell's own routes over the fixtures: the platform home page, Studio, and the pages of an application site. */
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
        <Route path=":site" element={<SiteRoute />}>
          <Route path=":page" element={<PageRoute />} />
          <Route path=":page/new" element={<PageRoute form="new" />} />
          {/* Proposed: a saved record's form page, as App.tsx routes it, see the review on #3650. */}
          <Route path=":page/:id" element={<PageRoute form="edit" />} />
          <Route path=":module/:page/new" element={<PageRoute form="new" />} />
          <Route path=":module/:page/:id" element={<PageRoute form="edit" />} />
        </Route>
      </Routes>
    </PlatformContext.Provider>
  )
}

/** Clicks the first button matching `selector` once the shell draws it, so a state opens or collapses it once. */
export function ClickOnMount({ selector, children }: { selector: string; children: ReactNode }) {
  const wrapper = useRef<HTMLDivElement>(null)
  const clicked = useRef(false)
  useEffect(() => {
    const element = wrapper.current
    if (!element) {
      return
    }
    const click = () => {
      const button = element.querySelector<HTMLButtonElement>(selector)
      if (clicked.current || !button) {
        return false
      }
      clicked.current = true
      button.click()
      return true
    }
    if (click()) {
      return
    }
    const observer = new MutationObserver(() => {
      if (click()) {
        observer.disconnect()
      }
    })
    observer.observe(element, { childList: true, subtree: true })
    return () => observer.disconnect()
  }, [selector])
  return <div ref={wrapper}>{children}</div>
}

/**
 * Proposed, see the review on #3650: types `text` at the end of the first input matching `selector` once the shell
 * draws it, as a person would, so a state shows a form with a change to save.
 */
export function TypedOnMount({ selector, text, children }: { selector: string; text: string; children: ReactNode }) {
  const wrapper = useRef<HTMLDivElement>(null)
  const typed = useRef(false)
  useEffect(() => {
    const element = wrapper.current
    if (!element) {
      return
    }
    const type = () => {
      const input = element.querySelector<HTMLInputElement>(selector)
      // The record's value arrives after the input, so the change waits for it.
      if (typed.current || !input || input.value === '') {
        return false
      }
      typed.current = true
      // The typing waits until React has drawn, as a person's would, so React does not flush inside an effect.
      setTimeout(() => {
        const setValue = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set
        setValue?.call(input, `${input.value}${text}`)
        input.dispatchEvent(new Event('input', { bubbles: true }))
      })
      return true
    }
    if (type()) {
      return
    }
    const observer = new MutationObserver(() => {
      if (type()) {
        observer.disconnect()
      }
    })
    observer.observe(element, { childList: true, subtree: true, attributes: true })
    return () => observer.disconnect()
  }, [selector, text])
  return <div ref={wrapper}>{children}</div>
}
