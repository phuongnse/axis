import type { MenuProps } from 'antd'
import { describe, expect, it, vi } from 'vitest'
import { menuOf, type MenuAction } from './menuActions'

const action = (key: string, destructive = false): MenuAction => ({ key, label: key, destructive, onClick: vi.fn() })

type Items = NonNullable<MenuProps['items']>

/** The keys of `items` in their order, with a divider as `---`. */
const keysOf = (items: Items) => items.map((item) => (item?.type === 'divider' ? '---' : String(item?.key)))

describe('menuOf', () => {
  it('puts destructive actions last, behind one divider, and keeps the others in order', () => {
    const items = menuOf([action('a'), action('del1', true), action('b'), action('del2', true)])

    expect(keysOf(items)).toEqual(['a', 'b', '---', 'del1', 'del2'])
    expect(items.filter((item) => item?.type === 'divider')).toHaveLength(1)
    expect(items.find((item) => item?.key === 'del1')).toMatchObject({ danger: true })
    expect(items.find((item) => item?.key === 'del2')).toMatchObject({ danger: true })
  })

  it('keeps the order and adds no divider when no action is destructive', () => {
    const items = menuOf([action('a'), action('b'), action('c')])

    expect(keysOf(items)).toEqual(['a', 'b', 'c'])
  })

  it('keeps the order and adds no divider when every action is destructive', () => {
    const items = menuOf([action('del1', true), action('del2', true)])

    expect(keysOf(items)).toEqual(['del1', 'del2'])
  })
})
