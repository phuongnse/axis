import { describe, expect, test } from 'vitest'
import { fixtureScopes, routes } from './fixtures'
import { previews } from './previews'

function hasEmptyState(name: string): boolean {
  return ['table', 'taskInbox', 'detail', 'kpi'].includes(name) || name.startsWith('chart-')
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

  test.each([
    ['shell', ['Application site', 'Navigation collapsed', 'Form page', 'Platform site']],
    ['signIn', ['Signed out', 'Sign-in refused', 'Session ended']],
    ['identitySignIn', ['Sign in', 'Wrong password', 'Organization sign-in only', 'Two-step verification']],
  ])('the %s preview has its states, each in a frame of its own height', (name, captions) => {
    const states = previews[name]
    expect(states.map(({ caption }) => caption)).toEqual(captions)
    for (const { frameHeight } of states) {
      expect(frameHeight).toBeGreaterThan(0)
    }
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
