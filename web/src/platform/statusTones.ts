import type { ColourName } from './designTokens'

/** The five tones of a status, which say what the status asks of the reader. See docs/reference/shared-widgets.md#which-widget. */
export type StatusTone = 'neutral' | 'attention' | 'success' | 'muted' | 'danger'

/** Each tone's text colour and border colour, as tokens. A tag is outlined on a transparent background. */
export const toneColours: Record<StatusTone, { text: ColourName; border: ColourName }> = {
  neutral: { text: 'text', border: 'border' },
  attention: { text: 'warningText', border: 'warningText' },
  success: { text: 'successText', border: 'successText' },
  muted: { text: 'textMuted', border: 'borderSubtle' },
  danger: { text: 'dangerText', border: 'dangerText' },
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
