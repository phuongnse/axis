import { createContext } from 'react'

/**
 * Proposed, see the review on #3263: the resources a draft adds, which the application does not hold yet, by their
 * file, with their name and kind as the draft's diff names them, and how to show a change of the draft. A request's
 * page gives it once its draft's diff has loaded, so every pointer on the page to such a resource reads by its name
 * and shows its change, whatever draws the pointer.
 */
export const DraftResourcesContext = createContext<{
  added: ReadonlyMap<string, { name: string; kind: string }>
  show: (file: string) => void
} | null>(null)
