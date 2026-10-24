import { describe, expect, it } from 'vitest'
import {
  addableTypes,
  childLists,
  containerTypes,
  dropPlace,
  innermostPointer,
  insertionPlace,
  knownWidgetKeys,
  listLevel,
  moveTo,
  newColumn,
  newTab,
  newWidget,
  paletteLimit,
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

  it('adds an empty record when a form becomes a detail and keeps its entity and actions', () => {
    expect(typeChanges({ type: 'form', entity: 'Order', actions: [] }, 'detail')).toEqual([
      ['/type', 'detail'],
      ['/record', ''],
    ])
  })

  it('adds the keys a KPI requires and keeps the data source when a table becomes a KPI', () => {
    expect(
      typeChanges({ type: 'table', dataSource: 'List', entity: 'Order', rowActions: [], toolbarActions: [] }, 'kpi'),
    ).toEqual([
      ['/type', 'kpi'],
      ['/entity', undefined],
      ['/rowActions', undefined],
      ['/toolbarActions', undefined],
      ['/measure', ''],
      ['/label', { textKey: '' }],
    ])
  })

  it('adds the keys a chart requires to a task inbox', () => {
    expect(typeChanges({ type: 'taskInbox' }, 'chart')).toEqual([
      ['/type', 'chart'],
      ['/dataSource', ''],
      ['/kind', 'bar'],
      ['/category', ''],
      ['/measures', [{ name: '', label: { textKey: '' } }]],
      ['/title', { textKey: '' }],
    ])
  })

  it('removes the chart keys when a chart becomes a table', () => {
    expect(
      typeChanges(
        {
          type: 'chart',
          dataSource: 'D',
          kind: 'pie',
          category: 'c',
          measures: [{ name: 'm' }],
          title: { textKey: 't' },
        },
        'table',
      ),
    ).toEqual([
      ['/type', 'table'],
      ['/kind', undefined],
      ['/category', undefined],
      ['/measures', undefined],
      ['/title', undefined],
    ])
  })

  it('removes the record when a detail becomes a table', () => {
    expect(typeChanges({ type: 'detail', entity: 'Order', form: 'F', record: 'order' }, 'table')).toEqual([
      ['/type', 'table'],
      ['/form', undefined],
      ['/record', undefined],
    ])
  })
})

