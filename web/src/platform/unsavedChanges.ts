import { createContext, useContext, useEffect } from 'react'
import type { Blocker } from 'react-router'

/** Counts an editor in, for the guard of `UnsavedChangesGuard`. The returned function counts it out. It keeps its identity, so an editor stays counted in. */
export type Register = () => () => void

export const RegisterContext = createContext<Register | null>(null)
export const BlockerContext = createContext<Blocker | null>(null)

/**
 * The navigation state of a move that Studio makes after it deleted the resource whose draft is open, so the guard lets
 * it through, see docs/reference/studio.md#editor. The guard honours it only for the move itself, not when Back or
 * Forward returns to the entry that kept it.
 */
export const leaveWithoutAsking = { leaveWithoutAsking: true }

export function leavesWithoutAsking(state: unknown): boolean {
  return (
    typeof state === 'object' && state !== null && 'leaveWithoutAsking' in state && state.leaveWithoutAsking === true
  )
}

const viewParameters = ['search', 'sort', 'page', 'pageSize']

/**
 * Whether two addresses differ only in a table's view, its `search`, `sort`, `page` and `pageSize`, see
 * docs/reference/actions-and-controls.md#tables. Such a move keeps the editor on screen, so the guard does not ask.
 */
export function onlyTableViewChanged(
  current: { pathname: string; search: string },
  next: { pathname: string; search: string },
): boolean {
  const withoutView = (search: string) => {
    const params = new URLSearchParams(search)
    viewParameters.forEach((name) => params.delete(name))
    return params.toString()
  }
  return current.pathname === next.pathname && withoutView(current.search) === withoutView(next.search)
}

/**
 * Counts an editor in while it has unsaved changes, so the guard asks before they are left, see
 * docs/reference/studio.md#editor. It does nothing where no guard is mounted.
 */
export function useUnsavedChanges(unsaved: boolean): void {
  const register = useContext(RegisterContext)
  useEffect(() => (unsaved ? register?.() : undefined), [unsaved, register])
}
