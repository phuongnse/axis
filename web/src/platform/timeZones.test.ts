import { describe, expect, it } from 'vitest'
import { browserTimeZone, timeZoneText } from './timeZones'

describe('timeZoneText', () => {
  it('shows the offset and the id without underscores', () => {
    expect(timeZoneText({ id: 'Asia/Ho_Chi_Minh', offset: 420 })).toBe('(UTC+07:00) Asia/Ho Chi Minh')
    expect(timeZoneText({ id: 'America/St_Johns', offset: -210 })).toBe('(UTC-03:30) America/St Johns')
    expect(timeZoneText({ id: 'UTC', offset: 0 })).toBe('(UTC+00:00) UTC')
  })
})

describe('browserTimeZone', () => {
  it('returns the zone the browser resolves, with an offset that matches it', () => {
    const zone = browserTimeZone()

    expect(zone.id).toBe(Intl.DateTimeFormat().resolvedOptions().timeZone)
    expect(zone.offset).toBe(0 - new Date().getTimezoneOffset())
  })
})
