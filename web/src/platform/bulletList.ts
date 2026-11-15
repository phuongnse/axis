import type { CSSProperties } from 'react'
import { useDesignTokens } from './themeMode'

/**
 * Proposed, see step 20 of the review on #3263: a list whose items are sentences, free words that may wrap, gives each
 * item a bullet, so where one ends and the next begins shows, and so it reads as a list. An item led by a mark of its
 * own, such as a problem's severity or a review's icon, keeps its bullet, and the mark follows it. The list belongs to the line above it, so its bullets sit in
 * from that line's start, near `spacing-md`, where whatever else belongs to it starts, and the words start at
 * `spacing-xl`, a wrapped line under the words and not under the bullet. The bullet is a filled disc at any depth, as a
 * browser draws a list inside another with a circle, in the `text` colour, so it shows on every surface in either mode.
 */
export function useBulletList(): CSSProperties {
  const { colour, spacing } = useDesignTokens()
  return { margin: 0, paddingInlineStart: spacing.xl, listStyleType: 'disc', color: colour.text }
}
