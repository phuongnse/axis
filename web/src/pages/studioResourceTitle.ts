import { createContext } from 'react'

/**
 * Proposed: the label Studio names a resource of the open application by, from its id, or undefined for a resource it
 * does not list. Every place in Studio that names a resource reads it, so no place shows a technical name.
 */
export const StudioResourceTitle = createContext<(id: string) => string | undefined>(() => undefined)
