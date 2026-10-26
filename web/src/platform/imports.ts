import { apiFetch } from './api'

/** The state of an import: `pending` until the dry run ends, then `checked` or `failed`, and `committed` after a commit. */
export type ImportState = 'pending' | 'checked' | 'failed' | 'committed'

/** An import as the server answers it. `errors` holds the messages by JSON pointer, in row order. */
export interface RecordImport {
  id: string
  state: ImportState
  errors: Record<string, string[]>
}

/** The outcome of a request on an import: the import, or the refused response's status. */
export type ImportResult = { ok: true; import: RecordImport } | { ok: false; status: number }

const csvType = 'text/csv'
const xlsxType = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'

/** The imports route of an entity, from its records route. Both share their prefix. */
export function importsPath(recordsPath: string): string {
  return recordsPath.replace(/\/records$/, '/imports')
}

/**
 * The content type the server expects for `file`, from its extension ignoring case, or `null` when
 * the file is neither CSV nor xlsx. Browsers often report a CSV with another type or none.
 */
export function importContentType(file: { name: string }): string | null {
  const name = file.name.toLowerCase()
  if (name.endsWith('.csv')) {
    return csvType
  }
  return name.endsWith('.xlsx') ? xlsxType : null
}

async function result(response: Response, accepted: number): Promise<ImportResult> {
  if (response.status !== accepted) {
    return { ok: false, status: response.status }
  }
  const body = (await response.json()) as Partial<RecordImport>
  return { ok: true, import: { id: body.id!, state: body.state!, errors: body.errors ?? {} } }
}

/** Uploads `file` as the raw body of a new import at `path`. A 202 is an accepted, pending import. A network error throws. */
export async function uploadImport(path: string, file: File, contentType: string): Promise<ImportResult> {
  const response = await apiFetch(path, { method: 'POST', headers: { 'Content-Type': contentType }, body: file })
  return result(response, 202)
}

/** Reads the import `id` at `path`. A network error throws. */
export async function fetchImport(path: string, id: string, signal?: AbortSignal): Promise<ImportResult> {
  return result(await apiFetch(`${path}/${id}`, { signal }), 200)
}

/** Commits the checked import `id` at `path`. A 202 is a queued commit. A network error throws. */
export async function commitImport(path: string, id: string): Promise<ImportResult> {
  return result(await apiFetch(`${path}/${id}/commit`, { method: 'POST' }), 202)
}

/** One error of an import, with where it is: a row and a field, a row, a column, or the whole file. */
export type ImportError =
  | { kind: 'field'; row: number; field: string; message: string }
  | { kind: 'row'; row: number; message: string }
  | { kind: 'column'; column: number; message: string }
  | { kind: 'file'; message: string }

/** Parses the error keys, `/rows/{n}/values/{field}`, `/rows/{n}`, `/columns/{n}` and any other as the file's, keeping the server's order. */
export function importErrorRows(errors: Record<string, string[]>): ImportError[] {
  return Object.entries(errors).flatMap(([key, messages]) =>
    messages.map((message): ImportError => {
      const field = /^\/rows\/(\d+)\/values\/([^/]+)/.exec(key)
      if (field) {
        return { kind: 'field', row: Number(field[1]), field: field[2], message }
      }
      const row = /^\/rows\/(\d+)$/.exec(key)
      if (row) {
        return { kind: 'row', row: Number(row[1]), message }
      }
      const column = /^\/columns\/(\d+)$/.exec(key)
      return column ? { kind: 'column', column: Number(column[1]), message } : { kind: 'file', message }
    }),
  )
}
