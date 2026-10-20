import { apiFetch } from './api'
import type { RecordProblem } from './records'
import { newUuid } from './uuid'

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

/** A new `Idempotency-Key`: a version 4 UUID. */
export function newIdempotencyKey(): string {
  return newUuid()
}
