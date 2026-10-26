import { describe, expect, test } from 'vitest'
import { fixtureScopes, routes } from './fixtures'
import { previews } from './previews'

describe('design-system previews', () => {
  test.each(Object.entries(previews))('the %s preview has states with unique captions', (_, states) => {
    expect(states.length).toBeGreaterThanOrEqual(1)
    for (const { caption } of states) {
      expect(caption.trim()).not.toBe('')
      expect(caption).toBe(caption.trim())
    }
    expect(new Set(states.map(({ caption }) => caption)).size).toBe(states.length)
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
