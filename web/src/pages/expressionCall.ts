import type { ExpressionScopeFunction } from '../platform/studio'

/** The name before an open bracket: a function, or a rule as `module:rule`. */
const nameBeforeBracket = /([A-Za-z][A-Za-z0-9-]*:)?[A-Za-z][A-Za-z0-9]*\s*$/

/**
 * The innermost call around the end of `before` to one of `functions`, and the index of the argument there, counted by
 * the commas at the call's own level, see [the expression editor](docs/reference/studio.md#expression-editor). Null
 * outside such a call. A plain bracket, an `in (…)` list or a rule call is not a function, so the call around it wins.
 */
export function callAt(
  before: string,
  functions: ExpressionScopeFunction[],
): { fn: ExpressionScopeFunction; argument: number } | null {
  const frames: { name: string | null; commas: number }[] = []
  for (let i = 0; i < before.length; i++) {
    const char = before[i]
    if (char === "'") {
      // Quoted text ends at a quote that is not followed by another, which `''` escapes.
      for (i++; i < before.length; i++) {
        if (before[i] === "'") {
          if (before[i + 1] !== "'") {
            break
          }
          i++
        }
      }
    } else if (char === '(') {
      const name = nameBeforeBracket.exec(before.slice(0, i))?.[0].trim() ?? null
      frames.push({ name, commas: 0 })
    } else if (char === ')') {
      frames.pop()
    } else if (char === ',' && frames.length > 0) {
      frames[frames.length - 1].commas++
    }
  }
  for (let i = frames.length - 1; i >= 0; i--) {
    const name = frames[i].name?.toLowerCase()
    const fn = functions.find((item) => item.name.toLowerCase() === name)
    if (fn) {
      return { fn, argument: frames[i].commas }
    }
  }
  return null
}
