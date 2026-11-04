import { describe, expect, it } from 'vitest'
import { entityKeyedLists, layerMarks, markOf } from './layerMarks'

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

describe('markOf', () => {
  it('finds the mark by list and key', () => {
    const elements = [{ list: '/fields', key: 'title', mark: 'added' as const }]

    expect(markOf(elements, '/fields', 'title')).toBe('added')
    expect(markOf(elements, '/indexes', 'title')).toBeUndefined()
    expect(markOf(elements, '/fields', 'other')).toBeUndefined()
  })
})
