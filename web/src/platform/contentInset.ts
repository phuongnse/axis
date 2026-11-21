import { createContext, useContext } from 'react'

/**
 * Proposed, #3264 and #4888: the inline padding of the shell's content area, `spacing-md` beside a claimed sidebar and
 * `spacing-lg` elsewhere, so a rule or a row that runs the whole way reaches the content area's edges, see
 * docs/reference/shell.md#layout. Outside the shell's content area it is 0, so nothing reaches past its box.
 */
export const ContentInsetContext = createContext(0)

/** The content area's inline padding, which a rule that runs the whole way reaches past. */
export function useContentInset(): number {
  return useContext(ContentInsetContext)
}
