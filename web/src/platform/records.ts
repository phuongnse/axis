/**
 * A value of a record as the SPA holds it. Integers and decimals keep their JSON source text, so
 * they are strings.
 */
export type RecordValue = string | boolean | null

/** One row of a child collection: its child fields' values. */
export type RecordRow = Record<string, RecordValue>

/** A value of a record field. A child collection holds its rows in order. */
export type FieldValue = RecordValue | RecordRow[]

/** One record of the record API. `version` is record metadata, so it is a number. */
export interface RecordItem {
  id: string
  version: number
  values: Record<string, FieldValue>
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

/** A 400 answer to a records or rows request. `errors` maps a query parameter name to its messages. */
export class RecordQueryProblem extends Error {
  readonly errors: Record<string, string[]>

  constructor(errors: Record<string, string[]>) {
    super('The records request was rejected.')
    this.name = 'RecordQueryProblem'
    this.errors = errors
  }
}

/**
 * Loads one page of records. `query` holds the record API's `page`, `pageSize` and `sort`, and a
 * data source's parameter values. A 400 throws a {@link RecordQueryProblem}.
 */
export async function fetchRecords(recordsPath: string, query: string, signal?: AbortSignal): Promise<RecordPage> {
  const response = await fetch(`${recordsPath}${query ? `?${query}` : ''}`, { signal })
  if (response.status === 400) {
    const problem = JSON.parse(await response.text()) as { errors?: Record<string, string[]> }
    throw new RecordQueryProblem(problem.errors ?? {})
  }
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

/** A 400 or 409 problem of the record API. `errors` maps a JSON Pointer into the body to its messages. */
export interface RecordProblem {
  status: number
  title: string
  errors: Record<string, string[]>
}

/** The outcome of a create or update: the saved record, or the problem the server reported. */
export type SaveResult = { ok: true; record: RecordItem } | { ok: false; problem: RecordProblem }

function parseRecord(text: string): RecordItem {
  // The reviver turned `version` into a string too. Only `values` keep the source text.
  const record = parseRecordJson<Omit<RecordItem, 'version'> & { version: string }>(text)
  return { ...record, version: Number(record.version) }
}

/** Loads one record, or returns `null` when the entity has no record with the id. */
export async function fetchRecord(recordsPath: string, id: string, signal?: AbortSignal): Promise<RecordItem | null> {
  const response = await fetch(`${recordsPath}/${id}`, { signal })
  if (response.status === 404) {
    return null
  }
  if (!response.ok) {
    throw new Error(`Loading the record '${id}' of '${recordsPath}' failed with status ${response.status}.`)
  }
  return parseRecord(await response.text())
}

/**
 * Creates a record when `id` is `null`, and updates the record otherwise. `body` is the prepared
 * body text, so number text reaches the server unchanged. A 400 or 409 comes back as the problem;
 * any other failure throws.
 */
export async function saveRecord(
  recordsPath: string,
  id: string | null,
  body: string,
  signal?: AbortSignal,
): Promise<SaveResult> {
  const response = await fetch(id === null ? recordsPath : `${recordsPath}/${id}`, {
    method: id === null ? 'POST' : 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body,
    signal,
  })
  if (response.status === 200 || response.status === 201) {
    return { ok: true, record: parseRecord(await response.text()) }
  }
  if (response.status === 400 || response.status === 409) {
    const problem = JSON.parse(await response.text()) as Partial<RecordProblem>
    return {
      ok: false,
      problem: { status: response.status, title: problem.title ?? '', errors: problem.errors ?? {} },
    }
  }
  throw new Error(`Saving a record of '${recordsPath}' failed with status ${response.status}.`)
}
