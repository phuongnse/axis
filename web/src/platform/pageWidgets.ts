import { getAt } from './jsonPatch'
import type { StudioNames } from './studio'

/** The widget types the page editor has a panel for. */
export const widgetTypes = ['table', 'form', 'taskInbox', 'processInspector', 'kpi', 'chart', 'detail']

/** The widget types that hold lists of widgets. The editor shows their lists and fixes their type. */
export const containerTypes = ['columns', 'tabs', 'section']

/** Every type an author can add to a list. */
export const addableTypes = [...widgetTypes, ...containerTypes]

/** The properties each widget type takes besides `type`, from the page schema and the compiler's rules. */
export const widgetKeys: Record<string, string[]> = {
  table: ['entity', 'dataSource', 'rowActions', 'toolbarActions'],
  form: ['entity', 'form', 'actions'],
  taskInbox: [],
  processInspector: ['process'],
  kpi: ['dataSource', 'measure', 'label', 'comparison', 'navigate'],
  chart: ['dataSource', 'kind', 'category', 'measures', 'title'],
  detail: ['entity', 'form', 'record', 'actions'],
}

/** Every property the editor knows on a widget. A key outside this set is never touched. */
export const knownWidgetKeys = [...new Set(Object.values(widgetKeys).flat())]

/** The end of a pointer to an action: a list entry of a table, form or detail, or the navigate action of a KPI. */
const actionEnd = /\/(?:(?:rowActions|toolbarActions|actions)\/\d+|navigate)$/

/**
 * The pointer of the widget that a selection pointer names. The selection of an action ends in the action, such as
 * `/widgets/0/rowActions/1`, and its widget is `/widgets/0`. The pointer of a widget is returned as it is.
 */
export function widgetPointerOf(pointer: string): string {
  return pointer.replace(actionEnd, '')
}

/**
 * The key of the action a selection pointer names, relative to its widget, such as `rowActions/1` or `navigate`.
 * It is `undefined` when the pointer names a widget.
 */
export function actionKeyOf(pointer: string): string | undefined {
  const end = actionEnd.exec(pointer)
  return end === null ? undefined : end[0].slice(1)
}

/** The properties of a widget that name what it shows, in the order the summary prefers them. */
const summaryKeys = ['entity', 'dataSource', 'form', 'process']

/** A widget's type and the first name it binds, such as `table · PurchaseRequestList`. */
export function widgetSummary(widget: Record<string, unknown>): string {
  const type = typeof widget.type === 'string' ? widget.type : ''
  const name = summaryKeys.map((key) => widget[key]).find((value) => typeof value === 'string' && value !== '')
  return [type, name].filter((part) => typeof part === 'string' && part !== '').join(' · ')
}

/**
 * The changes that give a widget another type, as pointers inside the widget and their new values. The type comes
 * first. A known property the new type does not take is removed, because the compiler rejects it. A key that
 * `newWidget` gives the new type and the widget does not have is added empty, because the schema requires it. A key
 * the editor does not know is never touched.
 */
export function typeChanges(widget: Record<string, unknown>, next: string): [string, unknown][] {
  const changes: [string, unknown][] = [['/type', next]]
  const taken = widgetKeys[next] ?? []
  for (const key of knownWidgetKeys) {
    if (widget[key] !== undefined && !taken.includes(key)) {
      changes.push([`/${key}`, undefined])
    }
  }
  for (const [key, value] of Object.entries(newWidget(next))) {
    if (key !== 'type' && widget[key] === undefined) {
      changes.push([`/${key}`, value])
    }
  }
  return changes
}

/**
 * A new column for a columns widget. Its width is what is left of 12 after the existing columns, or 1 when nothing
 * is left. The existing columns keep their widths.
 */
export function newColumn(columns: unknown[]): Record<string, unknown> {
  const used = columns.reduce<number>(
    (sum, column) => sum + (isContent(column) && typeof column.width === 'number' ? column.width : 0),
    0,
  )
  const left = 12 - used
  return { width: left >= 1 && left <= 12 ? left : 1, widgets: [] }
}

