import type { CSSProperties } from 'react'
import { spacingTokens } from './designTokens'

/**
 * Proposed, see step 20 of the review on #3263: a list whose items are sentences, free words that may wrap, gives each
 * item a bullet, so where one ends and the next begins shows. An item led by a mark of its own, a link, a tag or a
 * lead, takes none, as the mark does the bullet's work. The list belongs to the line above it, so its bullets sit in
 * from that line's start, near `spacing-md`, where whatever else belongs to it starts, and the words start at
 * `spacing-xl`, a wrapped line under the words and not under the bullet. The bullet is a filled disc at any depth, as a
 * browser draws a list inside another with a circle.
 */
export const bulletList: CSSProperties = { margin: 0, paddingInlineStart: spacingTokens.xl, listStyleType: 'disc' }
