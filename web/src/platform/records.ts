/**
 * A value of a record as the SPA holds it. Integers and decimals keep their JSON source text, so
 * they are strings.
 */
export type RecordValue = string | boolean | null

/** One record of the record API. `version` is record metadata, so it is a number. */
export interface RecordItem {
  id: string
  version: number
  values: Record<string, RecordValue>
  labels: Record<string, string>
}

/** One page of records of the record API. */
export interface RecordPage {
  items: RecordItem[]
  page: number
  pageSize: number
  totalCount: number
}

// The reviver context of "JSON.parse source text access" is not in the TypeScript lib yet.
type SourceContext = { source?: string }
type SourceReviver = (key: string, value: unknown, context?: SourceContext) => unknown

/**
 * Parses a record API response and turns every JSON number into its source text, so a decimal
 * such as `1250.50` keeps its digits. The `String(value)` fallback serves browsers without
 * source text access.
 */
export function parseRecordJson<T>(text: string): T {
  const parse = JSON.parse as (text: string, reviver: SourceReviver) => T
  return parse(text, (_key, value, context) => (typeof value === 'number' ? (context?.source ?? String(value)) : value))
}

/** Loads one page of records. `query` holds the record API's `page`, `pageSize` and `sort`. */
export async function fetchRecords(recordsPath: string, query: string, signal?: AbortSignal): Promise<RecordPage> {
  const response = await fetch(`${recordsPath}${query ? `?${query}` : ''}`, { signal })
  if (!response.ok) {
    throw new Error(`Loading the records of '${recordsPath}' failed with status ${response.status}.`)
  }
  // The reviver turned the metadata numbers into strings too. Only `values` keep the source text.
  const body = parseRecordJson<{
    items: (Omit<RecordItem, 'version'> & { version: string })[]
    page: string
    pageSize: string
    totalCount: string
  }>(await response.text())
  return {
    items: body.items.map((item) => ({ ...item, version: Number(item.version) })),
    page: Number(body.page),
    pageSize: Number(body.pageSize),
    totalCount: Number(body.totalCount),
  }
}
