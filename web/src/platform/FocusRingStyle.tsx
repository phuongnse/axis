import { useDesignTokens } from './themeMode'

/**
 * The one CSS rule of a focus ring that inline styles cannot draw: a `focusRing` outline, 2 px, on what `selector`
 * matches, `offset` px out, or in when negative, such as a tile's keyboard focus or a diff's file block while its diff
 * has focus. See docs/reference/shared-widgets.md#which-widget.
 */
export function FocusRingStyle({ selector, offset }: { selector: string; offset: number }) {
  const { colour } = useDesignTokens()
  return <style>{`${selector} { outline: 2px solid ${colour.focusRing}; outline-offset: ${offset}px; }`}</style>
}
