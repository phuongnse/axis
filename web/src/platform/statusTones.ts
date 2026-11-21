import type { ColourName } from './designTokens'

/** The five tones of a status, which say what the status asks of the reader. See docs/reference/shared-widgets.md#which-widget. */
export type StatusTone = 'neutral' | 'attention' | 'success' | 'muted' | 'danger'

/**
 * Each tone's text colour, as a token. A tag's fill and border are Ant Design's tints, which `statusTints` in
 * antTheme.ts reads.
 */
export const toneColours: Record<StatusTone, { text: ColourName }> = {
  neutral: { text: 'text' },
  attention: { text: 'warningText' },
  success: { text: 'successText' },
  muted: { text: 'textMuted' },
  danger: { text: 'dangerText' },
}

/**
 * Each tone's icon colour, for the state icon of a step, see
 * docs/reference/process-inspector-widget.md#process-graph. A tag keeps its own colours above.
 */
export const toneIconColours: Record<StatusTone, ColourName> = {
  neutral: 'infoText',
  attention: 'warningText',
  success: 'successText',
  danger: 'dangerText',
  muted: 'textMuted',
}