/** A new tab with an empty label key and no widgets. */
export function newTab(): Record<string, unknown> {
  return { label: { textKey: '' }, widgets: [] }
}

/**
 * A new widget of a type. A container starts with the shape the page schema requires and empty lists. A process
 * inspector starts with an empty process, a detail with an empty record, a KPI with an empty data source, measure and
 * label, and a chart with an empty data source and category, the `bar` kind, one empty measure and an empty title,
 * which the schema requires.
 */
export function newWidget(type: string): Record<string, unknown> {
  switch (type) {
    case 'columns':
      return {
        type,
        columns: [
          { width: 6, widgets: [] },
          { width: 6, widgets: [] },
        ],
      }
    case 'tabs':
      return { type, tabs: [newTab()] }
    case 'section':
      return { type, title: { textKey: '' }, widgets: [] }
    case 'processInspector':
      return { type, process: '' }
    case 'kpi':
      return { type, dataSource: '', measure: '', label: { textKey: '' } }
    case 'chart':
      return {
        type,
        dataSource: '',
        kind: 'bar',
        category: '',
        measures: [{ name: '', label: { textKey: '' } }],
        title: { textKey: '' },
      }
    case 'detail':
      return { type, record: '' }
    default:
      return { type }
  }
}

/** The part of a container that holds a widget list: a column or a tab by its index, or the body of a section. */
export type ListPart = { kind: 'column' | 'tab'; index: number } | { kind: 'section' }

/** A widget list inside a container. */
export interface ChildList {
  pointer: string
  part: ListPart
}

