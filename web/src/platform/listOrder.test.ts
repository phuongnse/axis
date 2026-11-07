import { describe, expect, it } from 'vitest'
import { nameCompare, sortByName } from './listOrder'

describe('sortByName', () => {
  it('sorts Vietnamese names with Đ after D and leaves the input unchanged', () => {
    const names = ['Đơn hàng', 'Dự án', 'Bảng']

    expect(sortByName(names, (name) => name, 'vi')).toEqual(['Bảng', 'Dự án', 'Đơn hàng'])
    expect(names).toEqual(['Đơn hàng', 'Dự án', 'Bảng'])
  })

  it('puts Dz before Đa in Vietnamese', () => {
    expect(sortByName(['Đa', 'Dz'], (name) => name, 'vi')).toEqual(['Dz', 'Đa'])
  })

  it('groups by rank first and sorts by name within each group', () => {
    const items = [
      { name: 'b', rank: 1 },
      { name: 'z', rank: 0 },
      { name: 'a', rank: 1 },
      { name: 'c', rank: 0 },
    ]

    const sorted = sortByName(
      items,
      (item) => item.name,
      'en',
      (item) => item.rank,
    )

    expect(sorted.map((item) => item.name)).toEqual(['c', 'z', 'a', 'b'])
  })
})

describe('nameCompare', () => {
  it('orders two names in the locale', () => {
    expect(nameCompare('vi')('Dự án', 'Đơn hàng')).toBeLessThan(0)
    expect(nameCompare('vi')('Đơn hàng', 'Dự án')).toBeGreaterThan(0)
    expect(nameCompare('en')('a', 'a')).toBe(0)
  })
})
