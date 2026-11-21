import type { ColourName } from './designTokens'

/** The five tones of a status, which say what the status asks of the reader. See docs/reference/shared-widgets.md#which-widget. */
export type StatusTone = 'neutral' | 'attention' | 'success' | 'muted' | 'danger'

/**
 * Each tone's icon colour, for the state icon of a step, see
 * docs/reference/process-inspector-widget.md#process-graph. A tag's colours come from `useStatusTints`.
 */
export const toneIconColours: Record<StatusTone, ColourName> = {
  neutral: 'infoText',
  attention: 'warningText',
  success: 'successText',
  danger: 'dangerText',
  muted: 'textMuted',
}
