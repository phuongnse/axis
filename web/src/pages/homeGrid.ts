import type { CSSProperties } from 'react'
import { useDesignTokens } from '../platform/themeMode'
import { useNarrowWidth } from '../platform/useNarrowWidth'

/** The home grid's columns: as many as tiles, up to three, so no code counts the tiles. */
export function homeGridColumns(gap: number): string {
  return `repeat(auto-fit, minmax(max(0px, calc((100% - ${2 * gap}px) / 3)), 1fr))`
}

/** The style of a grid of tiles: one column on a narrow screen, up to three otherwise. */
export function useHomeGrid(): CSSProperties {
  const narrow = useNarrowWidth()
  const { spacing } = useDesignTokens()
  return {
    display: 'grid',
    gridTemplateColumns: narrow ? '1fr' : homeGridColumns(spacing.md),
    gridAutoRows: '1fr',
    gap: spacing.md,
  }
}
