import { createContext, useCallback, useContext, useEffect } from 'react'

export interface LeaveGuard {
  /** Counts an editor with unsaved changes in, and returns how to count it out. */
  hold: () => () => void
  /** Runs `go` at once with nothing unsaved, else once the person confirms they leave without saving. */
  guard: (go: () => void) => void
}

export const LeaveGuardContext = createContext<LeaveGuard | null>(null)

/**
 * Proposed: counts the calling editor in while `dirty`, so leaving asks first, and has the browser ask on a reload or
 * on closing the tab. `SaveBar` calls it, so an editor with a save bar needs nothing more.
 */
export function useUnsavedChanges(dirty: boolean) {
  const hold = useContext(LeaveGuardContext)?.hold
  useEffect(() => {
    if (!dirty || !hold) {
      return
    }
    const release = hold()
    const ask = (event: BeforeUnloadEvent) => event.preventDefault()
    window.addEventListener('beforeunload', ask)
    return () => {
      release()
      window.removeEventListener('beforeunload', ask)
    }
  }, [dirty, hold])
}

/**
 * Proposed: a navigation that a page starts by itself, such as a button that opens a resource, goes through the guard,
 * as a link does. Outside a guard it runs at once.
 */
export function useGuard(): (go: () => void) => void {
  const guard = useContext(LeaveGuardContext)?.guard
  return useCallback((go: () => void) => (guard ? guard(go) : go()), [guard])
}
