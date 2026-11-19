import { useMemo, useState, type ReactNode } from 'react'
import { ShellAsideContext } from '../platform/shellAsideContext'

/**
 * Stands in for the shell's right column, which a side panel draws in: the children, and under them the element that
 * holds the panels they claim. A bare provider shows every claim, as the shell shows the newest.
 */
export function ShellAsideHost({ children }: { children: ReactNode }) {
  const [target, setTarget] = useState<HTMLElement | null>(null)
  const value = useMemo(() => ({ target, claim: () => () => {} }), [target])
  return (
    <ShellAsideContext.Provider value={value}>
      {children}
      <div ref={setTarget} data-testid="shell-aside-host" />
    </ShellAsideContext.Provider>
  )
}
