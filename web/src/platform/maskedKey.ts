/**
 * A stored key as a screen shows it: `••••` and the key's last four characters, never the key, see
 * docs/reference/model-bindings.md#a-persons-own-key and docs/reference/platform-pages.md#model-key.
 */
export function maskedKey(ending: string | null | undefined): string {
  return `••••${ending ?? ''}`
}
