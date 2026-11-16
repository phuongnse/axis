import { createContext } from 'react'

/**
 * Proposed, see the review on #3650: the place in the shell's content column, between the page and the footer, where
 * the save bar draws. It sticks to the window's foot while the page scrolls and rests right above the footer at the
 * page's end, as wide as the content, so it never covers the sidebar or the footer. Outside a shell there is none.
 */
export const ShellBarSlot = createContext<HTMLElement | null | undefined>(undefined)
