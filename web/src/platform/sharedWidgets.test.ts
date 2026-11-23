// Holds the table in docs/reference/shared-widgets.md#which-widget to the SPA's exports.
import { describe, expect, it } from 'vitest'
import { docsTables, tablesIn } from '../test/docTables'

/** The header of the table that names the shared widget of each job. */
const tableHeader = /^\|\s*Job\s*\|\s*Shared widget\s*\|/
/** A backticked identifier, as a row names a widget, a helper or a prop. */
const identifier = /`([A-Za-z]\w*)`/g
/** A name that Ant Design's own API has, such as Ant Design's `Button`. */
const antDesignName = /Ant Design's `\w+`/g
/** A row with this marker names work that the code does not have yet: a planned marker with a milestone, an issue number or both, see docs/writing-docs.md#planned-work. */
const plannedMarker = /_\(planned(?: for [^)#]+?)?(?:, #\d+)?\)_/
/** A row that names a source path is about the server, which the SPA does not hold. */
const serverPath = /`src\/Axis\./
/** An export of a function, a constant, a let, a class or the default. */
const exportPattern = /^export (?:default )?(?:async )?(?:function\*?|const|let|class) (\w+)/gm

type Documented = { all: Set<string>; required: Set<string> }

/** The names that the rows name: all of them, and those the SPA must export. */
function documentedNames(rows: string[]): Documented {
  const all = new Set<string>()
  const required = new Set<string>()
  for (const row of rows) {
    const cell = row.split('|')[2]
    for (const [, name] of cell.matchAll(identifier)) all.add(name)
    if (plannedMarker.test(cell) || serverPath.test(cell)) continue
    for (const [, name] of cell.replace(antDesignName, '').matchAll(identifier)) {
      if (/[A-Z]/.test(name)) required.add(name)
    }
  }
  return { all, required }
}

/** The names that the sources export. */
function exportedNames(sources: Record<string, string>): Set<string> {
  const names = new Set<string>()
  for (const text of Object.values(sources)) {
    for (const [, name] of text.matchAll(exportPattern)) names.add(name)
  }
  return names
}

/** The names the table names but the code does not export, and the Axis widgets the code exports but the table misses. */
function compare(documented: Documented, exported: Set<string>) {
  const notExported = [...documented.required].filter((name) => !exported.has(name)).sort()
  const notDocumented = [...exported].filter((name) => /^Axis[A-Z]\w*$/.test(name) && !documented.all.has(name)).sort()
  return { notExported, notDocumented }
}

describe('the Which widget table', () => {
  it('finds the table under its header', () => {
    const doc = [
      '# Shared widgets',
      '',
      '| Job | Shared widget |',
      '| --- | --- |',
      '| A button | `AxisButton` |',
      '',
      'Text.',
    ]
    expect(tablesIn(doc.join('\n'), tableHeader)).toEqual([['| A button | `AxisButton` |']])
  })

  it('names the widgets of a row without a planned marker as required', () => {
    const { all, required } = documentedNames([
      "| A thing | `KeptThing`, never Ant Design's `Button` |",
      '| A planned thing | `PlannedThing` _(planned, #12)_ |',
      '| A milestone thing | `MilestoneThing` _(planned for Mx, #12)_ |',
    ])
    expect(required).toEqual(new Set(['KeptThing']))
    expect(all).toEqual(new Set(['KeptThing', 'Button', 'PlannedThing', 'MilestoneThing']))
  })

  it("does not require a lowercase prop, a name after Ant Design's or a server row", () => {
    const { required } = documentedNames([
      "| A field | `KeptThing` with a `role`, a `small` size and Ant Design's `Table` |",
      '| A list | `ListQuery` in `src/Axis.Server/Http/`, which checks `page` with `RecordListFilter` |',
    ])
    expect(required).toEqual(new Set(['KeptThing']))
  })

  it('reports a name in the table that the code does not export', () => {
    const documented = documentedNames(['| A thing | `Missing` |', '| A widget | `AxisKept` |'])
    expect(compare(documented, new Set(['AxisKept']))).toEqual({ notExported: ['Missing'], notDocumented: [] })
  })

  it('reports an Axis widget that the code exports and the table does not name', () => {
    const documented = documentedNames(['| A widget | `AxisKept` |'])
    expect(compare(documented, new Set(['AxisKept', 'AxisNew', 'helper']))).toEqual({
      notExported: [],
      notDocumented: ['AxisNew'],
    })
  })

  it('reads the exports of a function, a default function, a constant and a generic function', () => {
    const names = exportedNames({
      'a.tsx': 'export default function Editor() {}',
      'b.ts': 'export const y = 1',
      'c.ts': 'export function Z<T>(value: T) {}',
      'd.ts': 'const hidden = 1\nexport async function later() {}',
    })
    expect(names).toEqual(new Set(['Editor', 'y', 'Z', 'later']))
  })
})

describe('the Which widget table against the SPA', () => {
  const sources = import.meta.glob(['../**/*.{ts,tsx}', '!../**/*.test.{ts,tsx}', '!../test/**'], {
    query: '?raw',
    import: 'default',
    eager: true,
  }) as Record<string, string>

  const tables = docsTables(tableHeader)

  it('finds the sources', () => {
    expect(Object.keys(sources).length).toBeGreaterThan(50)
  })

  it('finds one table', () => {
    expect(tables).toHaveLength(1)
  })

  it('requires the widgets and helpers that the table names without a planned marker', () => {
    const { required } = documentedNames(tables[0])
    expect([...required]).toEqual(expect.arrayContaining(['EmptyDash', 'StepsBar', 'placeProblemErrors', 'AxisButton']))
  })

  it('exports each name that a row without a planned marker names', () => {
    const { notExported } = compare(documentedNames(tables[0]), exportedNames(sources))
    expect(notExported).toEqual([])
  })

  it('names in the table each Axis widget that the SPA exports', () => {
    const { notDocumented } = compare(documentedNames(tables[0]), exportedNames(sources))
    expect(notDocumented).toEqual([])
  })
})
