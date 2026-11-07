import { describe, expect, it } from 'vitest'

// The separator rule is in docs/reference/actions-and-controls.md#text.
describe('separators', () => {
  const sources = import.meta.glob(['../**/*.{ts,tsx,json}', '!../**/*.test.{ts,tsx}', '!../test/**'], {
    query: '?raw',
    import: 'default',
    eager: true,
  }) as Record<string, string>

  it('finds the sources', () => {
    expect(Object.keys(sources).length).toBeGreaterThan(50)
  })

  it('keeps the middle dot out of the sources', () => {
    expect(Object.keys(sources).filter((file) => sources[file].includes('·'))).toEqual([])
  })
})
