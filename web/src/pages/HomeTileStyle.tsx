import { useDesignTokens } from '../platform/themeMode'

/** The keyboard focus ring of a link tile. Inline styles cannot target focus. */
export function HomeTileStyle() {
  const { colour } = useDesignTokens()
  return (
    <style>{`[data-home-tile]:focus-visible { outline: 2px solid ${colour.focusRing}; outline-offset: 2px; }`}</style>
  )
}
