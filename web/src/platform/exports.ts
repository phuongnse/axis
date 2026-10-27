import { apiFetch } from './api'
import { recordQuery, type TableQuery } from './tableQuery'
import type { WidgetMetadata } from './site'

/** The formats an export offers, as the route's `format`. */
export type ExportFormat = 'csv' | 'xlsx'

/** The outcome of an export request: the file with its name, or the refused response's status. */
export type ExportResult =
  { ok: true; blob: Blob; fileName: string; truncated: boolean } | { ok: false; status: number }

/**
 * The export route of a table: its entity's records route, or its data source's rows route, with
 * `export` in place of `rows`. A module data source keeps its module segment.
 */
export function exportPath(widget: Pick<WidgetMetadata, 'entity' | 'dataSource'>): string {
  // The server sets exactly one of the two.
  return widget.dataSource
    ? widget.dataSource.rowsPath.replace(/\/rows$/, '/export')
    : `${widget.entity!.recordsPath}/export`
}

/**
 * The address of an export: the table's parameter values, search and sort as its rows request has
 * them, then `format`. An export holds every matching row, so it has no `page` or `pageSize`.
 */
export function exportAddress(
  path: string,
  query: TableQuery,
  parameters: Record<string, string>,
  format: ExportFormat,
  defaultPageSize: number,
): string {
  // The paging of the table is reset to the defaults, so recordQuery leaves it out.
  const params = new URLSearchParams(
    recordQuery({ ...query, parameters, page: 1, pageSize: defaultPageSize }, defaultPageSize),
  )
  params.set('format', format)
  return `${path}?${params}`
}

function decode(value: string): string | null {
  try {
    return decodeURIComponent(value)
  } catch {
    return null
  }
}

/** The file name of a `Content-Disposition` header: `filename*` decoded if it has one, then `filename`. */
function fileNameOf(disposition: string | null): string | null {
  const encoded = /filename\*\s*=\s*UTF-8''([^;]+)/i.exec(disposition ?? '')
  const decoded = encoded ? decode(encoded[1].trim()) : null
  if (decoded) {
    return decoded
  }
  const plain = /filename\s*=\s*(?:"([^"]*)"|([^;]+))/i.exec(disposition ?? '')
  return (plain?.[1] ?? plain?.[2])?.trim() || null
}

/**
 * Requests the export at `address`. A 200 is the file, with the name the server gave it and whether it
 * stopped at the row limit. Any other status comes back as `{ ok: false, status }`. A network error throws.
 */
export async function downloadExport(address: string): Promise<ExportResult> {
  const response = await apiFetch(address)
  if (!response.ok) {
    return { ok: false, status: response.status }
  }
  const format = new URL(address, window.location.origin).searchParams.get('format')
  return {
    ok: true,
    blob: await response.blob(),
    fileName: fileNameOf(response.headers.get('Content-Disposition')) ?? `export.${format}`,
    truncated: response.headers.get('Axis-Export-Truncated') === 'true',
  }
}

/** Saves `blob` as `name` through a temporary download link, which the browser handles as a download. */
export function saveFile(blob: Blob, name: string): void {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = name
  document.body.append(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}
