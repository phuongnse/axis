import { colourTokens } from './designTokens'
import { useDesignTokens } from './themeMode'

/**
 * Proposed: a person's avatar: the first letters of the first and last words of their name on a `primary` circle. The
 * circle tells a person from an application or a site, whose marks are rounded squares.
 */
export function PersonAvatar({ name, size = 24 }: { name: string; size?: number }) {
  const { colour } = useDesignTokens()
  const words = name.split(/\s+/).filter(Boolean)
  const initials = (words.length > 1 ? [words[0], words[words.length - 1]] : words)
    .map((word) => Array.from(word)[0]?.toUpperCase())
    .join('')
  return (
    <span
      aria-hidden
      style={{
        display: 'grid',
        placeItems: 'center',
        flex: 'none',
        width: size,
        height: size,
        borderRadius: '50%',
        background: colour.primary,
        // White in both modes, as the circle is the same purple family in both.
        color: colourTokens.surface.light,
        fontSize: Math.round(size * 0.42),
        fontWeight: 600,
      }}
    >
      {initials}
    </span>
  )
}
