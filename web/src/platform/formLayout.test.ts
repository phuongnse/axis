import { describe, expect, it } from 'vitest'
import {
  addSection,
  addStep,
  dropPlace,
  insertionPlace,
  moveField,
  movePlace,
  outline,
  paletteFields,
} from './formLayout'
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

describe('movePlace', () => {
  it('moves onto a field to its place and to the end of a section', () => {
    expect(movePlace(draft, '/sections/0/fields/1')).toEqual({ section: 0, index: 1 })
    expect(movePlace(draft, '/sections/1')).toEqual({ section: 1, index: 1 })
  })

  it('moves nowhere else', () => {
    expect(movePlace(draft, '/steps/0')).toBeUndefined()
    expect(movePlace(draft, '/sections/9')).toBeUndefined()
    expect(movePlace(draft, '/sections/9/fields/0')).toBeUndefined()
  })
})

describe('moveField', () => {
  const names = (sections: unknown[]) =>
    sections.map((section) => (section as { fields: { field: string }[] }).fields.map((f) => f.field))

  it('moves a field within its section to the place of the field it is dropped on', () => {
    const result = moveField(draft, { section: 0, index: 0 }, { section: 0, index: 1 })
    expect(names(result?.sections ?? [])).toEqual([['status', 'title'], ['notes'], []])
    expect(result?.place).toEqual({ section: 0, index: 1 })
    expect(names(moveField(draft, { section: 0, index: 1 }, { section: 0, index: 0 })?.sections ?? [])).toEqual([
      ['status', 'title'],
      ['notes'],
      [],
    ])
  })

  it('moves a field before the field it is dropped on in another section', () => {
    const result = moveField(draft, { section: 0, index: 1 }, { section: 1, index: 0 })
    expect(names(result?.sections ?? [])).toEqual([['title'], ['status', 'notes'], []])
    expect(result?.place).toEqual({ section: 1, index: 0 })
  })

  it('clamps the place to the end of the target section, which an empty section takes', () => {
    expect(moveField(draft, { section: 0, index: 0 }, { section: 1, index: 9 })?.place).toEqual({
      section: 1,
      index: 1,
    })
    const result = moveField(draft, { section: 0, index: 0 }, { section: 2, index: 0 })
    expect(names(result?.sections ?? [])).toEqual([['status'], ['notes'], ['title']])
  })

  it('keeps all keys of the moved field and the sections that do not change', () => {
    const rich = {
      sections: [
        { name: 'a', fields: [{ field: 'x', readOnly: true, label: { textKey: 'k' } }] },
        { name: 'b', fields: [] },
        { name: 'c', fields: [{ field: 'y' }] },
      ],
    }
    const result = moveField(rich, { section: 0, index: 0 }, { section: 1, index: 0 })
    expect(result?.sections[1]).toEqual({
      name: 'b',
      fields: [{ field: 'x', readOnly: true, label: { textKey: 'k' } }],
    })
    expect(result?.sections[0]).toEqual({ name: 'a', fields: [] })
    expect(result?.sections[2]).toBe(rich.sections[2])
  })

  it('moves nothing for a missing field or section, or a move to the current place', () => {
    expect(moveField(draft, { section: 0, index: 5 }, { section: 1, index: 0 })).toBeUndefined()
    expect(moveField(draft, { section: 9, index: 0 }, { section: 1, index: 0 })).toBeUndefined()
    expect(moveField(draft, { section: 0, index: 0 }, { section: 9, index: 0 })).toBeUndefined()
    expect(moveField(draft, { section: 0, index: 0 }, { section: 0, index: 0 })).toBeUndefined()
    expect(moveField(draft, { section: 0, index: 1 }, { section: 0, index: 9 })).toBeUndefined()
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

describe('addSection', () => {
  const keys = { file: 'forms/ticket.json', name: 'Ticket', texts: new Set<string>() }
  const flat = {
    sections: [
      { name: 'Section1', fields: [{ field: 'title' }] },
      { name: 'details', title: { textKey: 'forms.ticket.sections.1.title' }, fields: [] },
    ],
  }
  const stepped = {
    sections: [
      { name: 'a', fields: [{ field: 'title' }] },
      { name: 'b', fields: [] },
      { name: 'c', fields: [] },
    ],
    steps: [
      { title: { textKey: 's.one' }, sections: ['a'] },
      { title: { textKey: 's.two' }, sections: ['B'] },
      { title: { textKey: 's.three' }, sections: ['c'] },
    ],
  }

  it('appends an empty section with the lowest unused name, ignoring letter case', () => {
    const add = addSection(flat, null, keys)
    expect(add.sections).toHaveLength(3)
    expect(add.sections[2]).toEqual({
      name: 'section2',
      title: { textKey: 'forms.ticket.sections.2.title' },
      fields: [],
    })
    expect(add.steps).toBeUndefined()
    expect(add.select).toBe('/sections/2')
    expect(add.name).toBe('section2')
    expect(flat.sections).toHaveLength(2)
  })

  it('gives a title key that no text and no title of the draft owns', () => {
    const taken = { ...keys, texts: new Set(['forms.ticket.sections.2.title']) }
    expect((addSection(flat, null, taken).sections[2] as { title: unknown }).title).toEqual({
      textKey: 'forms.ticket.sections.2.title2',
    })
    const used = { sections: [{ name: 'a', title: { textKey: 'forms.ticket.sections.1.title' }, fields: [] }] }
    expect((addSection(used, null, keys).sections[1] as { title: unknown }).title).toEqual({
      textKey: 'forms.ticket.sections.1.title2',
    })
  })

  it('joins the selected step, the step of the selected section or field, or the last step', () => {
    expect(
      addSection(stepped, '/steps/0', keys).steps?.map((step) => (step as { sections: string[] }).sections),
    ).toEqual([['a', 'section1'], ['B'], ['c']])
    expect(
      addSection(stepped, '/sections/1', keys).steps?.map((step) => (step as { sections: string[] }).sections),
    ).toEqual([['a'], ['B', 'section1'], ['c']])
    expect(
      addSection(stepped, '/sections/0/fields/0', keys).steps?.map((step) => (step as { sections: string[] }).sections),
    ).toEqual([['a', 'section1'], ['B'], ['c']])
    expect(addSection(stepped, null, keys).steps?.map((step) => (step as { sections: string[] }).sections)).toEqual([
      ['a'],
      ['B'],
      ['c', 'section1'],
    ])
    expect(stepped.steps[0].sections).toEqual(['a'])
  })
})

describe('addStep', () => {
  const keys = { file: 'forms/ticket.json', name: 'Ticket', texts: new Set<string>() }

  it('names every unnamed section and lists all sections in the first step', () => {
    const add = addStep(
      { sections: [{ fields: [] }, { name: 'Section1', fields: [] }, { name: 'details', fields: [] }, { fields: [] }] },
      keys,
    )
    expect((add.sections as { name: string }[]).map((section) => section.name)).toEqual([
      'section2',
      'Section1',
      'details',
      'section3',
    ])
    expect(add.steps).toEqual([
      { title: { textKey: 'forms.ticket.steps.0.title' }, sections: ['section2', 'Section1', 'details', 'section3'] },
    ])
    expect(add.select).toBe('/steps/0')
  })

  it('adds an empty step to a form that has steps and selects it', () => {
    const add = addStep(
      {
        sections: [{ name: 'a', fields: [] }],
        steps: [{ title: { textKey: 'forms.ticket.steps.0.title' }, sections: ['a'] }],
      },
      keys,
    )
    expect(add.sections).toEqual([{ name: 'a', fields: [] }])
    expect(add.steps?.[1]).toEqual({ title: { textKey: 'forms.ticket.steps.1.title' }, sections: [] })
    expect(add.select).toBe('/steps/1')
  })
})
