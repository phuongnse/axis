/** Which columns move under their row first, see docs/reference/actions-and-controls.md#tables. */
export type ColumnPriority = 'low' | 'high'

/** A shown column as the room measures it: its key, the width it needs in px and its priority. */
export interface RoomColumn {
  key: string
  width: number
  priority?: ColumnPriority
}

const rank = { low: 0, unset: 1, high: 2 }

/**
 * Decides which columns move under their row when a table is too narrow, see
 * docs/reference/actions-and-controls.md#tables. `available` is the table's width and `others` the width of what is
 * not a data column, such as the checkboxes and the row actions. Columns move `low` first, then those without a
 * priority, then `high`, and within each group from the right. The first column never moves. `scrolls` is true when
 * the columns that stay still do not fit. A table with no width yet moves nothing. `moved` is in moving order.
 */
export function makeRoom(
  available: number,
  others: number,
  columns: readonly RoomColumn[],
): { moved: string[]; scrolls: boolean } {
  if (available <= 0) {
    return { moved: [], scrolls: false }
  }
  const order = columns
    .map((column, index) => ({ column, index }))
    .filter(({ index }) => index > 0)
    .sort((a, b) => rank[a.column.priority ?? 'unset'] - rank[b.column.priority ?? 'unset'] || b.index - a.index)
  let needed = others + columns.reduce((sum, column) => sum + column.width, 0)
  const moved: string[] = []
  for (const { column } of order) {
    if (needed <= available) {
      break
    }
    moved.push(column.key)
    needed -= column.width
  }
  return { moved, scrolls: needed > available }
}
