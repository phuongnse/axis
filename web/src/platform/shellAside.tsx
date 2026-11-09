import { createContext, useContext, useLayoutEffect, type ReactNode } from 'react'
import { createPortal } from 'react-dom'

export interface ShellAsideState {
  /** The element in the shell's right column that holds the side panel, or `null` while no page claims it. */
  target: HTMLElement | null
  /** Registers a claim on the right column. The returned function releases it. */
  claim: () => () => void
}

export const ShellAsideContext = createContext<ShellAsideState>({ target: null, claim: () => () => {} })

/**
 * Proposed: draws a side panel into the shell's right column, which runs from under the header to the bottom of the
 * window, as the sidebar does on the left. The column shows only while a page renders this. See the review on #3263.
 */
export function ShellAside({ children }: { children: ReactNode }) {
  const { target, claim } = useContext(ShellAsideContext)
  useLayoutEffect(() => claim(), [claim])
  return target ? createPortal(children, target) : null
}
