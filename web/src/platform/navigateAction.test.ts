import { describe, expect, it } from 'vitest'
import { navigatePath, navigateValue } from './navigateAction'
import type { NavigateAction } from './site'

const scope = {
  id: 'row-1',
  values: { Title: 'Hello', Count: '3', Done: false, Note: null, id: 'field-id', Items: [{ a: '1' }] },
}

function action(overrides: Partial<NavigateAction> = {}): NavigateAction {
  return {
    type: 'navigate',
    page: 'TicketDetail',
    labelKey: 'x.open',
    parameters: {},
    record: null,
    opensForm: false,
    ...overrides,
  }
}

describe('navigateValue', () => {
  it('matches a field name ignoring case', () => {
    expect(navigateValue('title', scope)).toBe('Hello')
    expect(navigateValue(' TITLE ', scope)).toBe('Hello')
  })

  it('reads id from the record id before a field of that name', () => {
    expect(navigateValue('id', scope)).toBe('row-1')
    expect(navigateValue('ID', scope)).toBe('row-1')
  })

  it('reads literals', () => {
    expect(navigateValue("'it''s'", null)).toBe("it's")
    expect(navigateValue('42', null)).toBe('42')
    expect(navigateValue('-1.5', null)).toBe('-1.5')
    expect(navigateValue('true', null)).toBe(true)
    expect(navigateValue('FALSE', null)).toBe(false)
    expect(navigateValue('null', null)).toBeNull()
  })

  it('returns undefined for what it cannot work out', () => {
    expect(navigateValue('concat(a, b)', scope)).toBeUndefined()
    expect(navigateValue('missing', scope)).toBeUndefined()
    expect(navigateValue('items', scope)).toBeUndefined()
    expect(navigateValue('title', null)).toBeUndefined()
    expect(navigateValue('id', { id: null, values: {} })).toBeUndefined()
  })
})

describe('navigatePath', () => {
  it('puts parameters in the query in declaration order', () => {
    expect(navigatePath('e2e', action({ parameters: { ticket: 'id', title: 'title', flag: 'true' } }), scope)).toBe(
      '/e2e/ticketdetail?ticket=row-1&title=Hello&flag=true',
    )
  })

  it('leaves out a parameter whose value is null', () => {
    expect(navigatePath('e2e', action({ parameters: { note: 'note', ticket: 'id' } }), scope)).toBe(
      '/e2e/ticketdetail?ticket=row-1',
    )
  })

  it('opens the record with record', () => {
    expect(navigatePath('e2e', action({ page: 'NoteForm', opensForm: true, record: 'id' }), scope)).toBe(
      '/e2e/noteform/row-1',
    )
  })

  it('opens a form page without record at /new', () => {
    expect(navigatePath('e2e', action({ page: 'NoteForm', opensForm: true }), null)).toBe('/e2e/noteform/new')
  })

  it('returns null when record is null or not a string', () => {
    expect(navigatePath('e2e', action({ record: 'note' }), scope)).toBeNull()
    expect(navigatePath('e2e', action({ record: 'done' }), scope)).toBeNull()
    expect(navigatePath('e2e', action({ record: "''" }), scope)).toBeNull()
  })

  it('returns null when an expression is unsupported', () => {
    expect(navigatePath('e2e', action({ parameters: { a: 'concat(x)' } }), scope)).toBeNull()
    expect(navigatePath('e2e', action({ record: 'concat(x)' }), scope)).toBeNull()
    expect(navigatePath('e2e', action({ parameters: { a: 'id' } }), null)).toBeNull()
  })

  it('resolves literals without a scope', () => {
    expect(navigatePath('e2e', action({ parameters: { kind: "'open'" } }), null)).toBe('/e2e/ticketdetail?kind=open')
  })
})
