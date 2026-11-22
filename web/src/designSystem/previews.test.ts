import { describe, expect, test } from 'vitest'
import { docsTables } from '../test/docTables'
import { cardGroups, cardName } from './cardOrder'
import { fixtureScopes, routes } from './fixtures'
import { previews, stateSlug } from './previews'

function hasEmptyState(name: string): boolean {
  return ['taskInbox', 'detail', 'kpi'].includes(name) || name.startsWith('chart-')
}

/** The header of the cards table in docs/reference/widget-previews.md. */
const cardsHeader = /^\|\s*Group\s*\|\s*Previews\s*\|\s*States\s*\|/

/** A row of the cards table: its group, the previews its Previews cell names and the captions its States cell quotes. */
interface CardRow {
  group: string
  previews: string[]
  captions: string[]
  /** Whether the States cell links the doc that holds the captions, as the Shell group's cell does. */
  linked: boolean
}

/** Reads the rows of the cards table: a preview is backticked, a caption is in straight double quotes. */
function cardRows(rows: string[]): CardRow[] {
  return rows.map((row) => {
    const [, group, previewCell, stateCell] = row.split('|')
    return {
      group: group.trim(),
      previews: [...previewCell.matchAll(/`([^`]+)`/g)].map(([, name]) => name),
      captions: [...stateCell.matchAll(/"([^"]+)"/g)].map(([, caption]) => caption),
      linked: /\]\([^)]+\.md#[^)]+\)/.test(stateCell),
    }
  })
}

/** The `group: preview` entries of the rows, one for each preview a row names. */
function listedEntries(rows: CardRow[]): string[] {
  return rows.flatMap(({ group, previews: names }) => names.map((name) => `${group}: ${name}`))
}

/** The `group: preview` entries of the cards, one for each preview a group holds. */
const codeEntries = cardGroups.flatMap(({ name, previews: names }) => names.map((preview) => `${name}: ${preview}`))

/** The entries that only the doc lists, that only the code lists, and that the doc lists twice. */
function entryDifferences(doc: string[], code: string[]): string[] {
  return [
    ...doc.filter((entry) => !code.includes(entry)).map((entry) => `${entry} is in the doc only`),
    ...code.filter((entry) => !doc.includes(entry)).map((entry) => `${entry} is in the code only`),
    ...doc.filter((entry, index) => doc.indexOf(entry) !== index).map((entry) => `${entry} is listed twice in the doc`),
  ].sort()
}

/** The captions that only the doc quotes or only the code defines, or else that the two list in another order. */
function captionDifferences(preview: string, doc: string[], code: string[]): string[] {
  if (doc.length === code.length && doc.every((caption, index) => caption === code[index])) return []
  const problems = [
    ...doc.filter((caption) => !code.includes(caption)).map((caption) => `${preview}: "${caption}" is in the doc only`),
    ...code
      .filter((caption) => !doc.includes(caption))
      .map((caption) => `${preview}: "${caption}" is in the code only`),
  ]
  return problems.length > 0 ? problems : [`${preview}: the doc and the code differ in order or in repeats`]
}

/** The rows whose States cell has neither a quoted caption nor a link to the doc that holds the captions. */
function unstatedRows(rows: CardRow[]): string[] {
  return rows
    .filter(({ captions, linked }) => captions.length === 0 && !linked)
    .map(({ group, previews: names }) => `${group}: ${names.join(', ')}`)
}

/** The captions of a preview in previews.tsx, in order. */
function codeCaptions(preview: string): string[] {
  return Object.hasOwn(previews, preview) ? previews[preview].map(({ caption }) => caption) : []
}

describe('design-system previews', () => {
  test.each(Object.entries(previews))('the %s preview has states with unique captions', (_, states) => {
    expect(states.length).toBeGreaterThanOrEqual(1)
    for (const { caption } of states) {
      expect(caption.trim()).not.toBe('')
      expect(caption).toBe(caption.trim())
    }
    expect(new Set(states.map(({ caption }) => caption)).size).toBe(states.length)
  })

  test.each(Object.entries(previews).filter(([name]) => hasEmptyState(name)))(
    'the %s preview draws its data and its empty state',
    (_, states) => {
      expect(states.map(({ caption }) => caption)).toEqual(['With data', 'Empty'])
    },
  )

  test('the table preview draws its data, its empty state and a row with many actions', () => {
    expect(previews.table.map(({ caption }) => caption)).toEqual(['With data', 'Empty', 'Many row actions'])
  })

  test('the form preview has its five states', () => {
    expect(previews.form.map(({ caption }) => caption)).toEqual([
      'Validation errors',
      'Read-only by a condition',
      'Wizard steps',
      'Fields in columns, with errors',
      'Many steps',
    ])
  })

  test.each(
    Object.entries(previews).filter(([name]) => name.startsWith('field-') && name !== 'field-child-collection'),
  )('the %s preview has the three states of a field', (_, states) => {
    expect(states.map(({ caption }) => caption)).toEqual(['Editable', 'Read-only', 'Required, help text and error'])
  })

  test('the card groups are the nine agreed groups and list every preview once', () => {
    expect(cardGroups.map(({ name }) => name)).toEqual([
      'Field inputs',
      'Data widgets',
      'Charts and KPIs',
      'Process widgets',
      'Containers',
      'Shared states',
      'Shell',
      'Studio',
      'Sign-up',
    ])
    const listed = cardGroups.flatMap(({ previews: group }) => group)
    expect(new Set(listed).size).toBe(listed.length)
    expect([...listed].sort()).toEqual(Object.keys(previews).sort())
  })

  test.each([
    [
      'shell',
      [
        'Application site',
        'Navigation collapsed',
        'Form page',
        'Default record page',
        'Saved record',
        'Saved record with a change',
        'Record not found',
        'Task',
        'Task not found',
        'Dashboard',
        'Process instance',
        'Platform site',
        'App switcher',
        'Language and theme',
        'Account page',
        'Account notifications',
        'Users page',
        'User page',
      ],
    ],
    ['signIn', ['Sign-in refused', 'Signed out', 'Session ended']],
    [
      'identitySignIn',
      [
        'Sign in',
        'Wrong password',
        'Fields left empty',
        'Organization sign-in only',
        'Two-step verification',
        'Forgot password',
        'Set password',
        'Add an authenticator',
        'Choose how to sign in',
        'Update your profile',
        'Terms and privacy',
        'Terms not accepted',
        'Sign out',
        'Notice',
        'Error',
        'Language and theme',
      ],
    ],
  ])('the %s preview has its states, each in a frame of its own height', (name, captions) => {
    const states = previews[name]
    expect(states.map(({ caption }) => caption)).toEqual(captions)
    for (const { frameHeight } of states) {
      expect(frameHeight).toBeGreaterThan(0)
    }
  })

  test('every shell state draws in an 800px frame', () => {
    for (const state of previews.shell) {
      expect(state.frameHeight, state.caption).toBe(800)
    }
  })

  test('the cards have the 31 names of the Design System artifact, in group order', () => {
    expect(cardGroups.flatMap(({ previews: group }) => group).map(cardName)).toEqual([
      'TextField',
      'IntegerField',
      'DecimalField',
      'BooleanField',
      'DateField',
      'DateTimeField',
      'EnumField',
      'ReferenceField',
      'ChildCollectionField',
      'Table',
      'Form',
      'Detail',
      'ChartBar',
      'ChartLine',
      'ChartPie',
      'Kpi',
      'TaskInbox',
      'ProcessInspector',
      'Columns',
      'Tabs',
      'Section',
      'States',
      'StatusTag',
      'Shell',
      'SignIn',
      'IdentitySignIn',
      'Studio',
      'StudioPageDesign',
      'StudioFormDesign',
      'StudioProcessDesign',
      'SignUp',
    ])
  })

  test('each Studio designer card has one Typical state over the fixtures of the studio preview', () => {
    for (const name of ['studioPageDesign', 'studioFormDesign', 'studioProcessDesign']) {
      const states = previews[name]
      expect(states.map(({ caption }) => caption)).toEqual(['Typical'])
      expect(states[0].fixtures).toBe(previews.studio[0].fixtures)
      expect(states[0].entry).toContain('&resource=')
      expect(states[0].frameHeight).toBeGreaterThan(0)
    }
  })

  test('the studio preview has its sixteen states, each with a slug of its own and a frame', () => {
    const states = previews.studio

    expect(states.map(({ caption }) => caption)).toEqual([
      'Typical',
      'Empty',
      'Loading',
      'Failed',
      'Many rows',
      'Long names',
      'Access denied',
      'Overview with errors and changes not live',
      'Overview of a new application',
      'Entity',
      'Entity with expressions',
      'Policy matrix',
      'Releases',
      'History',
      'Revision',
      'Release page',
    ])
    expect(states.map(({ caption }) => stateSlug(caption))).toEqual([
      'typical',
      'empty',
      'loading',
      'failed',
      'many-rows',
      'long-names',
      'access-denied',
      'overview-with-errors-and-changes-not-live',
      'overview-of-a-new-application',
      'entity',
      'entity-with-expressions',
      'policy-matrix',
      'releases',
      'history',
      'revision',
      'release-page',
    ])
    for (const { frameHeight, fixtures } of states) {
      expect(frameHeight).toBeGreaterThan(0)
      expect(Object.keys(fixtures ?? {})).toContain('/api/studio/apps')
    }
  })

  test("a state's shared fixture paths are scoped and in no route of the shared routes or of a state", () => {
    const stateOwn = Object.values(previews).flatMap((states) =>
      states.flatMap((state) => Object.keys(state.routes ?? {})),
    )
    const paths = Object.values(previews).flatMap((states) =>
      states.flatMap((state) => Object.keys(state.fixtures ?? {})),
    )

    expect(paths.length).toBeGreaterThan(0)
    for (const path of paths) {
      expect(Object.keys(routes)).not.toContain(path)
      expect(stateOwn).not.toContain(path)
      expect(fixtureScopes.some((scope) => path.startsWith(scope))).toBe(true)
    }
  })

  describe('the cards table in widget-previews.md', () => {
    const tables = docsTables(cardsHeader)
    const rows = cardRows(tables.flat())

    test('is the one table with the cards header', () => {
      expect(tables).toHaveLength(1)
    })

    test('lists the groups of cardOrder.ts, in order', () => {
      expect([...new Set(rows.map(({ group }) => group))]).toEqual(cardGroups.map(({ name }) => name))
    })

    test('lists each preview of each group, and no other, as cardOrder.ts does', () => {
      expect(entryDifferences(listedEntries(rows), codeEntries)).toEqual([])
    })

    test('quotes the states of each row, or links the doc that holds them', () => {
      expect(unstatedRows(rows)).toEqual([])
    })

    test('lists the states of each preview that previews.tsx defines, in order', () => {
      const problems = rows
        .filter(({ captions }) => captions.length > 0)
        .flatMap(({ previews: names, captions }) =>
          names.flatMap((name) => captionDifferences(name, captions, codeCaptions(name))),
        )
      expect(problems).toEqual([])
    })
  })

  describe('the cards table reader and its comparisons', () => {
    test('reads the previews, the quoted captions and a link of each row', () => {
      expect(
        cardRows([
          '| Field inputs | `field-text`, `field-integer` | "Editable", "Required, help text and error" | Inputs. |',
          '| Shell | `shell` | see [shell](shell.md#design-system-previews) | Frames. |',
        ]),
      ).toEqual([
        {
          group: 'Field inputs',
          previews: ['field-text', 'field-integer'],
          captions: ['Editable', 'Required, help text and error'],
          linked: false,
        },
        { group: 'Shell', previews: ['shell'], captions: [], linked: true },
      ])
    })

    test('reports a preview that only the doc names, and one that only the code has', () => {
      expect(
        entryDifferences(
          ['Field inputs: field-text', 'Field inputs: field-gone'],
          ['Field inputs: field-text', 'Field inputs: field-new'],
        ),
      ).toEqual(['Field inputs: field-gone is in the doc only', 'Field inputs: field-new is in the code only'])
    })

    test('reports a preview that the doc lists twice', () => {
      expect(entryDifferences(['Data widgets: table', 'Data widgets: table'], ['Data widgets: table'])).toEqual([
        'Data widgets: table is listed twice in the doc',
      ])
    })

    test('reports a caption that only the doc quotes, and one that only the code defines', () => {
      expect(captionDifferences('field-text', ['Editable', 'Read only'], ['Editable', 'Read-only'])).toEqual([
        'field-text: "Read only" is in the doc only',
        'field-text: "Read-only" is in the code only',
      ])
    })

    test('reports a preview whose captions are in another order', () => {
      expect(captionDifferences('table', ['With data', 'Empty'], ['Empty', 'With data'])).toEqual([
        'table: the doc and the code differ in order or in repeats',
      ])
    })

    test('accepts a States cell that only links the doc', () => {
      expect(
        unstatedRows(cardRows(['| Shell | `shell` | see [shell](shell.md#design-system-previews) | Frames. |'])),
      ).toEqual([])
    })

    test('reports a States cell with neither quoted captions nor a link', () => {
      expect(
        unstatedRows(cardRows(['| Shell | `shell` | Application site, Navigation collapsed | Frames. |'])),
      ).toEqual(['Shell: shell'])
    })
  })

  test("a state's own fixture paths are scoped and repeat no shared path or other state's path", () => {
    const paths = Object.values(previews).flatMap((states) =>
      states.flatMap((state) => Object.keys(state.routes ?? {})),
    )

    expect(paths.length).toBeGreaterThan(0)
    expect(new Set(paths).size).toBe(paths.length)
    for (const path of paths) {
      expect(Object.keys(routes)).not.toContain(path)
      expect(fixtureScopes.some((scope) => path.startsWith(scope))).toBe(true)
    }
  })
})