function isContent(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

/**
 * The widget lists directly inside a container widget at `pointer`. A column or tab without a `widgets` list is
 * skipped, because the editor cannot show it.
 */
export function childLists(widget: Record<string, unknown>, pointer: string): ChildList[] {
  if (widget.type === 'section') {
    return Array.isArray(widget.widgets) ? [{ pointer: `${pointer}/widgets`, part: { kind: 'section' } }] : []
  }
  const key = widget.type === 'columns' ? 'columns' : widget.type === 'tabs' ? 'tabs' : undefined
  const parts = key === undefined ? undefined : widget[key]
  if (key === undefined || !Array.isArray(parts)) {
    return []
  }
  const kind = key === 'columns' ? 'column' : 'tab'
  return parts.flatMap((part, index) =>
    isContent(part) && Array.isArray(part.widgets)
      ? [{ pointer: `${pointer}/${key}/${index}/widgets`, part: { kind, index } as ListPart }]
      : [],
  )
}

/** One level of a list's path: the container widget, by its type and place in its list, and the part of it. */
export interface PathStep {
  type: string
  /** The widget's position in its list, counted from 1. */
  position: number
  part: ListPart
}

/** A widget list in the tree, with the containers and parts that lead to it from the page. */
export interface WidgetList {
  pointer: string
  path: PathStep[]
}

/** Every widget list in the tree, the page's own list first, in document order. */
export function widgetLists(widgets: unknown[]): WidgetList[] {
  const lists: WidgetList[] = []
  function walk(items: unknown[], pointer: string, path: PathStep[]) {
    lists.push({ pointer, path })
    items.forEach((item, index) => {
      if (!isContent(item) || typeof item.type !== 'string') {
        return
      }
      for (const child of childLists(item, `${pointer}/${index}`)) {
        walk(getAt({ widgets }, child.pointer) as unknown[], child.pointer, [
          ...path,
          { type: item.type, position: index + 1, part: child.part },
        ])
      }
    })
  }
  walk(widgets, '/widgets', [])
  return lists
}

/** The level of a widget list, the page's own list being 1: the number of `widgets` segments in its pointer. */
export function listLevel(list: string): number {
  return list.split('/').filter((segment) => segment === 'widgets').length
}

/**
 * Where the palette puts a new widget for the selected pointer: after a leaf, at the end of a container's first list
 * (after the container when it has none), or at the end of the page's list when `selected` names no widget.
 */
export function insertionPlace(widgets: unknown[], selected: string): { list: string; index: number } {
  const widget = getAt({ widgets }, selected)
  if (!isContent(widget) || typeof widget.type !== 'string') {
    return { list: '/widgets', index: widgets.length }
  }
  const first = childLists(widget, selected)[0]
  if (containerTypes.includes(widget.type) && first !== undefined) {
    const items = getAt({ widgets }, first.pointer) as unknown[]
    return { list: first.pointer, index: items.length }
  }
  const cut = selected.lastIndexOf('/')
  return { list: selected.slice(0, cut), index: Number(selected.slice(cut + 1)) + 1 }
}

/** Why the page cannot take one more widget of `type` in `list`, or undefined. */
export function paletteLimit(
  widgets: unknown[],
  list: string,
  type: string,
): 'table' | 'recordWidget' | 'depth' | undefined {
  const types = widgetLists(widgets).flatMap((item) =>
    (getAt({ widgets }, item.pointer) as unknown[]).map((widget) => (isContent(widget) ? widget.type : undefined)),
  )
  if (type === 'table' && types.includes('table')) {
    return 'table'
  }
  const recordWidgets = ['form', 'taskInbox', 'processInspector']
  if (recordWidgets.includes(type) && types.some((existing) => recordWidgets.includes(existing as string))) {
    return 'recordWidget'
  }
  if (containerTypes.includes(type) && listLevel(list) >= 4) {
    return 'depth'
  }
  return undefined
}

/** What a palette type is dropped on: a widget, on its upper or lower half, or a widget list. */
export type DropTarget =
  { kind: 'widget'; pointer: string; half: 'upper' | 'lower' } | { kind: 'list'; pointer: string }

/**
 * The place a drop on `target` gives: the list and the index in it. A widget target gives its own list, at its index on
 * the upper half and after it on the lower half. A list target gives that list, at its end. It is `undefined` when the
 * draft has no such widget or list. The widgets are never changed.
 */
export function targetPlace(widgets: unknown[], target: DropTarget): { list: string; index: number } | undefined {
  if (target.kind === 'widget') {
    const cut = target.pointer.lastIndexOf('/')
    const list = target.pointer.slice(0, cut)
    const index = Number(target.pointer.slice(cut + 1))
    if (!Array.isArray(getAt({ widgets }, list)) || getAt({ widgets }, target.pointer) === undefined) {
      return undefined
    }
    return { list, index: target.half === 'upper' ? index : index + 1 }
  }
  const items = getAt({ widgets }, target.pointer)
  return Array.isArray(items) ? { list: target.pointer, index: items.length } : undefined
}

/**
 * Where a new widget of `type` lands when it is dropped on `target`, or the page limit that forbids it. See
 * {@link targetPlace} for the place. It is `undefined` when the draft has no such widget or list.
 */
export function dropPlace(
  widgets: unknown[],
  type: string,
  target: DropTarget,
): { list: string; index: number } | { limit: 'table' | 'recordWidget' | 'depth' } | undefined {
  const place = targetPlace(widgets, target)
  if (place === undefined) {
    return undefined
  }
  const limit = paletteLimit(widgets, place.list, type)
  return limit === undefined ? place : { limit }
}

/** The pointer with the most segments, which is the innermost one, or `undefined` for none. The first of equals wins. */
export function innermostPointer(pointers: string[]): string | undefined {
  let best: string | undefined
  for (const pointer of pointers) {
    if (best === undefined || pointer.split('/').length > best.split('/').length) {
      best = pointer
    }
  }
  return best
}

/**
 * The page's widgets with the widget at `from` moved into the list at `toList`, and the pointer the widget has after
 * the move. `index` is the place in the list as it was before the move, and defaults to the end. The widget keeps
 * every key. Nothing changes when `from` does not hold a widget, or when `toList` is not a list or lies inside the
 * widget.
 */
export function moveTo(
  widgets: unknown[],
  from: string,
  toList: string,
  index?: number,
): { widgets: unknown[]; pointer: string } | undefined {
  const cut = from.lastIndexOf('/')
  const sourcePointer = from.slice(0, cut)
  const sourceIndex = Number(from.slice(cut + 1))
  const root = structuredClone({ widgets })
  const source = getAt(root, sourcePointer)
  const target = getAt(root, toList)
  if (!Array.isArray(source) || !Array.isArray(target) || source[sourceIndex] === undefined) {
    return undefined
  }
  if (toList.startsWith(`${from}/`)) {
    return undefined
  }
  const [moved] = source.splice(sourceIndex, 1)
  // The index counts the list as it was before the widget left it.
  const shifted = index !== undefined && target === source && index > sourceIndex ? index - 1 : index
  const at = Math.min(Math.max(shifted ?? target.length, 0), target.length)
  target.splice(at, 0, moved)
  let targetPointer = toList
  if (toList.startsWith(`${sourcePointer}/`)) {
    // The target lies inside a widget of the source list, so a widget removed before it shifts its index.
    const rest = toList.slice(sourcePointer.length + 1).split('/')
    if (Number(rest[0]) > sourceIndex) {
      rest[0] = String(Number(rest[0]) - 1)
    }
    targetPointer = `${sourcePointer}/${rest.join('/')}`
  }
  return { widgets: root.widgets, pointer: `${targetPointer}/${at}` }
}

/**
 * Where the widget at `from` lands when it is dropped on `target`, or the limit that forbids it. A widget target gives
 * its own list, at its index on the upper half and after it on the lower half. A list target gives that list, at its
 * end. The limit is `inside` for a drop inside the widget itself, and `depth` when the deepest list in the widget
 * would land past level 4. It is `undefined` when `from` holds no widget, the draft has no such target, or the
 * target is the widget itself. The widgets are never changed.
 */
export function movePlace(
  widgets: unknown[],
  from: string,
  target: DropTarget,
): { list: string; index: number } | { limit: 'inside' | 'depth' } | undefined {
  const widget = getAt({ widgets }, from)
  if (!isContent(widget) || target.pointer === from) {
    return undefined
  }
  if (target.pointer.startsWith(`${from}/`)) {
    return { limit: 'inside' }
  }
  let place: { list: string; index: number }
  if (target.kind === 'widget') {
    const cut = target.pointer.lastIndexOf('/')
    const list = target.pointer.slice(0, cut)
    const index = Number(target.pointer.slice(cut + 1))
    if (!Array.isArray(getAt({ widgets }, list)) || getAt({ widgets }, target.pointer) === undefined) {
      return undefined
    }
    place = { list, index: target.half === 'upper' ? index : index + 1 }
  } else {
    const items = getAt({ widgets }, target.pointer)
    if (!Array.isArray(items)) {
      return undefined
    }
    place = { list: target.pointer, index: items.length }
  }
  const inner = widgetLists(widgets)
    .filter((item) => item.pointer.startsWith(`${from}/`))
    .map((item) => listLevel(item.pointer))
  const extra = inner.length === 0 ? 0 : Math.max(...inner) - listLevel(from.slice(0, from.lastIndexOf('/')))
  return listLevel(place.list) + extra > 4 ? { limit: 'depth' } : place
}

/** A column a data source projects, with its type as Studio's names give it. */
export interface DataSourceMeasure {
  name: string
  type: string | null
}

/**
 * The group fields and measures of the data source a widget names, found in Studio's names ignoring letter case.
 * The group fields are the data source's `groupBy`, and the measures are its other columns. It is `undefined` when
 * the names do not list the data source.
 */
export function dataSourceColumns(
  names: StudioNames,
  dataSource: unknown,
): { groupFields: string[]; measures: DataSourceMeasure[] } | undefined {
  if (typeof dataSource !== 'string') {
    return undefined
  }
  const wanted = dataSource.toLowerCase()
  const found = names.dataSources.find((item) => item.name.toLowerCase() === wanted)
  if (found === undefined) {
    return undefined
  }
  const groupFields = found.groupBy ?? []
  const grouped = groupFields.map((field) => field.toLowerCase())
  return { groupFields, measures: found.columns.filter((column) => !grouped.includes(column.name.toLowerCase())) }
}

/**
 * The parameters of the data source a widget names, found in Studio's names ignoring letter case. It is `undefined`
 * when the names do not list the data source.
 */
export function dataSourceParameters(
  names: StudioNames,
  dataSource: unknown,
): { name: string; type: string }[] | undefined {
  if (typeof dataSource !== 'string') {
    return undefined
  }
  const wanted = dataSource.toLowerCase()
  return names.dataSources.find((item) => item.name.toLowerCase() === wanted)?.parameters
}

/** Whether a column type is a number, which a chart measure must be. */
export function isNumberType(type: string | null): boolean {
  return type === 'integer' || type === 'decimal'
}

/** An entity or a data source that the data panel offers to bind a widget to. */
export interface DataBinding {
  kind: 'entity' | 'dataSource'
  name: string
}

/**
 * The widget types that can show a binding. An entity gives a table, a form or a detail. A data source gives a table,
 * a chart when it has exactly one group field, and a KPI when it has a total row, which is an empty `groupBy`.
 * Names are found ignoring letter case. A data source the names do not list gives a table only.
 */
export function bindingTypes(names: StudioNames, binding: DataBinding): string[] {
  if (binding.kind === 'entity') {
    return ['table', 'form', 'detail']
  }
  const wanted = binding.name.toLowerCase()
  const groupBy = names.dataSources.find((item) => item.name.toLowerCase() === wanted)?.groupBy
  const types = ['table']
  if (groupBy?.length === 1) {
    types.push('chart')
  }
  if (Array.isArray(groupBy) && groupBy.length === 0) {
    types.push('kpi')
  }
  return types
}

/**
 * A new widget of `type` bound to `binding`. A table or a form takes the entity or data source. A detail takes the
 * entity and, as its record, the first required `reference` parameter of the page that targets the entity, or an empty
 * record when there is none. A chart takes the data source, its only group field as the category and its first numeric
 * measure, or its first measure when none is numeric. A KPI takes the data source's first measure. Texts stay empty,
 * as `newWidget` leaves them, because a data source measure has no label.
 */
export function boundWidget(
  names: StudioNames,
  page: Record<string, unknown>,
  binding: DataBinding,
  type: string,
): Record<string, unknown> {
  if (binding.kind === 'entity') {
    if (type !== 'detail') {
      return { type, entity: binding.name }
    }
    const wanted = binding.name.toLowerCase()
    const parameters = Array.isArray(page.parameters) ? page.parameters : []
    const record = parameters.find(
      (parameter) =>
        isContent(parameter) &&
        parameter.type === 'reference' &&
        parameter.required === true &&
        typeof parameter.target === 'string' &&
        parameter.target.toLowerCase() === wanted,
    ) as Record<string, unknown> | undefined
    return { type, entity: binding.name, record: typeof record?.name === 'string' ? record.name : '' }
  }
  const columns = dataSourceColumns(names, binding.name)
  const first = columns?.measures[0]?.name
  if (type === 'chart') {
    const measure = columns?.measures.find((item) => isNumberType(item.type))?.name ?? first ?? ''
    return {
      ...newWidget('chart'),
      dataSource: binding.name,
      category: columns?.groupFields[0] ?? '',
      measures: [{ name: measure, label: { textKey: '' } }],
    }
  }
  if (type === 'kpi') {
    return { ...newWidget('kpi'), dataSource: binding.name, measure: first ?? '' }
  }
  return { type, dataSource: binding.name }
}
