import { colourTokens } from './designTokens'
import { useDesignTokens } from './themeMode'

/** The mark of a site or an application: the first letter of its title on a `primary` tile. */
export function SiteMark({ title, size = 28 }: { title: string; size?: number }) {
  const { colour, radius } = useDesignTokens()
  return (
    <span
      aria-hidden
      style={{
        display: 'grid',
        placeItems: 'center',
        flex: 'none',
        width: size,
        height: size,
        background: colour.primary,
        // White in both modes, as the tile is the same purple family in both.
        color: colourTokens.surface.light,
        fontWeight: 600,
        borderRadius: radius.base,
      }}
    >
      {Array.from(title)[0]?.toUpperCase()}
    </span>
  )
}
