/**
 * The form field of the longest prefix of `path` that the form shows, so a path inside a field lands on that field. The
 * editor uses it for the form, and the entity designer for the field drawer, see docs/reference/entity-designer.md#diagnostics.
 */
export function fieldElement(root: HTMLElement, path: string): HTMLElement | undefined {
  const fields = new Map<string, HTMLElement>()
  for (const element of root.querySelectorAll<HTMLElement>('[data-testid^="studio-field-"]')) {
    fields.set(element.getAttribute('data-testid')!.slice('studio-field-'.length), element)
  }
  let prefix = path
  while (prefix !== '') {
    const element = fields.get(prefix)
    if (element) {
      return element
    }
    prefix = prefix.slice(0, Math.max(prefix.lastIndexOf('/'), 0))
  }
  return undefined
}
