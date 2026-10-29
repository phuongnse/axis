import { describe, expect, test } from 'vitest'
import { cardGroups, cardName } from './cardOrder'
import { fixtureScopes, routes } from './fixtures'
import { previews } from './previews'

function hasEmptyState(name: string): boolean {
  return ['taskInbox', 'detail', 'kpi'].includes(name) || name.startsWith('chart-')
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

  test('the card groups are the seven agreed groups and list every preview once', () => {
    expect(cardGroups.map(({ name }) => name)).toEqual([
      'Field inputs',
      'Data widgets',
      'Charts and KPIs',
      'Process widgets',
      'Containers',
      'Shared states',
      'Shell',
    ])
    const listed = cardGroups.flatMap(({ previews: group }) => group)
    expect(new Set(listed).size).toBe(listed.length)
    expect([...listed].sort()).toEqual(Object.keys(previews).sort())
  })

  test.each([
    ['shell', ['Application site', 'Navigation collapsed', 'Form page', 'Platform site']],
    ['signIn', ['Signed out', 'Sign-in refused', 'Session ended']],
    [
      'identitySignIn',
      ['Sign in', 'Wrong password', 'Organization sign-in only', 'Two-step verification', 'Add an authenticator'],
    ],
  ])('the %s preview has its states, each in a frame of its own height', (name, captions) => {
    const states = previews[name]
    expect(states.map(({ caption }) => caption)).toEqual(captions)
    for (const { frameHeight } of states) {
      expect(frameHeight).toBeGreaterThan(0)
    }
  })

  test('the cards have the 25 names of the Design System artifact, in group order', () => {
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
      'Shell',
      'SignIn',
      'IdentitySignIn',
    ])
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
