import { describe, expect, it } from 'vitest'
import type { PageWidgetMetadata, TabsWidgetMetadata, WidgetMetadata } from './site'
import { isContainer, leafWidgets, tabParameters } from './widgetTree'

function leaf(type: WidgetMetadata['type'], process: string | null = null): WidgetMetadata {
  return {
    type,
    entity: null,
    dataSource: null,
    form: null,
    tasksPath: null,
    process,
    instancesPath: null,
    actions: [],
    rowActions: [],
    toolbarActions: [],
    parameters: {},
    access: null,
    detail: null,
    kpi: null,
    chart: null,
  }
}

function tabs(...widgets: PageWidgetMetadata[][]): TabsWidgetMetadata {
  return { type: 'tabs', tabs: widgets.map((list, index) => ({ labelKey: `tab.${index}`, widgets: list })) }
}

describe('isContainer', () => {
  it('is true for columns, tabs and section, and false for a widget that shows something', () => {
    expect(isContainer({ type: 'columns', columns: [] })).toBe(true)
    expect(isContainer(tabs())).toBe(true)
    expect(isContainer({ type: 'section', titleKey: 't', collapsed: false, widgets: [] })).toBe(true)
    expect(isContainer(leaf('table'))).toBe(false)
  })
})

describe('leafWidgets', () => {
  it('lists the widgets that show something depth first, in document order', () => {
    const first = leaf('table')
    const second = leaf('taskInbox')
    const third = leaf('kpi')
    const fourth = leaf('form')
    const fifth = leaf('processInspector', 'Review')

    const widgets: PageWidgetMetadata[] = [
      first,
      {
        type: 'columns',
        columns: [
          { width: 8, widgets: [{ type: 'section', titleKey: 's', collapsed: true, widgets: [second] }] },
          { width: 4, widgets: [tabs([third], [fourth])] },
        ],
      },
      fifth,
    ]

    expect(leafWidgets(widgets)).toEqual([first, second, third, fourth, fifth])
  })

  it('is empty for no widgets and for containers without widgets', () => {
    expect(leafWidgets([])).toEqual([])
    expect(leafWidgets([{ type: 'columns', columns: [{ width: 12, widgets: [] }] }, tabs([])])).toEqual([])
  })
})

describe('tabParameters', () => {
  it('names the first tabs widget tab, then tab2, tab3, in document order', () => {
    const one = tabs([])
    const two = tabs([])
    const three = tabs([])

    const names = tabParameters([
      one,
      {
        type: 'columns',
        columns: [
          { width: 6, widgets: [two] },
          { width: 6, widgets: [three] },
        ],
      },
    ])

    expect([names.get(one), names.get(two), names.get(three)]).toEqual(['tab', 'tab2', 'tab3'])
  })

  it('counts a tabs widget inside a tab whether or not that tab is selected', () => {
    const inner = tabs([])
    const outer = tabs([], [inner])
    const after = tabs([])

    const names = tabParameters([outer, after])

    expect([names.get(outer), names.get(inner), names.get(after)]).toEqual(['tab', 'tab2', 'tab3'])
  })

  it('gives nothing for a page without tabs', () => {
    expect(tabParameters([leaf('table')]).size).toBe(0)
  })
})
