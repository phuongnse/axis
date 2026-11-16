import { createContext } from 'react'

/**
 * Proposed, see the review on #3650: the list a record's page leads back to, its address and its label. A page outside
 * a site has none.
 */
export const RecordList = createContext<{ to: string; label: string } | undefined>(undefined)
