/**
 * Focuses the first enabled box marked `aria-invalid="true"` in `root`, and scrolls it into view. Returns whether it
 * found one, and leaves the focus where it was when there is none. A screen that holds its fields calls it after a
 * refused action, see docs/reference/actions-and-controls.md#fields.
 */
export function focusFirstError(root: ParentNode | null | undefined): boolean {
  const box = Array.from(root?.querySelectorAll<HTMLElement>('[aria-invalid="true"]') ?? []).find(
    (element) => !element.matches(':disabled'),
  )
  if (box === undefined) return false
  box.scrollIntoView?.({ block: 'center' })
  box.focus({ preventScroll: true })
  return true
}
