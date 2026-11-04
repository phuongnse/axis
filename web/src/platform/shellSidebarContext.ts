import { createContext, useContext } from 'react'

export interface ShellSidebarState {
  /** The element in the sidebar that holds a claiming page's content, or `null` while there is none. */
  target: HTMLElement | null
  collapsed: boolean
  /** Registers a claim on the sidebar. The returned function releases it. */
  claim: () => () => void
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
