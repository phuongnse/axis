import { apiFetch } from './api'

/** What a form shows of one field, as the server's form state endpoint answers. */
export interface FieldState {
  visible: boolean
  required: boolean
  readOnly: boolean
}

/**
 * The answer of the form state endpoint. `errors` maps a JSON Pointer such as `/values/title` to its
 * messages, for the fields the user touched.
 */
export interface FormStateAnswer {
  sections: { name: string; visible: boolean }[]
  fields: Record<string, FieldState>
  errors: Record<string, string[]>
}

const recordsSuffix = /\/entities\/[^/]+\/records$/

/**
 * The form state endpoint of a form, worked out from the records path of the form's entity: for
 * `/api/apps/E2eApp/entities/Note/records` and the form `NoteSections` it is
 * `/api/apps/E2eApp/forms/NoteSections/state`.
 */
export function formStatePath(recordsPath: string, form: string): string {
  return `${recordsPath.replace(recordsSuffix, '')}/forms/${encodeURIComponent(form)}/state`
}

/** Asks the form state endpoint for the states of a draft. Any answer but a `200` throws. */
export async function fetchFormState(statePath: string, body: string, signal?: AbortSignal): Promise<FormStateAnswer> {
  const response = await apiFetch(statePath, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body,
    signal,
  })
  if (response.status !== 200) {
    throw new Error(`Loading the form state from '${statePath}' failed with status ${response.status}.`)
  }
  // The answer holds no field values, so plain numbers are safe.
  return JSON.parse(await response.text()) as FormStateAnswer
}
