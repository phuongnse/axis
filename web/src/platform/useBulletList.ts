import { useMemo, type CSSProperties } from 'react'
import { useDesignTokens } from './themeMode'

/**
 * The style of every bulleted list, whose items are sentences: a filled bullet at any depth, in the `text` colour, in
 * `spacing-xl` from the start of the line above, so the words start there and a wrapped line sits under the words. Give
 * `list` to the list and `item` to each of its items, see docs/reference/actions-and-controls.md#layout-shows-structure. A
 * list of rows that are not sentences draws no bullet and does not use it.
 */
export function useBulletList(): { list: CSSProperties; item: CSSProperties } {
  const { colour, spacing } = useDesignTokens()
  return useMemo(
    () => ({
      list: { margin: 0, paddingInlineStart: spacing.xl, listStyleType: 'disc', listStylePosition: 'outside' },
      item: { color: colour.text },
    }),
    [colour.text, spacing.xl],
  )
}
