import { describe, expect, it } from 'vitest'
import { knownWidgetKeys, typeChanges, widgetKeys, widgetSummary, widgetTypes } from './pageWidgets'

describe('widgetSummary', () => {
  it('names the type and the first name the widget binds', () => {
    expect(widgetSummary({ type: 'table', dataSource: 'PurchaseRequestList' })).toBe('table · PurchaseRequestList')
    expect(widgetSummary({ type: 'form', entity: 'PurchaseRequest' })).toBe('form · PurchaseRequest')
    expect(widgetSummary({ type: 'form', form: 'RequestForm' })).toBe('form · RequestForm')
    expect(widgetSummary({ type: 'processInspector', process: 'Approval' })).toBe('processInspector · Approval')
  })

  it('shows only the type when nothing is bound', () => {
    expect(widgetSummary({ type: 'taskInbox' })).toBe('taskInbox')
    expect(widgetSummary({ type: 'table', entity: '' })).toBe('table')
  })

  it('does not fail on a widget without a type', () => {
    expect(widgetSummary({})).toBe('')
  })
})

describe('typeChanges', () => {
  it('sets the type first and removes the known keys the new type does not take', () => {
    expect(typeChanges({ type: 'form', entity: 'Order', actions: [], form: 'F', 'x-note': 'keep' }, 'table')).toEqual([
      ['/type', 'table'],
      ['/form', undefined],
      ['/actions', undefined],
    ])
  })

  it('adds an empty process when the new type is a process inspector', () => {
    expect(typeChanges({ type: 'table', dataSource: 'List', formPage: 'Edit' }, 'processInspector')).toEqual([
      ['/type', 'processInspector'],
      ['/dataSource', undefined],
      ['/formPage', undefined],
      ['/process', ''],
    ])
  })

  it('keeps an existing process on a process inspector', () => {
    expect(typeChanges({ type: 'processInspector', process: 'Approval' }, 'processInspector')).toEqual([
      ['/type', 'processInspector'],
    ])
  })

  it('removes the process when the new type does not take it', () => {
    expect(typeChanges({ type: 'processInspector', process: 'Approval' }, 'taskInbox')).toEqual([
      ['/type', 'taskInbox'],
      ['/process', undefined],
    ])
  })

  it('never touches an unknown key', () => {
    const changes = typeChanges({ type: 'form', 'x-note': 'keep', layout: {} }, 'taskInbox')
    expect(changes.map(([pointer]) => pointer)).toEqual(['/type'])
  })
})

describe('widget keys', () => {
  it('lists every type, and the known keys are the union', () => {
    expect(Object.keys(widgetKeys)).toEqual(widgetTypes)
    expect([...knownWidgetKeys].sort()).toEqual(['actions', 'dataSource', 'entity', 'form', 'formPage', 'process'])
  })
})
