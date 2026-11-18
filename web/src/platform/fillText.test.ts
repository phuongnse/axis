import { describe, expect, it } from 'vitest'
import { fillText } from './fillText'

describe('fillText', () => {
  it('fills each placeholder in the word order of an en text', () => {
    expect(fillText('Added step {name} after {after}.', { name: 'remind', after: 'resolve' })).toBe(
      'Added step remind after resolve.',
    )
  })

  it('fills each placeholder in the word order of a vi text', () => {
    expect(fillText('Đã thêm bước {name} sau {after}.', { name: 'remind', after: 'resolve' })).toBe(
      'Đã thêm bước remind sau resolve.',
    )
  })

  it('keeps a placeholder with no value as written', () => {
    expect(fillText('Hello {who}, see {unknown}.', { who: 'Ann' })).toBe('Hello Ann, see {unknown}.')
  })

  it('does not fill a placeholder again inside a value', () => {
    expect(fillText('{name} after {after}', { name: '{after}', after: 'resolve' })).toBe('{after} after resolve')
  })

  it('fills each place of a repeated placeholder', () => {
    expect(fillText('{x} and {x}', { x: 'a' })).toBe('a and a')
  })

  it('fills a number as its digits', () => {
    expect(fillText('Branch {number}', { number: 2 })).toBe('Branch 2')
  })

  it('does not read an inherited name as a value', () => {
    expect(fillText('{toString}', {})).toBe('{toString}')
  })
})
