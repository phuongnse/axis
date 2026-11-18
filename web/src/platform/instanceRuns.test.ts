import { describe, expect, it } from 'vitest'
import { isoDurationMs, isoDurationText } from './instanceRuns'

describe('isoDurationMs and isoDurationText', () => {
  it('reads a duration of days, hours, minutes and seconds, as a wait gives up after', () => {
    expect(isoDurationMs('P30D')).toBe(30 * 86_400_000)
    expect(isoDurationMs('PT1H30M')).toBe(90 * 60_000)
    expect(isoDurationText('P30D', 'en')).toBe('30 days')
    expect(isoDurationText('PT4H', 'en')).toBe('4 hr')
  })

  it('gives null for anything that is not such a duration', () => {
    expect(isoDurationMs('P1M')).toBeNull()
    expect(isoDurationMs(30)).toBeNull()
    expect(isoDurationText(undefined, 'en')).toBeNull()
  })
})
