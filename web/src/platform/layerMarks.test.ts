import { describe, expect, it } from 'vitest'
import { entityKeyedLists, layerMarks, markOf, pageListKey } from './layerMarks'

const content = {
  name: 'Department',
  fields: [{ name: 'title' }, { name: 'costCentre' }],
  validations: [{ name: 'positive' }],
  indexes: [{ name: 'byTitle' }],
}

describe('layerMarks', () => {
  it('maps element pointers to their list and the name of the element', () => {
    const elements = layerMarks(
      content,
      [
        { path: '/fields/1', mark: 'added' },
        { path: '/validations/0', mark: 'changed' },
        { path: '/indexes/0', mark: 'added' },
      ],
      [],
      entityKeyedLists,
    )

    expect(elements).toEqual([
      { list: '/fields', key: 'costCentre', mark: 'added' },
      { list: '/validations', key: 'positive', mark: 'changed' },
      { list: '/indexes', key: 'byTitle', mark: 'added' },
    ])
  })

  it('ignores property pointers and root pointers', () => {
    const elements = layerMarks(
      content,
      [
        { path: '/fields/1/label', mark: 'changed' },
        { path: '/displayField', mark: 'changed' },
        { path: '/fields', mark: 'changed' },
        { path: '', mark: 'changed' },
      ],
      [],
      entityKeyedLists,
    )

    expect(elements).toEqual([])
  })

  it('ignores a list that is not keyed and an element without a text key', () => {
    const elements = layerMarks(
      { ...content, other: [{ name: 'x' }], fields: [{ label: 'no name' }] },
      [
        { path: '/other/0', mark: 'added' },
        { path: '/fields/0', mark: 'added' },
        { path: '/fields/7', mark: 'added' },
      ],
      [{ list: '/other', key: 'x', content: {} }],
      entityKeyedLists,
    )

    expect(elements).toEqual([])
  })

  it('passes the removed elements as removed marks', () => {
    const elements = layerMarks(
      content,
      [],
      [{ list: '/fields', key: 'code', content: { name: 'code' } }],
      entityKeyedLists,
    )

    expect(elements).toEqual([{ list: '/fields', key: 'code', mark: 'removed' }])
  })
})

describe('layerMarks for a page', () => {
  const page = {
    widgets: [
      {
        type: 'columns',
        name: 'grid',
        columns: [
          { name: 'left', widgets: [] },
          {
            name: 'right',
            widgets: [{ type: 'table', name: 'list', rowActions: [{ name: 'open' }], measures: [{ name: 'm' }] }],
          },
        ],
      },
    ],
  }

  it('keys the page lists at any depth by name', () => {
    const elements = layerMarks(
      page,
      [
        { path: '/widgets/0', mark: 'changed' },
        { path: '/widgets/0/columns/1', mark: 'changed' },
        { path: '/widgets/0/columns/1/widgets/0', mark: 'changed' },
        { path: '/widgets/0/columns/1/widgets/0/rowActions/0', mark: 'added' },
        { path: '/widgets/0/columns/1/widgets/0/measures/0', mark: 'added' },
      ],
      [{ list: '/widgets/0/columns/1/widgets', key: 'old', content: { name: 'old' } }],
      pageListKey,
    )

    expect(elements).toEqual([
      { list: '/widgets', key: 'grid', mark: 'changed' },
      { list: '/widgets/0/columns', key: 'right', mark: 'changed' },
      { list: '/widgets/0/columns/1/widgets', key: 'list', mark: 'changed' },
      { list: '/widgets/0/columns/1/widgets/0/rowActions', key: 'open', mark: 'added' },
      { list: '/widgets/0/columns/1/widgets', key: 'old', mark: 'removed' },
    ])
  })
})

describe('pageListKey', () => {
  it('keys the lists of widgets, actions, columns and tabs by name', () => {
    for (const list of ['widgets', 'actions', 'rowActions', 'toolbarActions', 'columns', 'tabs']) {
      expect(pageListKey(`/widgets/0/${list}`)).toBe('name')
    }
    expect(pageListKey('/widgets')).toBe('name')
    expect(pageListKey('/widgets/0/columns/1/widgets')).toBe('name')
  })

  it('leaves the other lists out', () => {
    expect(pageListKey('/widgets/0/measures')).toBeUndefined()
    expect(pageListKey('/parameters')).toBeUndefined()
    expect(pageListKey('/widgets/0')).toBeUndefined()
  })
})

describe('markOf', () => {
  it('finds the mark by list and key', () => {
    const elements = [{ list: '/fields', key: 'title', mark: 'added' as const }]

    expect(markOf(elements, '/fields', 'title')).toBe('added')
    expect(markOf(elements, '/indexes', 'title')).toBeUndefined()
    expect(markOf(elements, '/fields', 'other')).toBeUndefined()
  })
})
