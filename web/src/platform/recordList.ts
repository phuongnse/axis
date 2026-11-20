import { createContext } from 'react'

/** The list a page was opened from: the text key of its navigation entry's label, and the address that opens it. */
export interface RecordListValue {
  labelKey: string
  to: string
}

/** The list that `PageRoute` gives the page's widgets, which `RecordNotFound` links back to. */
export const RecordList = createContext<RecordListValue | null>(null)
