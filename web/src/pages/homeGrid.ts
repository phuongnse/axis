/** The home grid's columns: as many as tiles, up to three, so no code counts the tiles. */
export function homeGridColumns(gap: number): string {
  return `repeat(auto-fit, minmax(max(0px, calc((100% - ${2 * gap}px) / 3)), 1fr))`
}
