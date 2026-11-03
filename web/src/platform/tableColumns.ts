import type { CSSProperties } from 'react'
import { sizeTokens, spacingTokens } from './designTokens'

/**
 * Proposed: every row of a table is 48 px high, whatever it holds, so a row with an icon button is as high as one of
 * text. A row of one line is centred in it, as spacing-sm above and below a 24 px line of text or small icon button; a row
 * of more lines grows and starts on the same first line, so its cells read from the top.
 */
const rowLine: CSSProperties = {
  height: sizeTokens.bar,
  boxSizing: 'border-box',
  paddingBlock: spacingTokens.sm,
  lineHeight: `${sizeTokens.smallControl}px`,
  verticalAlign: 'top',
}

/**
 * Proposed: a column whose values have a fixed form, such as a number, a date, a date and time, a status tag or the
 * row's icon actions, is as wide as its content and never wraps, so the columns of free text take the rest of the row.
 * It keeps spacing-lg on each side, so columns fitted to their content do not crowd each other.
 */
const fitted: CSSProperties = { width: '1%', whiteSpace: 'nowrap', paddingInline: spacingTokens.lg }
export const fitContent = {
  onHeaderCell: () => ({ style: fitted }),
  onCell: () => ({ style: { ...fitted, ...rowLine } }),
}

/**
 * Free text wraps at 320 px (D96), so one long value does not widen its column past it. Proposed: a column of free text
 * is as wide as its content up to that, so a short value such as a person's name leaves no empty width. Its `render`
 * wraps the value in `textCell`. A table narrower than its columns' content scrolls sideways, as
 * `scroll={{ x: 'max-content' }}`, rather than squeezing them.
 */
export const freeText = {
  // Its title never wraps, and it keeps spacing-lg on each side, as a column fitted to its content does.
  onHeaderCell: () => ({ style: fitted }),
  onCell: () => ({ style: { width: '1%', paddingInline: spacingTokens.lg, ...rowLine } as CSSProperties }),
}

/**
 * Proposed: the one column of free text that takes the width the other columns leave, the longest and the one most
 * worth reading, such as a release's note. Its text still wraps at 320 px. A table has one such column, else the
 * columns of free text would share the width left over and each show empty space.
 */
export const fillText = {
  onHeaderCell: () => ({ style: { paddingInline: spacingTokens.lg } }),
  onCell: () => ({ style: { paddingInline: spacingTokens.lg, ...rowLine } as CSSProperties }),
}

/** The value of a free text cell: as wide as its content, up to 320 px, where it wraps. */
export const textCell: CSSProperties = { width: 'max-content', maxWidth: 320, overflowWrap: 'anywhere' }
