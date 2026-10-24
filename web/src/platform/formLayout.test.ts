import { describe, expect, it } from 'vitest'
import { dropPlace, insertionPlace, outline, paletteFields } from './formLayout'
import type { StudioNames } from './studio'

const draft = {
  entity: 'Ticket',
  sections: [
    { name: 'Problem', fields: [{ field: 'title' }, { field: 'status' }] },
    { name: 'details', fields: [{ field: 'notes' }] },
    { fields: [] },
  ],
  steps: [
    { title: { textKey: 'steps.one' }, sections: ['DETAILS'] },
    { title: { textKey: 'steps.two' }, sections: ['problem', 'missing'] },
  ],
}

describe('insertionPlace', () => {
  it('puts a field after the selected field', () => {
    expect(insertionPlace(draft, '/sections/0/fields/0')).toEqual({ section: 0, index: 1 })
  })

  it('puts a field at the end of the selected section', () => {
    expect(insertionPlace(draft, '/sections/1')).toEqual({ section: 1, index: 1 })
  })

  it('puts a field at the end of the first section of the selected step, ignoring case', () => {
    expect(insertionPlace(draft, '/steps/0')).toEqual({ section: 1, index: 1 })
    expect(insertionPlace(draft, '/steps/1')).toEqual({ section: 0, index: 2 })
  })

  it('falls back to the end of the first section', () => {
    expect(insertionPlace(draft, null)).toEqual({ section: 0, index: 2 })
    expect(insertionPlace(draft, '/sections/9')).toEqual({ section: 0, index: 2 })
    expect(insertionPlace(draft, '/steps/9')).toEqual({ section: 0, index: 2 })
  })

  it('finds no place in a draft without sections', () => {
    expect(insertionPlace({ entity: 'Ticket' }, null)).toBeUndefined()
  })
})

describe('dropPlace', () => {
  it('drops after a field and at the end of a section', () => {
    expect(dropPlace(draft, '/sections/0/fields/0')).toEqual({ section: 0, index: 1 })
    expect(dropPlace(draft, '/sections/0')).toEqual({ section: 0, index: 2 })
  })

  it('drops nowhere else', () => {
    expect(dropPlace(draft, '/steps/0')).toBeUndefined()
    expect(dropPlace(draft, '/sections/9')).toBeUndefined()
  })
})

describe('outline', () => {
  it('lists the steps with their sections, then the sections no step lists', () => {
    expect(outline(draft)).toEqual([
      { step: { pointer: '/steps/0', titleKey: 'steps.one' }, sections: [1] },
      { step: { pointer: '/steps/1', titleKey: 'steps.two' }, sections: [0] },
      { step: null, sections: [2] },
    ])
  })

  it('puts all sections in one group without steps', () => {
    expect(outline({ sections: [{}, {}] })).toEqual([{ step: null, sections: [0, 1] }])
  })
})

describe('paletteFields', () => {
  it('lists nothing without names or a known entity', () => {
    const names = { entities: [] } as unknown as StudioNames
    expect(paletteFields(draft, undefined)).toEqual([])
    expect(paletteFields(draft, names)).toEqual([])
  })
})
