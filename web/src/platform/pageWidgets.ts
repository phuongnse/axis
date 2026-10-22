import { getAt } from './jsonPatch'

/** The widget types the page editor has a panel for. */
export const widgetTypes = ['table', 'form', 'taskInbox', 'processInspector']

/** The widget types that hold lists of widgets. The editor shows their lists and fixes their type. */
export const containerTypes = ['columns', 'tabs', 'section']

/** Every type an author can add to a list. */
export const addableTypes = [...widgetTypes, ...containerTypes]

/** The properties each widget type takes besides `type`, from the page schema and the compiler's rules. */
export const widgetKeys: Record<string, string[]> = {
  table: ['entity', 'dataSource', 'formPage', 'rowActions', 'toolbarActions'],
  form: ['entity', 'form', 'actions'],
  taskInbox: [],
  processInspector: ['process'],
}

/** Every property the editor knows on a widget. A key outside this set is never touched. */
export const knownWidgetKeys = [...new Set(Object.values(widgetKeys).flat())]

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
 * first. A known property the new type does not take is removed, because the compiler rejects it. A switch to
 * `processInspector` adds an empty `process` when there is none, because the schema requires it. A key the editor
 * does not know is never touched.
 */
export function typeChanges(widget: Record<string, unknown>, next: string): [string, unknown][] {
  const changes: [string, unknown][] = [['/type', next]]
  const taken = widgetKeys[next] ?? []
  for (const key of knownWidgetKeys) {
    if (widget[key] !== undefined && !taken.includes(key)) {
      changes.push([`/${key}`, undefined])
    }
  }
  if (next === 'processInspector' && widget.process === undefined) {
    changes.push(['/process', ''])
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

/** A new widget of a type. A container starts with the shape the page schema requires and empty lists. */
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

/**
 * The page's widgets with the widget at `from` moved to the end of the list at `toList`, and the pointer the widget
 * has after the move. The widget keeps every key. Nothing changes when `from` does not hold a widget, or when
 * `toList` is not a list or lies inside the widget.
 */
export function moveTo(
  widgets: unknown[],
  from: string,
  toList: string,
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
  target.push(source[sourceIndex])
  source.splice(sourceIndex, 1)
  let targetPointer = toList
  if (toList.startsWith(`${sourcePointer}/`)) {
    // The target lies inside a widget of the source list, so a widget removed before it shifts its index.
    const rest = toList.slice(sourcePointer.length + 1).split('/')
    if (Number(rest[0]) > sourceIndex) {
      rest[0] = String(Number(rest[0]) - 1)
    }
    targetPointer = `${sourcePointer}/${rest.join('/')}`
  }
  return { widgets: root.widgets, pointer: `${targetPointer}/${target.length - 1}` }
}
