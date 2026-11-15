import { describe, expect, it } from 'vitest'
import { slotTitle } from './studioCatalog'

describe('slotTitle', () => {
  const slot = { name: 'approver', titles: { en: 'Approver', vi: 'Người duyệt' } }

  it('follows the one language rule: the locale, else en, else the name', () => {
    expect(slotTitle(slot, 'vi')).toBe('Người duyệt')
    expect(slotTitle(slot, 'fr')).toBe('Approver')
    expect(slotTitle({ ...slot, titles: {} }, 'en')).toBe('approver')
  })

  it('counts a blank title as missing', () => {
    expect(slotTitle({ ...slot, titles: { en: 'Approver', vi: '' } }, 'vi')).toBe('Approver')
  })
})
