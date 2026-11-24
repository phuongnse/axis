import { Menu, type MenuProps } from 'antd'
import { render } from '@testing-library/react'
import type { ReactNode } from 'react'
import { MemoryRouter } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import { menuOf, type AxisAction } from './menuActions'

const text = (key: string) => `text:${key}`

const action = (key: string, extra: Partial<AxisAction> = {}): AxisAction =>
  ({
    key,
    labelKey: `test.${key}`,
    onClick: vi.fn(),
    ...extra,
  }) as AxisAction

type Items = NonNullable<MenuProps['items']>

/** The keys of `items` in their order, with a divider as `---` and a group as `[group]` with its children. */
function shapeOf(items: Items): unknown[] {
  return items.map((item) => {
    if (item?.type === 'divider') return '---'
    if (item?.type === 'group') return { group: String(item.key), children: shapeOf(item.children ?? []) }
    return String(item?.key)
  })
}

describe('menuOf', () => {
  it('puts destructive actions last, behind one divider, and keeps the others in order', () => {
    const { items } = menuOf(
      [action('a'), action('del1', { destructive: true }), action('b'), action('del2', { destructive: true })],
      text,
    )

    expect(shapeOf(items!)).toEqual(['a', 'b', '---', 'del1', 'del2'])
    expect(items!.find((item) => item?.key === 'del1')).toMatchObject({ danger: true })
  })

  it('keeps the order and adds no divider when no action is destructive', () => {
    const { items } = menuOf([action('a'), action('b'), action('c')], text)

    expect(shapeOf(items!)).toEqual(['a', 'b', 'c'])
  })

  it('keeps the order and adds no divider when every action is destructive', () => {
    const { items } = menuOf([action('del1', { destructive: true }), action('del2', { destructive: true })], text)

    expect(shapeOf(items!)).toEqual(['del1', 'del2'])
  })

  it('draws the text of a key, and the author’s own text when it has no key', () => {
    const { items } = menuOf([action('a'), { key: 'b', label: 'Site title', onClick: vi.fn() }], text)

    expect(items!.map((item) => (item as { label: unknown }).label)).toEqual(['text:test.a', 'Site title'])
  })

  it('lists the choices first, grouped under their headings with no divider between the groups', () => {
    const { items } = menuOf(
      [
        action('save'),
        action('en', { selected: true, groupKey: 'group.language' }),
        action('vi', { selected: false, groupKey: 'group.language' }),
        action('light', { selected: false, groupKey: 'group.theme' }),
        action('dark', { selected: true, groupKey: 'group.theme' }),
      ],
      text,
    )

    expect(shapeOf(items!)).toEqual([
      { group: 'group.language', children: ['en', 'vi'] },
      { group: 'group.theme', children: ['light', 'dark'] },
      '---',
      'save',
    ])
    expect(items!.find((item) => item?.key === 'group.language')).toMatchObject({ label: 'text:group.language' })
  })

  it('puts actions after the choices behind one divider, and a destructive action last behind another', () => {
    const { items } = menuOf(
      [
        action('del', { destructive: true }),
        action('open'),
        action('current', { selected: true }),
        action('other', { selected: false }),
      ],
      text,
    )

    expect(shapeOf(items!)).toEqual(['current', 'other', '---', 'open', '---', 'del'])
  })

  it('draws a choice with no address as a checked radio entry', () => {
    const { items } = menuOf([action('en', { selected: true }), action('vi', { selected: false })], text)

    expect(items![0]).toMatchObject({ role: 'menuitemradio', 'aria-checked': true })
    expect(items![1]).toMatchObject({ role: 'menuitemradio', 'aria-checked': false })
  })

  it('draws an action with no choice as a plain entry', () => {
    const { items } = menuOf([action('open')], text)

    expect(items![0]).not.toHaveProperty('role')
  })

  it('marks the chosen keys as selected and selects only when there is a choice', () => {
    expect(
      menuOf([action('a', { selected: true }), action('b', { selected: false }), action('c')], text),
    ).toMatchObject({ selectable: true, selectedKeys: ['a'] })
    expect(menuOf([action('c')], text)).toMatchObject({ selectable: false, selectedKeys: [] })
  })

  it('draws a choice with an address as a router link marked as the current page when it is chosen', async () => {
    const { items } = menuOf(
      [action('account', { href: '/account', selected: true }), action('help', { href: '/help', selected: false })],
      text,
    )
    const { getByRole, queryByRole } = render(
      <MemoryRouter>
        <ul>
          {items!.map((item) => (
            <li key={String(item?.key)}>{(item as { label: ReactNode }).label}</li>
          ))}
        </ul>
      </MemoryRouter>,
    )

    expect(getByRole('link', { name: 'text:test.account' })).toHaveAttribute('href', '/account')
    expect(getByRole('link', { name: 'text:test.account' })).toHaveAttribute('aria-current', 'page')
    expect(getByRole('link', { name: 'text:test.help' })).not.toHaveAttribute('aria-current')
    expect(queryByRole('menuitemradio')).toBeNull()
  })

  it('draws an address that is not a path as a plain link', () => {
    const { items } = menuOf([action('billing', { href: 'https://billing.example', selected: false })], text)
    const { getByRole } = render(<>{(items![0] as { label: ReactNode }).label}</>)

    expect(getByRole('link', { name: 'text:test.billing' })).toHaveAttribute('href', 'https://billing.example')
  })

  it('gives an entry the attributes of its action, such as its test id, and keeps its own label', () => {
    const { items } = menuOf(
      [action('a', { attributes: { 'data-testid': 'entry-a', label: 'Other' } }), action('b')],
      text,
    )
    const { getByTestId } = render(<Menu items={items} />)

    expect(getByTestId('entry-a')).toHaveTextContent('text:test.a')
    expect(getByTestId('entry-a')).not.toHaveTextContent('Other')
  })
})
