import { describe, expect, it } from 'vitest'
import { suggestSubdomain } from './suggestSubdomain'

describe('suggestSubdomain', () => {
  it('drops Vietnamese accents and turns đ into d', () => {
    expect(suggestSubdomain('Công ty Đông Á')).toBe('cong-ty-dong-a')
  })

  it('joins symbols into one hyphen and keeps none at the ends', () => {
    expect(suggestSubdomain('  --Acme & Sons, Ltd.!  ')).toBe('acme-sons-ltd')
  })

  it('stops at 40 characters without a hyphen at the end', () => {
    const suggestion = suggestSubdomain('a'.repeat(39) + ' bbbb')
    expect(suggestion).toBe('a'.repeat(39))
    expect(suggestSubdomain('x'.repeat(60))).toHaveLength(40)
  })

  it('suggests nothing for a name with no letter or digit', () => {
    expect(suggestSubdomain('!!!')).toBe('')
  })
})
