import { useDesignTokens } from './themeMode'

/**
 * The one keyboard focus ring that inline styles cannot draw, such as a `:focus-visible` or a `:has()` ring. It draws a
 * 2px `focusRing` outline 2px outside the element that `selector` matches, or 2px inside it with `inset`, for an element
 * at the edge of a region. The `focusRing` colour is in docs/reference/frontend.md#design-tokens-and-previews, and which
 * widget draws a ring is in docs/reference/shared-widgets.md#which-widget.
 */
export function FocusRingStyle({ selector, inset = false }: { selector: string; inset?: boolean }) {
  const { colour } = useDesignTokens()
  return <style>{`${selector} { outline: 2px solid ${colour.focusRing}; outline-offset: ${inset ? -2 : 2}px; }`}</style>
}
