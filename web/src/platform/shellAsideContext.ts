import { createContext } from 'react'

export interface ShellAsideState {
  /** The element in the shell's right column that holds the side panel, or `null` while no page claims it. */
  target: HTMLElement | null
  /** Registers a claim on the right column. The returned function releases it. */
  claim: () => () => void
}

export const ShellAsideContext = createContext<ShellAsideState>({ target: null, claim: () => () => {} })
