// The tint and the marks of a removed or an added line, see docs/reference/process-inspector-widget.md#instance-page and docs/reference/ai-authoring-in-studio.md#revisions.
import type { DesignTokens } from './designTokens'

/** A line removed from the text before, or added in the text after. */
export type LineChange = 'removed' | 'added'

/** The tint of a state colour, which the design tokens give as a seed, as a fill under a line of text. */
export function changeTint(seed: string): string {
  return `color-mix(in srgb, ${seed} 16%, transparent)`
}

/** A removed or an added line's mark, the mark's colour and the line's tint. */
export function changeLook(
  change: LineChange,
  colour: DesignTokens['colour'],
): { mark: string; markColour: string; tint: string } {
  return change === 'removed'
    ? { mark: '−', markColour: colour.dangerText, tint: changeTint(colour.danger) }
    : { mark: '+', markColour: colour.successText, tint: changeTint(colour.success) }
}
