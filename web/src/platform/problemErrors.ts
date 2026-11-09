import { pointerSegments } from './jsonPatch'

export interface PlacedProblemErrors<F> {
  /** The messages of each key that names a field, in key order. */
  fields: { field: F; messages: string[] }[]
  /** The messages of every other key, which show above the form. */
  form: string[]
}

/**
 * Where each message of a refused request goes, as docs/reference/shared-widgets.md#which-widget says.
 * `fieldOf` gets the decoded segments of a key's JSON Pointer and returns the field it names, or `null`
 * when the form shows no such field. A key that is not a pointer, such as `""`, never names a field.
 */
export function placeProblemErrors<F>(
  errors: Record<string, string[]>,
  fieldOf: (segments: string[]) => F | null,
): PlacedProblemErrors<F> {
  const placed: PlacedProblemErrors<F> = { fields: [], form: [] }
  for (const [key, messages] of Object.entries(errors)) {
    const field = key.startsWith('/') ? fieldOf(pointerSegments(key)) : null
    if (field === null) {
      placed.form.push(...messages)
    } else {
      placed.fields.push({ field, messages })
    }
  }
  return placed
}
