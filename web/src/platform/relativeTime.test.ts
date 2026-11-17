import { describe, expect, it } from 'vitest'
import { formatTimeUntil } from './relativeTime'

describe('formatTimeUntil', () => {
  const now = new Date('2026-03-12T09:00:00Z')

  it('says how far ahead a time is, in its largest whole unit', () => {
    expect(formatTimeUntil('2026-04-08T15:07:00Z', 'en', now)).toBe('in 3 weeks')
    expect(formatTimeUntil('2026-03-12T12:30:00Z', 'en', now)).toBe('in 3 hours')
    expect(formatTimeUntil('2026-03-12T09:00:30Z', 'en', now)).toBe('in 1 minute')
  })

  it('gives null once the time has passed', () => {
    expect(formatTimeUntil('2026-03-12T08:59:00Z', 'en', now)).toBeNull()
    expect(formatTimeUntil(now, 'en', now)).toBeNull()
  })
})
