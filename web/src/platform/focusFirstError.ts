/**
 * Proposed, #3264: moves focus to the first box in error inside `root`, as a form does when it refuses to submit, so a
 * person lands on what to fix, see docs/reference/actions-and-controls.md#fields. A box in error is marked
 * `aria-invalid`, as `AxisField`, `AxisFieldGroup` and `LabelTexts` mark it.
 */
export function focusFirstError(root: HTMLElement | null) {
  root?.querySelector<HTMLElement>('input[aria-invalid="true"], textarea[aria-invalid="true"]')?.focus()
}
