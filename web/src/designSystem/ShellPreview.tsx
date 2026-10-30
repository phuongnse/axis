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

/** Collapses the navigation sidebar once the shell draws its collapse button, so the shell starts collapsed. */
export function CollapsedOnMount({ children }: { children: ReactNode }) {
  const wrapper = useRef<HTMLDivElement>(null)
  const collapsed = useRef(false)
  useEffect(() => {
    const element = wrapper.current
    if (!element) {
      return
    }
    const collapse = () => {
      const button = element.querySelector<HTMLButtonElement>(
        'button[aria-controls="shell-sidebar"][aria-expanded="true"]',
      )
      if (collapsed.current || !button) {
        return false
      }
      collapsed.current = true
      button.click()
      return true
    }
    if (collapse()) {
      return
    }
    const observer = new MutationObserver(() => {
      if (collapse()) {
        observer.disconnect()
      }
    })
    observer.observe(element, { childList: true, subtree: true })
    return () => observer.disconnect()
  }, [])
  return <div ref={wrapper}>{children}</div>
}
