import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, test } from 'vitest'
import { cardGroups, cardName } from './cardOrder'
import { fixtureScopes, routes } from './fixtures'
import { previews, stateSlug } from './previews'

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

  test('the card groups are the eight agreed groups and list every preview once', () => {
    expect(cardGroups.map(({ name }) => name)).toEqual([
      'Field inputs',
      'Data widgets',
      'Charts and KPIs',
      'Process widgets',
      'Containers',
      'Shared states',
      'Shell',
      'Studio',
    ])
    const listed = cardGroups.flatMap(({ previews: group }) => group)
    expect(new Set(listed).size).toBe(listed.length)
    expect([...listed].sort()).toEqual(Object.keys(previews).sort())
  })

  test.each([
    [
      'shell',
      ['Application site', 'Navigation collapsed', 'Form page', 'Platform site', 'App switcher', 'Language and theme'],
    ],
    ['signIn', ['Sign-in refused', 'Signed out', 'Session ended']],
    [
      'identitySignIn',
      [
        'Sign in',
        'Wrong password',
        'Organization sign-in only',
        'Two-step verification',
        'Forgot password',
        'Set password',
        'Add an authenticator',
        'Choose how to sign in',
        'Update your profile',
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

  test('the cards have the 26 names of the Design System artifact, in group order', () => {
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
      'Studio',
    ])
  })

  test('the studio preview has its seven states, each with a slug of its own and a frame', () => {
    const states = previews.studio

    expect(states.map(({ caption }) => caption)).toEqual([
      'Typical',
      'Empty',
      'Loading',
      'Failed',
      'Many rows',
      'Long names',
      'Access denied',
    ])
    expect(states.map(({ caption }) => stateSlug(caption))).toEqual([
      'typical',
      'empty',
      'loading',
      'failed',
      'many-rows',
      'long-names',
      'access-denied',
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

  test('the card groups are the groups of the cards table in widget-previews.md', () => {
    // Vitest runs from the web folder, as `npm test --prefix web` does.
    const doc = readFileSync(join(process.cwd(), '../docs/reference/widget-previews.md'), 'utf8')
    const section = doc.split(/^## /m).find((part) => part.startsWith('The cards\n'))
    expect(section).toBeDefined()
    const rows = section!
      .split('\n')
      .filter((line) => line.startsWith('|'))
      .slice(2)
    const documented = rows.map((row) => row.split('|')[1].trim())

    expect(documented).toEqual(cardGroups.map(({ name }) => name))
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