describe('widget keys', () => {
  it('lists every type, and the known keys are the union', () => {
    expect(Object.keys(widgetKeys)).toEqual(widgetTypes)
    expect([...knownWidgetKeys].sort()).toEqual([
      'actions',
      'category',
      'comparison',
      'dataSource',
      'entity',
      'form',
      'kind',
      'label',
      'measure',
      'measures',
      'navigate',
      'process',
      'record',
      'rowActions',
      'title',
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

  it('gives a KPI and a chart the keys the schema requires, left empty', () => {
    expect(newWidget('kpi')).toEqual({ type: 'kpi', dataSource: '', measure: '', label: { textKey: '' } })
    expect(newWidget('chart')).toEqual({
      type: 'chart',
      dataSource: '',
      kind: 'bar',
      category: '',
      measures: [{ name: '', label: { textKey: '' } }],
      title: { textKey: '' },
    })
  })

  it('gives a detail an empty record', () => {
    expect(newWidget('detail')).toEqual({ type: 'detail', record: '' })
  })

  it('adds the containers to the addable types and keeps the types with a panel apart', () => {
    expect(addableTypes).toEqual([...widgetTypes, ...containerTypes])
    expect(widgetTypes).toEqual(['table', 'form', 'taskInbox', 'processInspector', 'kpi', 'chart', 'detail'])
    expect(addableTypes).toHaveLength(10)
    expect(addableTypes).toContain('kpi')
    expect(addableTypes).toContain('chart')
  })
})

describe('newColumn', () => {
  it('takes what is left of 12', () => {
    expect(newColumn([{ width: 6 }])).toEqual({ width: 6, widgets: [] })
    expect(newColumn([])).toEqual({ width: 12, widgets: [] })
  })

  it('takes 1 when nothing is left or the widths are not numbers', () => {
    expect(newColumn([{ width: 6 }, { width: 6 }])).toEqual({ width: 1, widgets: [] })
    expect(newColumn([{ width: 8 }, { width: 8 }])).toEqual({ width: 1, widgets: [] })
    expect(newColumn(['x', { width: '6' }])).toEqual({ width: 12, widgets: [] })
  })
})

describe('newTab', () => {
  it('has an empty label key and no widgets', () => {
    expect(newTab()).toEqual({ label: { textKey: '' }, widgets: [] })
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

describe('listLevel', () => {
  it('counts the widgets segments of the pointer', () => {
    expect(listLevel('/widgets')).toBe(1)
    expect(listLevel('/widgets/0/widgets')).toBe(2)
    expect(listLevel('/widgets/0/columns/1/widgets')).toBe(2)
    expect(listLevel('/widgets/0/tabs/0/widgets/1/widgets/2/widgets')).toBe(4)
  })
})

describe('insertionPlace', () => {
  const page = [
    { type: 'kpi' },
    { type: 'section', title: { textKey: '' }, widgets: [{ type: 'chart' }, { type: 'kpi' }] },
    {
      type: 'columns',
      columns: [
        { width: 6, widgets: [{ type: 'kpi' }] },
        { width: 6, widgets: [] },
      ],
    },
    {
      type: 'tabs',
      tabs: [
        { label: { textKey: '' }, widgets: [] },
        { label: { textKey: '' }, widgets: [{ type: 'kpi' }] },
      ],
    },
    { type: 'columns' },
  ]

  it('puts the widget right after a leaf', () => {
    expect(insertionPlace(page, '/widgets/0')).toEqual({ list: '/widgets', index: 1 })
    expect(insertionPlace(page, '/widgets/1/widgets/0')).toEqual({ list: '/widgets/1/widgets', index: 1 })
  })

  it('puts the widget at the end of a section', () => {
    expect(insertionPlace(page, '/widgets/1')).toEqual({ list: '/widgets/1/widgets', index: 2 })
  })

  it('puts the widget at the end of the first column or tab', () => {
    expect(insertionPlace(page, '/widgets/2')).toEqual({ list: '/widgets/2/columns/0/widgets', index: 1 })
    expect(insertionPlace(page, '/widgets/3')).toEqual({ list: '/widgets/3/tabs/0/widgets', index: 0 })
  })

  it('puts the widget after a container that has no list the editor can show', () => {
    expect(insertionPlace(page, '/widgets/4')).toEqual({ list: '/widgets', index: 5 })
  })

  it('puts the widget at the end of the page when nothing is selected', () => {
    expect(insertionPlace([], '/widgets/0')).toEqual({ list: '/widgets', index: 0 })
    expect(insertionPlace(page, '/widgets/9')).toEqual({ list: '/widgets', index: 5 })
  })
})

describe('paletteLimit', () => {
  const section = (widgets: unknown[]) => ({ type: 'section', title: { textKey: '' }, widgets })

  it('refuses a second table, also one nested in a container', () => {
    expect(paletteLimit([{ type: 'kpi' }], '/widgets', 'table')).toBeUndefined()
    expect(paletteLimit([section([{ type: 'table' }])], '/widgets', 'table')).toBe('table')
  })

  it('refuses a second form, task inbox or process inspector', () => {
    const page = [section([{ type: 'taskInbox' }])]
    expect(paletteLimit(page, '/widgets', 'form')).toBe('recordWidget')
    expect(paletteLimit(page, '/widgets', 'taskInbox')).toBe('recordWidget')
    expect(paletteLimit(page, '/widgets', 'processInspector')).toBe('recordWidget')
    expect(paletteLimit(page, '/widgets', 'table')).toBeUndefined()
    expect(paletteLimit([{ type: 'table' }], '/widgets', 'form')).toBeUndefined()
  })

  it('refuses a container in a list on level 4 and takes other types', () => {
    const deep = '/widgets/0/widgets/0/widgets/0/widgets'
    const page = [section([section([section([{ type: 'kpi' }])])])]
    for (const type of containerTypes) {
      expect(paletteLimit(page, deep, type)).toBe('depth')
      expect(paletteLimit(page, '/widgets/0/widgets/0/widgets', type)).toBeUndefined()
    }
    expect(paletteLimit(page, deep, 'kpi')).toBeUndefined()
  })
})

describe('dropPlace', () => {
  const section = (widgets: unknown[]) => ({ type: 'section', title: { textKey: '' }, widgets })
  const page = [{ type: 'kpi' }, section([{ type: 'chart' }, { type: 'detail' }])]

  it('puts the type before a widget on its upper half and after it on its lower half', () => {
    expect(dropPlace(page, 'kpi', { kind: 'widget', pointer: '/widgets/0', half: 'upper' })).toEqual({
      list: '/widgets',
      index: 0,
    })
    expect(dropPlace(page, 'kpi', { kind: 'widget', pointer: '/widgets/0', half: 'lower' })).toEqual({
      list: '/widgets',
      index: 1,
    })
    expect(dropPlace(page, 'kpi', { kind: 'widget', pointer: '/widgets/1/widgets/1', half: 'lower' })).toEqual({
      list: '/widgets/1/widgets',
      index: 2,
    })
  })

  it('appends to a list, empty or not', () => {
    expect(dropPlace(page, 'kpi', { kind: 'list', pointer: '/widgets' })).toEqual({ list: '/widgets', index: 2 })
    expect(dropPlace(page, 'kpi', { kind: 'list', pointer: '/widgets/1/widgets' })).toEqual({
      list: '/widgets/1/widgets',
      index: 2,
    })
    expect(dropPlace([section([])], 'kpi', { kind: 'list', pointer: '/widgets/0/widgets' })).toEqual({
      list: '/widgets/0/widgets',
      index: 0,
    })
  })

  it('does nothing when the draft has no such widget or list', () => {
    expect(dropPlace(page, 'kpi', { kind: 'widget', pointer: '/widgets/5', half: 'upper' })).toBeUndefined()
    expect(dropPlace(page, 'kpi', { kind: 'widget', pointer: '/widgets/0/widgets/0', half: 'upper' })).toBeUndefined()
    expect(dropPlace(page, 'kpi', { kind: 'list', pointer: '/widgets/0/widgets' })).toBeUndefined()
    expect(dropPlace(page, 'kpi', { kind: 'list', pointer: '/widgets/9/widgets' })).toBeUndefined()
  })

  it('gives the page limit that forbids the drop and leaves the widgets unchanged', () => {
    const deep = [section([section([section([{ type: 'kpi' }])])])]
    const before = structuredClone(deep)
    expect(dropPlace(deep, 'section', { kind: 'list', pointer: '/widgets/0/widgets/0/widgets/0/widgets' })).toEqual({
      limit: 'depth',
    })
    expect(
      dropPlace(deep, 'columns', {
        kind: 'widget',
        pointer: '/widgets/0/widgets/0/widgets/0/widgets/0',
        half: 'lower',
      }),
    ).toEqual({ limit: 'depth' })
    expect(dropPlace([{ type: 'table' }], 'table', { kind: 'list', pointer: '/widgets' })).toEqual({ limit: 'table' })
    expect(dropPlace([{ type: 'form' }], 'taskInbox', { kind: 'list', pointer: '/widgets' })).toEqual({
      limit: 'recordWidget',
    })
    expect(deep).toEqual(before)
  })
})

describe('innermostPointer', () => {
  it('picks the pointer with the most segments', () => {
    expect(innermostPointer(['/widgets', '/widgets/0', '/widgets/0/widgets', '/widgets/0/widgets/1'])).toBe(
      '/widgets/0/widgets/1',
    )
    expect(innermostPointer(['/widgets/0/widgets/1', '/widgets'])).toBe('/widgets/0/widgets/1')
  })

  it('picks nothing from no pointers', () => {
    expect(innermostPointer([])).toBeUndefined()
  })
})
