import { apiFetch } from './api'
import type { RecordProblem } from './records'

/** The outcome of a start: the instance started, or the problem the server reported. */
export type StartResult = { ok: true } | { ok: false; problem: RecordProblem }

/**
 * Starts a process for one subject record by posting to the start endpoint at `instancesPath`. A
 * 201 is a start. A 400, 404, 409 or 422 is a final answer and comes back as the problem. Any other
 * answer, and a network error, throws, so the caller may retry with the same `idempotencyKey`.
 */
export async function startProcess(
  instancesPath: string,
  subjectId: string,
  idempotencyKey: string,
): Promise<StartResult> {
  const response = await apiFetch(instancesPath, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'Idempotency-Key': idempotencyKey },
    body: JSON.stringify({ subjectId }),
  })
  if (response.status === 201) {
    return { ok: true }
  }
  if ([400, 404, 409, 422].includes(response.status)) {
    const problem = JSON.parse(await response.text()) as Partial<RecordProblem>
    return {
      ok: false,
      problem: { status: response.status, title: problem.title ?? '', errors: problem.errors ?? {} },
    }
  }
  throw new Error(`Starting the process at '${instancesPath}' failed with status ${response.status}.`)
}

/**
 * A new `Idempotency-Key`. `crypto.randomUUID` exists only in a secure context, so a page served
 * over plain HTTP on another host than localhost builds a version 4 UUID from random bytes instead.
 */
export function newIdempotencyKey(): string {
  if (typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID()
  }
  const bytes = crypto.getRandomValues(new Uint8Array(16))
  bytes[6] = (bytes[6] & 0x0f) | 0x40
  bytes[8] = (bytes[8] & 0x3f) | 0x80
  const hex = Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('')
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
}
