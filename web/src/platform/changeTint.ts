/**
 * The tint of a state colour, which the design tokens give as a seed, as a fill under a line of text: a removed line on
 * the `danger` tint and an added one on the `success` tint. A run's changed field and a package file's line diff share
 * it, see docs/reference/shared-widgets.md#which-widget.
 */
export function changeTint(seed: string): string {
  return `color-mix(in srgb, ${seed} 16%, transparent)`
}
