import { createContext, useContext, useEffect } from 'react'

/**
 * Proposed: a page that has its own navigation, such as Studio, draws it in the shell's sidebar. The shell gives the
 * element the sidebar draws in, whether the sidebar is collapsed, and a way to claim it; the page portals its
 * navigation into that element while it is mounted.
 */
export interface ShellSidebar {
  slot: HTMLElement | null
  collapsed: boolean
  claim: (claimed: boolean) => void
}

export const ShellSidebarContext = createContext<ShellSidebar | null>(null)

/** Claims the shell's sidebar while the calling page is mounted, and gives the element to draw in, or null. */
export function useShellSidebar(): { slot: HTMLElement | null; collapsed: boolean } {
  const sidebar = useContext(ShellSidebarContext)
  const claim = sidebar?.claim
  useEffect(() => {
    if (!claim) {
      return
    }
    claim(true)
    return () => claim(false)
  }, [claim])
  return { slot: sidebar?.slot ?? null, collapsed: sidebar?.collapsed ?? false }
}
