/**
 * Focuses the input of the first cell with a problem, for a click on Save that finds a wrong value, see
 * docs/reference/actions-and-controls.md#fields. `problems` is in grid order, so its first key is the first cell.
 * Returns whether a cell took the focus.
 */
export function focusFirstSeedProblem(root: HTMLElement | null, problems: Map<string, string>): boolean {
  const pointer = problems.keys().next().value
  if (root === null || pointer === undefined) {
    return false
  }
  const testId = `studio-field-${pointer}`
  const cell = Array.from(root.querySelectorAll<HTMLElement>('[data-testid^="studio-field-"]')).find(
    (element) => element.getAttribute('data-testid') === testId,
  )
  const input = cell?.querySelector('input')
  input?.focus()
  return input !== null && input !== undefined
}
