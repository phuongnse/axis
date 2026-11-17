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

const isText = (value: unknown): value is string | null => value === null || typeof value === 'string'

const isView = (value: unknown): value is SavedTableView =>
  typeof value === 'object' &&
  value !== null &&
  'id' in value &&
  typeof value.id === 'string' &&
  'name' in value &&
  typeof value.name === 'string' &&
  'search' in value &&
  isText(value.search) &&
  'filter' in value &&
  isText(value.filter) &&
  'sort' in value &&
  isText(value.sort)

const isChoices = (body: unknown): body is TableChoices => {
  if (typeof body !== 'object' || body === null || !('columns' in body) || !('views' in body)) {
    return false
  }
  const { columns, views } = body
  return (
    (columns === null || (Array.isArray(columns) && columns.every((column) => typeof column === 'string'))) &&
    Array.isArray(views) &&
    views.every(isView)
  )
}

const tablePath = (key: string) => `/api/me/tables/${encodeURIComponent(key)}`

/** What a saved view holds besides its identity: the name and the search, filter and sort as the address writes them. */
export type SavedTableViewFields = Omit<SavedTableView, 'id'>

/**
 * A person's kept choices for the table `key`, and the means to keep or forget its columns and to save, rename and
 * delete its views, see docs/reference/frontend.md#table-views-and-columns. It asks the server once for each key.
 * `choices` stays undefined until they arrive, and for good when the request fails or the session has ended: the table
 * then works as it did without them, and the sign-in prompt stays hidden. `keepColumns` sends the shown columns, or
 * forgets the choice for `undefined`, and a failure is only logged. `saveView`, `renameView` and `deleteView` answer
 * whether the server took the write, and change the views in `choices` only then. A failure is logged. Without a key
 * they do nothing and answer false.
 */
export function useTableChoices(key: string | undefined): {
  choices: TableChoices | undefined
  keepColumns: (columns: string[] | undefined) => void
  saveView: (fields: SavedTableViewFields) => Promise<boolean>
  renameView: (view: SavedTableView, name: string) => Promise<boolean>
  deleteView: (view: SavedTableView) => Promise<boolean>
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

  /** Sends a view write, and on the `expected` status changes the loaded views of this key with `apply`. */
  const writeView = useCallback(
    async (
      path: string,
      init: RequestInit,
      expected: number,
      apply: (views: SavedTableView[], body: unknown) => SavedTableView[],
    ): Promise<boolean> => {
      if (key === undefined) {
        return false
      }
      try {
        const response = await apiFetch(`${tablePath(key)}/views${path}`, init)
        if (response.status !== expected) {
          console.warn(`Writing a view of the table '${key}' failed with status ${response.status}`)
          return false
        }
        const body: unknown = expected === 201 ? await response.json() : undefined
        if (expected === 201 && !isView(body)) {
          console.warn(`Writing a view of the table '${key}' answered with something that is not a view`)
          return false
        }
        setLoaded((current) =>
          current !== undefined && current.key === key
            ? { key, choices: { ...current.choices, views: apply(current.choices.views, body) } }
            : current,
        )
        return true
      } catch (error: unknown) {
        console.warn(`Writing a view of the table '${key}' failed`, error)
        return false
      }
    },
    [key],
  )

  const saveView = useCallback(
    (fields: SavedTableViewFields) =>
      writeView(
        '',
        { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(fields) },
        201,
        (views, body) => [...views, body as SavedTableView],
      ),
    [writeView],
  )

  const renameView = useCallback(
    (view: SavedTableView, name: string) => {
      const { id, ...fields } = view
      return writeView(
        `/${encodeURIComponent(id)}`,
        { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ ...fields, name }) },
        204,
        (views) => views.map((other) => (other.id === id ? { ...other, name } : other)),
      )
    },
    [writeView],
  )

  const deleteView = useCallback(
    (view: SavedTableView) =>
      writeView(`/${encodeURIComponent(view.id)}`, { method: 'DELETE' }, 204, (views) =>
        views.filter((other) => other.id !== view.id),
      ),
    [writeView],
  )

  return {
    choices: loaded !== undefined && loaded.key === key ? loaded.choices : undefined,
    keepColumns,
    saveView,
    renameView,
    deleteView,
  }
}
