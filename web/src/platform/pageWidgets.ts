/** The widget types the page editor has a panel for. */
export const widgetTypes = ['table', 'form', 'taskInbox', 'processInspector']

/** The properties each widget type takes besides `type`, from the page schema and the compiler's rules. */
export const widgetKeys: Record<string, string[]> = {
  table: ['entity', 'dataSource', 'formPage'],
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
