import { describe, expect, it, vi } from 'vitest'
import { placeProblemErrors } from './problemErrors'

const roleOnly = (segments: string[]) => (segments.join('/') === 'role' ? 'role' : null)

describe('placeProblemErrors', () => {
  it('sends a key that names a field to that field and any other key to the form', () => {
    const placed = placeProblemErrors({ '/role': ['Not a role.'], '/other': ['Not known.'] }, roleOnly)

    expect(placed).toEqual({ fields: [{ field: 'role', messages: ['Not a role.'] }], form: ['Not known.'] })
  })

  it('decodes the pointer segments before it asks for the field', () => {
    const fieldOf = vi.fn(() => 'output')

    placeProblemErrors({ '/output/a~1b': ['Not declared.'] }, fieldOf)

    expect(fieldOf).toHaveBeenCalledWith(['output', 'a/b'])
  })

  it('sends a pointer that the field function does not name to the form', () => {
    expect(placeProblemErrors({ '/role/extra': ['Too deep.'] }, roleOnly)).toEqual({
      fields: [],
      form: ['Too deep.'],
    })
  })

  it('sends the root key and a key that is no pointer to the form without asking for a field', () => {
    const fieldOf = vi.fn(() => 'role')

    const placed = placeProblemErrors({ '': ['Choose one.'], bad: ['Malformed.'] }, fieldOf)

    expect(placed).toEqual({ fields: [], form: ['Choose one.', 'Malformed.'] })
    expect(fieldOf).not.toHaveBeenCalled()
  })

  it('keeps the messages of each key in key order', () => {
    const placed = placeProblemErrors({ '/x': ['First.'], '/role': ['Second.'], '/y': ['Third.'] }, roleOnly)

    expect(placed.fields).toEqual([{ field: 'role', messages: ['Second.'] }])
    expect(placed.form).toEqual(['First.', 'Third.'])
  })
})
