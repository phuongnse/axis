import { createContext, useContext, useLayoutEffect } from 'react'

export interface ShellSidebarState {
  /** The element in the sidebar that holds a claiming page's content, or `null` while there is none. */
  target: HTMLElement | null
  collapsed: boolean
  /** Registers a claim on the sidebar. The returned function releases it. */
  claim: () => () => void
  /** Proposed: the padding round the page area, `spacing-md` for a workspace that claimed the sidebar, else `spacing-lg`. */
  contentPadding?: number
}

export const ShellSidebarContext = createContext<ShellSidebarState>({
  target: null,
  collapsed: false,
  claim: () => () => {},
})

/** What a page that claims the sidebar needs to draw its content: whether the sidebar is collapsed. */
export function useShellSidebar(): { collapsed: boolean } {
  const { collapsed } = useContext(ShellSidebarContext)
  return { collapsed }
}

/**
 * Proposed: claims the shell's sidebar while the calling page is mounted and gives the element to draw in, for a page
 * that draws its own navigation with a portal, such as Studio's workspace, and whether the sidebar is collapsed.
 */
export function useClaimedShellSidebar(): { slot: HTMLElement | null; collapsed: boolean } {
  const { target, claim, collapsed } = useContext(ShellSidebarContext)
  useLayoutEffect(() => claim(), [claim])
  return { slot: target, collapsed }
}
