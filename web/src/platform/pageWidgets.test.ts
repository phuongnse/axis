import { describe, expect, it } from 'vitest'
import {
  addableTypes,
  childLists,
  containerTypes,
  knownWidgetKeys,
  moveTo,
  newWidget,
  typeChanges,
  widgetKeys,
  widgetLists,
  widgetSummary,
  widgetTypes,
} from './pageWidgets'

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

  it('removes the row and toolbar actions of a table that becomes a form', () => {
    const navigate = { type: 'navigate', page: 'Edit', label: { textKey: 'x' } }
    expect(
      typeChanges({ type: 'table', rowActions: [navigate], toolbarActions: [navigate], 'x-note': 'keep' }, 'form'),
    ).toEqual([
      ['/type', 'form'],
      ['/rowActions', undefined],
      ['/toolbarActions', undefined],
    ])
  })

  it('adds an empty process when the new type is a process inspector', () => {
    expect(typeChanges({ type: 'table', dataSource: 'List' }, 'processInspector')).toEqual([
      ['/type', 'processInspector'],
      ['/dataSource', undefined],
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
    expect([...knownWidgetKeys].sort()).toEqual([
      'actions',
      'dataSource',
      'entity',
      'form',
      'process',
      'rowActions',
      'toolbarActions',
    ])
  })
})

const tree = [
  { type: 'table', entity: 'Order' },
  {
    type: 'columns',
    columns: [
      { width: 6, widgets: [{ type: 'form', form: 'F', 'x-note': 'keep' }] },
      { width: 6, widgets: [] },
    ],
  },
  { type: 'section', title: { textKey: '' }, widgets: [{ type: 'taskInbox' }] },
]

describe('newWidget', () => {
  it('gives each container the shape the schema requires', () => {
    expect(newWidget('columns')).toEqual({
      type: 'columns',
      columns: [
        { width: 6, widgets: [] },
        { width: 6, widgets: [] },
      ],
    })
    expect(newWidget('tabs')).toEqual({ type: 'tabs', tabs: [{ label: { textKey: '' }, widgets: [] }] })
    expect(newWidget('section')).toEqual({ type: 'section', title: { textKey: '' }, widgets: [] })
  })

  it('gives a process inspector an empty process and any other type only its type', () => {
    expect(newWidget('processInspector')).toEqual({ type: 'processInspector', process: '' })
    expect(newWidget('table')).toEqual({ type: 'table' })
  })

  it('adds the containers to the addable types and keeps the types with a panel apart', () => {
    expect(addableTypes).toEqual([...widgetTypes, ...containerTypes])
    expect(widgetTypes).toEqual(['table', 'form', 'taskInbox', 'processInspector'])
  })
})

describe('childLists', () => {
  it('maps columns, tabs and a section to their widget lists', () => {
    expect(childLists(tree[1] as Record<string, unknown>, '/widgets/1')).toEqual([
      { pointer: '/widgets/1/columns/0/widgets', part: { kind: 'column', index: 0 } },
      { pointer: '/widgets/1/columns/1/widgets', part: { kind: 'column', index: 1 } },
    ])
    expect(childLists({ type: 'tabs', tabs: [{ widgets: [] }] }, '/widgets/0')).toEqual([
      { pointer: '/widgets/0/tabs/0/widgets', part: { kind: 'tab', index: 0 } },
    ])
    expect(childLists(tree[2] as Record<string, unknown>, '/widgets/2')).toEqual([
      { pointer: '/widgets/2/widgets', part: { kind: 'section' } },
    ])
  })

  it('skips what is not a list and has none for a leaf', () => {
    expect(childLists({ type: 'columns', columns: [{ width: 6 }, 'oops', { widgets: [] }] }, '/widgets/0')).toEqual([
      { pointer: '/widgets/0/columns/2/widgets', part: { kind: 'column', index: 2 } },
    ])
    expect(childLists({ type: 'tabs' }, '/widgets/0')).toEqual([])
    expect(childLists({ type: 'section', widgets: {} }, '/widgets/0')).toEqual([])
    expect(childLists({ type: 'table' }, '/widgets/0')).toEqual([])
  })
})

describe('widgetLists', () => {
  it('lists every list in document order with the path that leads to it', () => {
    expect(widgetLists(tree)).toEqual([
      { pointer: '/widgets', path: [] },
      {
        pointer: '/widgets/1/columns/0/widgets',
        path: [{ type: 'columns', position: 2, part: { kind: 'column', index: 0 } }],
      },
      {
        pointer: '/widgets/1/columns/1/widgets',
        path: [{ type: 'columns', position: 2, part: { kind: 'column', index: 1 } }],
      },
      { pointer: '/widgets/2/widgets', path: [{ type: 'section', position: 3, part: { kind: 'section' } }] },
    ])
  })
})

describe('moveTo', () => {
  it('appends the widget to the target list and keeps every key', () => {
    const moved = moveTo(tree, '/widgets/1/columns/0/widgets/0', '/widgets/2/widgets')!
    expect(moved.pointer).toBe('/widgets/2/widgets/1')
    expect((moved.widgets[2] as { widgets: unknown[] }).widgets[1]).toEqual({
      type: 'form',
      form: 'F',
      'x-note': 'keep',
    })
    expect(widgetLists(moved.widgets).length).toBe(4)
  })

  it('moves a top-level widget into a column and shifts nothing else', () => {
    const moved = moveTo(tree, '/widgets/0', '/widgets/1/columns/1/widgets')!
    expect(moved.widgets).toHaveLength(2)
    // The columns widget was at 1 and is at 0 now.
    expect(moved.pointer).toBe('/widgets/0/columns/1/widgets/0')
    expect((moved.widgets[0] as { columns: { widgets: unknown[] }[] }).columns[1].widgets).toEqual([tree[0]])
  })

  it('does not change the input', () => {
    const before = structuredClone(tree)
    moveTo(tree, '/widgets/0', '/widgets/1/columns/1/widgets')
    expect(tree).toEqual(before)
  })

  it('moves a widget out of a container to the page', () => {
    const moved = moveTo(tree, '/widgets/2/widgets/0', '/widgets')!
    expect(moved.pointer).toBe('/widgets/3')
    expect(moved.widgets[3]).toEqual({ type: 'taskInbox' })
    expect((moved.widgets[2] as { widgets: unknown[] }).widgets).toEqual([])
  })

  it('refuses a target inside the widget or a missing widget', () => {
    expect(moveTo(tree, '/widgets/1', '/widgets/1/columns/0/widgets')).toBeUndefined()
    expect(moveTo(tree, '/widgets/9', '/widgets')).toBeUndefined()
    expect(moveTo(tree, '/widgets/0', '/widgets/0/columns/0/widgets')).toBeUndefined()
  })
})
