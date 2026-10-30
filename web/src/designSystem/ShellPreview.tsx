import { useEffect, useRef, type ReactNode } from 'react'
import { Route, Routes } from 'react-router'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { HomePage } from '../pages/HomePage'
import { PageRoute } from '../pages/PageRoute'
import { PlatformContext, type Platform } from '../pages/context'
import { PlatformShellRoute } from '../pages/PlatformShellRoute'
import { SiteRoute } from '../pages/SiteRoute'
import { locale, platformSite } from './fixtures'

const platform: Platform = {
  site: platformSite,
  fallbackTexts: platformEnglish,
  current: { locale, texts: platformEnglish },
  setCurrent: () => {},
  development: true,
}

/** The shell's own routes over the fixtures: the platform home page, and the pages of an application site. */
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
        <Route path=":site" element={<SiteRoute />}>
          <Route path=":page" element={<PageRoute />} />
          <Route path=":page/new" element={<PageRoute form="new" />} />
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
