import { describe, expect, it } from 'vitest'
import { maskedKey } from './maskedKey'

describe('maskedKey', () => {
  it('shows four dots and the last four characters of the key', () => {
    expect(maskedKey('a1B2')).toBe('••••a1B2')
  })

  it('shows the dots alone when the ending is missing', () => {
    expect(maskedKey(undefined)).toBe('••••')
    expect(maskedKey(null)).toBe('••••')
  })
})
