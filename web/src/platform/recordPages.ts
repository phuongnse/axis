import { createContext, useContext, type MouseEvent } from 'react'
import { useLocation, useNavigate } from 'react-router'

/**
 * Proposed, see the review on #3650: where each entity's records open in the current site, as the address of its record
 * page, `/{site}/{page}`, for the entities the person may read. The site names a page for an entity, else the entity
 * opens on the platform's default record page. Outside a site there is none, so a reference shows as plain text.
 */
export const RecordPagesContext = createContext<Readonly<Record<string, string>>>({})

/** The address that opens a record of `entity` in the current site, or `null` when the person cannot open it there. */
export function useRecordAddress(): (entity: string, id: string) => string | null {
  const pages = useContext(RecordPagesContext)
  return (entity, id) => {
    const page = Object.hasOwn(pages, entity) ? pages[entity] : undefined
    return page === undefined ? null : `${page}/${encodeURIComponent(id)}`
  }
}

/**
 * Proposed, see the review on #3650: how a control opens a record, as a real address and a click that goes there in the
 * SPA, passing the page it leaves as `from`, or `null` when the person cannot open the record in the site. A click
 * with Ctrl, Cmd, Shift or a middle button is the browser's, so it opens a new tab or window.
 */
export function useRecordOpener(entity: string, id: string | null) {
  const address = useRecordAddress()
  const navigate = useNavigate()
  const location = useLocation()
  const href = id === null ? null : address(entity, id)
  if (href === null) {
    return null
  }
  const from = `${location.pathname}${location.search}`
  return {
    href,
    from,
    onClick: (event: MouseEvent) => {
      if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) {
        return
      }
      event.preventDefault()
      void navigate(href, { state: { from } })
    },
  }
}
