import { createContext } from 'react'

/** The padding of the shell's content area, which a band takes as its bleed to the page area's edges, see docs/reference/shell.md#layout. 0 outside a shell. */
export const ContentInsetContext = createContext(0)
