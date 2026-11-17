/**
 * Proposed, see the review on #3650: what went wrong in a step run or a call's attempt, as a code the engine stores and
 * the values that fill its words, such as `timeout` with `seconds`. The reader sees it in their language through the
 * `shell.stepProblem.<code>` text. The values are names, kinds and numbers, never a field's value, as the stored error
 * is, see docs/reference/process-inspection.md#step-errors-in-inspection.
 */
export interface StepProblem {
  code: string
  values: Record<string, string>
}

/**
 * The words of a problem in the reader's language, or the stored error as it is when there is no problem or its code
 * has no text yet, or `null` when there is neither.
 */
export function problemText(
  t: (key: string) => string,
  problem: StepProblem | null | undefined,
  stored: string | null,
): string | null {
  if (problem) {
    const key = `shell.stepProblem.${problem.code}`
    const text = t(key)
    if (text !== key) {
      return Object.entries(problem.values).reduce(
        (filled, [name, value]) => filled.replaceAll(`{${name}}`, value),
        text,
      )
    }
  }
  return stored
}
