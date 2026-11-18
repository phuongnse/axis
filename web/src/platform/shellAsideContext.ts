import { createContext } from 'react'

export interface ShellAsideState {
  /** The element in the right column that holds a claiming page's content, or `null` while there is none. */
  target: HTMLElement | null
  /** Registers a claim on the right column. The returned function releases it. */
  claim: () => () => void
}

export const ShellAsideContext = createContext<ShellAsideState>({
  target: null,
  claim: () => () => {},
})
