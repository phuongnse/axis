import { createContext } from 'react'

/**
 * The least height of the shell: the browser window in the SPA. A design-system preview draws the
 * shell in a frame of its own height instead, as the window there is the whole card.
 */
export const ShellHeightContext = createContext('100vh')
