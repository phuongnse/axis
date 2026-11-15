import type { CSSProperties } from 'react'
import { spacingTokens } from './designTokens'

/**
 * Proposed, see step 20 of the review on #3263: a list whose items are sentences, free words that may wrap, gives each
 * item a bullet, so where one ends and the next begins shows. An item led by a mark of its own, a link, a tag or a
 * lead, takes none, as the mark does the bullet's work. The bullet sits in the indent and the words start at
 * `spacing-md`, where whatever belongs to the line above starts, so a list and the text beside it line up. The bullet is
 * a filled disc at any depth, as a browser draws a list inside another with a circle.
 */
export const bulletList: CSSProperties = { margin: 0, paddingInlineStart: spacingTokens.md, listStyleType: 'disc' }
