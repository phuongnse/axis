import { useCallback, useEffect, useState } from 'react'
import { apiFetch } from './api'
import { useCanvasActions } from './canvasActions'

/** A saved view of a table: a name and the search, filter and sort it restores. */
export interface SavedTableView {
  id: string
  name: string
  search: string | null
  filter: string | null
  sort: string | null
}

/** A person's choices for one table: the kept columns, or `null` when none are kept, and the saved views. */
export interface TableChoices {
  columns: string[] | null
  views: SavedTableView[]
}

/**
 * The table key of a table list on a site's page: `site.{site path}.{page name}` in lower case, with the `/` of a module
 * page's name written as `.`, see docs/reference/frontend.md#table-views-and-columns.
 */
export function siteTableKey(sitePath: string, pageName: string): string {
  return `site.${sitePath}.${pageName.replaceAll('/', '.')}`.toLowerCase()
}

/** The table key of a table list on a site's page, or `undefined` on the page designer's canvas, which never reads or writes a person's choices. */
export function useSiteTableKey(sitePath: string, pageName: string): string | undefined {
  const { active } = useCanvasActions()
  return active ? undefined : siteTableKey(sitePath, pageName)
}

const isChoices = (body: unknown): body is TableChoices => {
  if (typeof body !== 'object' || body === null || !('columns' in body)) {
    return false
  }
  const { columns } = body
  return columns === null || (Array.isArray(columns) && columns.every((column) => typeof column === 'string'))
}

const tablePath = (key: string) => `/api/me/tables/${encodeURIComponent(key)}`

/**
 * A person's kept choices for the table `key`, and the means to keep or forget its columns, see
 * docs/reference/frontend.md#table-views-and-columns. It asks the server once for each key. `choices` stays undefined
 * until they arrive, and for good when the request fails or the session has ended: the table then works as it did
 * without them, and the sign-in prompt stays hidden. `keepColumns` sends the shown columns, or forgets the choice for
 * `undefined`, and a failure is only logged. Without a key it does nothing.
 */
export function useTableChoices(key: string | undefined): {
  choices: TableChoices | undefined
  keepColumns: (columns: string[] | undefined) => void
} {
  // The key the choices belong to, so the choices of an earlier key never show for a new one.
  const [loaded, setLoaded] = useState<{ key: string; choices: TableChoices }>()

  useEffect(() => {
    if (key === undefined) {
      return
    }
    const controller = new AbortController()
    const { signal } = controller
    const load = async () => {
      try {
        const response = await apiFetch(tablePath(key), { signal }, { promptSignInOnUnauthorized: false })
        const body: unknown = response.status === 200 ? await response.json() : undefined
        if (isChoices(body)) {
          setLoaded({ key, choices: body })
        } else if (!signal.aborted) {
          console.warn(`Loading the choices of the table '${key}' failed with status ${response.status}`)
        }
      } catch (error: unknown) {
        if (!signal.aborted) {
          console.warn(`Loading the choices of the table '${key}' failed`, error)
        }
      }
    }
    void load()
    return () => controller.abort()
  }, [key])

  const keepColumns = useCallback(
    (columns: string[] | undefined) => {
      if (key === undefined) {
        return
      }
      const url = `${tablePath(key)}/columns`
      const request =
        columns === undefined
          ? apiFetch(url, { method: 'DELETE' })
          : apiFetch(url, {
              method: 'PUT',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({ columns }),
            })
      request
        .then((response) => {
          if (!response.ok) {
            console.warn(`Keeping the columns of the table '${key}' failed with status ${response.status}`)
          }
        })
        .catch((error: unknown) => console.warn(`Keeping the columns of the table '${key}' failed`, error))
    },
    [key],
  )

  return { choices: loaded !== undefined && loaded.key === key ? loaded.choices : undefined, keepColumns }
}
