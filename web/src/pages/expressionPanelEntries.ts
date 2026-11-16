export type EntryKind = 'field' | 'operator' | 'function'

/** One thing the panel lists: what a click inserts, and the words and example that say what it is. */
export interface PanelEntry {
  kind: EntryKind
  name: string
  insert: string
  cursorInside: boolean
  description: string
  example?: string
}

/** A call's signature as the panel and completion show it: the name and its parameter names in brackets. */
export function signatureOf(name: string, parameters: readonly string[]): string {
  return `${name}(${parameters.join(', ')})`
}

/** A name without its brackets and quotes, in lower case: `date('…')` is `date` and `toDate()` is `todate`. */
function bareName(name: string): string {
  return name
    .replace(/\(.*\)/, '')
    .replaceAll("'", '')
    .trim()
    .toLowerCase()
}

/**
 * The entries that match the words, best first, see [the expression editor](docs/reference/studio.md#expression-editor):
 * a name that is the words, then one that starts with them, then one that holds them, then one whose description does.
 * Entries of the same rank keep the order they came in. Case and the spaces around the words do not matter.
 */
export function rankEntries(entries: readonly PanelEntry[], words: string): PanelEntry[] {
  const needle = words.trim().toLowerCase()
  const ranked: { entry: PanelEntry; rank: number; index: number }[] = []
  entries.forEach((entry, index) => {
    const name = bareName(entry.name)
    let rank: number
    if (name === needle) {
      rank = 0
    } else if (name.startsWith(needle)) {
      rank = 1
    } else if (name.includes(needle)) {
      rank = 2
    } else if (entry.description.toLowerCase().includes(needle)) {
      rank = 3
    } else {
      return
    }
    ranked.push({ entry, rank, index })
  })
  return ranked.sort((a, b) => a.rank - b.rank || a.index - b.index).map(({ entry }) => entry)
}
