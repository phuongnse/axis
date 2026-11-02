import type { CSSProperties } from 'react'
import { useDesignTokens } from '../platform/themeMode'
import { useNarrowWidth } from '../platform/useNarrowWidth'

/** The home grid's columns: as many as tiles, up to three, so no code counts the tiles. */
export function homeGridColumns(gap: number): string {
  return `repeat(auto-fit, minmax(max(0px, calc((100% - ${2 * gap}px) / 3)), 1fr))`
}

/** The grid of tiles of the home page, which Studio's Overview uses for an application's live sites too. */
export function useTileGrid(): CSSProperties {
  const narrow = useNarrowWidth()
  const { spacing } = useDesignTokens()
  return {
    display: 'grid',
    gridTemplateColumns: narrow ? '1fr' : homeGridColumns(spacing.md),
    // Proposed: every row is as tall as the tallest, so a tile in one row matches the tiles in the others.
    gridAutoRows: '1fr',
    gap: spacing.md,
  }
}
