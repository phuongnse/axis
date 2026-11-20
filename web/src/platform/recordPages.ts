import { createContext, useContext } from 'react'
import { siteAddress } from './siteLinks'

/** The site a form is drawn in and the record page address of each of its entities, by the entity's name. */
export interface RecordPagesValue {
  sitePath: string
  recordPages: Readonly<Record<string, string>>
}

/**
 * The record pages of the site that `SiteRoute` opened. It is `null` outside a site, such as in Studio's page designer
 * and in the widget previews, where a reference has no page to open.
 */
export const RecordPagesContext = createContext<RecordPagesValue | null>(null)

/**
 * Gives the address of a record's page, through `siteAddress`, or `null` when the record's entity has no entry in the
 * site's `recordPages`. The entry is an address `/{site}/{page}` that the SPA treats as opaque: the record's id is its
 * last segment. See docs/reference/shared-widgets.md#which-widget.
 */
export function useRecordOpener(): (entity: string, id: string) => string | null {
  const context = useContext(RecordPagesContext)
  return (entity, id) => {
    if (!context || !Object.hasOwn(context.recordPages, entity)) {
      return null
    }
    const address = context.recordPages[entity]
    const prefix = `/${context.sitePath}/`
    const rest = address.toLowerCase().startsWith(prefix.toLowerCase())
      ? address.slice(prefix.length)
      : address.slice(1)
    return siteAddress(context.sitePath, `${rest}/${encodeURIComponent(id)}`)
  }
}
