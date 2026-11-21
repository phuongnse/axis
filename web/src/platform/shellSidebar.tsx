import { useContext, useLayoutEffect, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { ContentInsetContext } from './contentInsetContext'
import { ShellSidebarContext } from './shellSidebarContext'

/**
 * Claims the shell's sidebar for a page. The shell draws the children in the sidebar in place of the site's
 * navigation, and shows the sidebar and its collapse button even on a one-page site. The claim ends when this
 * component unmounts. Children read whether the sidebar is collapsed with `useShellSidebar`. The sidebar is outside the
 * content area's padding, so what it holds reads an inset of 0, see docs/reference/shell.md#layout.
 */
export function ShellSidebar({ children }: { children: ReactNode }) {
  const { target, claim } = useContext(ShellSidebarContext)
  useLayoutEffect(() => claim(), [claim])
  return target
    ? createPortal(<ContentInsetContext.Provider value={0}>{children}</ContentInsetContext.Provider>, target)
    : null
}
