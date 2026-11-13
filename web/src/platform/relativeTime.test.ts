import { describe, expect, it } from 'vitest'
import { formatTimeAgo } from './relativeTime'

const now = new Date('2026-11-13T12:00:00Z')
const ago = (seconds: number) => new Date(now.getTime() - seconds * 1000)

// Proposed: how long ago a notification came, see the review on #3263.
describe('formatTimeAgo', () => {
  it('says now under a minute and for a time ahead', () => {
    expect(formatTimeAgo(ago(30), 'en', now)).toBe('now')
    expect(formatTimeAgo(ago(-60), 'en', now)).toBe('now')
  })

  it('counts in the largest whole unit reached', () => {
    expect(formatTimeAgo(ago(5 * 60), 'en', now)).toBe('5 minutes ago')
    expect(formatTimeAgo(ago(60 * 60), 'en', now)).toBe('1 hour ago')
    expect(formatTimeAgo(ago(24 * 60 * 60), 'en', now)).toBe('yesterday')
    expect(formatTimeAgo(ago(3 * 24 * 60 * 60), 'en', now)).toBe('3 days ago')
    expect(formatTimeAgo(ago(14 * 24 * 60 * 60), 'en', now)).toBe('2 weeks ago')
    expect(formatTimeAgo(ago(31 * 24 * 60 * 60), 'en', now)).toBe('last month')
    expect(formatTimeAgo(ago(400 * 24 * 60 * 60), 'en', now)).toBe('last year')
  })

  it("speaks the reader's locale", () => {
    expect(formatTimeAgo(ago(60 * 60), 'vi', now)).toBe('1 giờ trước')
  })
})
