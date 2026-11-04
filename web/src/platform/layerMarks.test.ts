import { describe, expect, it } from 'vitest'
import {
  dataSourceKeyedLists,
  entityKeyedLists,
  formKeyedLists,
  layerMarks,
  markOf,
  pageListKey,
  textKeyedMaps,
} from './layerMarks'

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

describe('layerMarks of a data source', () => {
  it('keys a plain-string groupBy element by its value and an object one by its field', () => {
    const dataSource = {
      fields: [{ name: 'total' }],
      aggregate: { groupBy: ['status', { field: 'owner' }] },
    }

    const elements = layerMarks(
      dataSource,
      [
        { path: '/fields/0', mark: 'changed' },
        { path: '/aggregate/groupBy/0', mark: 'added' },
        { path: '/aggregate/groupBy/1', mark: 'changed' },
      ],
      [],
      dataSourceKeyedLists,
    )

    expect(elements).toEqual([
      { list: '/fields', key: 'total', mark: 'changed' },
      { list: '/aggregate/groupBy', key: 'status', mark: 'added' },
      { list: '/aggregate/groupBy', key: 'owner', mark: 'changed' },
    ])
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

describe('layerMarks of a text map', () => {
  const texts = { texts: { 'a.label': 'Label', 'a/b~c': 'Slash', 'a.new': 'New' } }

  it('maps a mark on /texts/<key> to that key, unescaping ~1 and ~0', () => {
    const elements = layerMarks(
      texts,
      [
        { path: '/texts/a.label', mark: 'changed' },
        { path: '/texts/a~1b~0c', mark: 'changed' },
        { path: '/texts/a.new', mark: 'added' },
      ],
      [],
      {},
      textKeyedMaps,
    )

    expect(elements).toEqual([
      { list: '/texts', key: 'a.label', mark: 'changed' },
      { list: '/texts', key: 'a/b~c', mark: 'changed' },
      { list: '/texts', key: 'a.new', mark: 'added' },
    ])
  })

  it('ignores a key the content lacks and a mark under a map that was not given', () => {
    const marks = [{ path: '/texts/a.label', mark: 'changed' as const }]

    expect(layerMarks(texts, [{ path: '/texts/gone', mark: 'added' }], [], {}, textKeyedMaps)).toEqual([])
    expect(layerMarks(texts, marks, [], {})).toEqual([])
  })
})

describe('layerMarks of a form', () => {
  const form = {
    steps: [{ name: 'first', sections: ['basics'] }],
    sections: [
      { name: 'basics', fields: [{ field: 'name' }, { field: 'amount' }] },
      { name: 'extra', fields: [{ field: 'note' }] },
    ],
  }

  it('lists the sections, the steps and the fields of each section', () => {
    expect(formKeyedLists(form)).toEqual({
      '/sections': 'name',
      '/steps': 'name',
      '/sections/0/fields': 'field',
      '/sections/1/fields': 'field',
    })
    expect(formKeyedLists(undefined)).toEqual({ '/sections': 'name', '/steps': 'name' })
  })

  it('turns marks and removed elements into section, step and section-field elements', () => {
    const elements = layerMarks(
      form,
      [
        { path: '/sections/1', mark: 'added' },
        { path: '/steps/0', mark: 'changed' },
        { path: '/sections/0/fields/1', mark: 'changed' },
        { path: '/sections/1/fields/0', mark: 'added' },
      ],
      [
        { list: '/sections/0/fields', key: 'year', content: { field: 'year' } },
        { list: '/steps', key: 'last', content: { name: 'last' } },
      ],
      formKeyedLists(form),
    )

    expect(elements).toEqual([
      { list: '/sections', key: 'extra', mark: 'added' },
      { list: '/steps', key: 'first', mark: 'changed' },
      { list: '/sections/0/fields', key: 'amount', mark: 'changed' },
      { list: '/sections/1/fields', key: 'note', mark: 'added' },
      { list: '/sections/0/fields', key: 'year', mark: 'removed' },
      { list: '/steps', key: 'last', mark: 'removed' },
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
