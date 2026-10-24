import type { CSSProperties } from 'react'
import { type ListPart, type PathStep, widgetLists } from '../platform/pageWidgets'
import { useText } from '../platform/texts'

/** Hides the announcement from the eye and keeps it in the accessibility tree. It has no colour of its own. */
export const visuallyHidden: CSSProperties = {
  position: 'absolute',
  width: 1,
  height: 1,
  margin: -1,
  padding: 0,
  border: 0,
  overflow: 'hidden',
  clip: 'rect(0 0 0 0)',
  whiteSpace: 'nowrap',
}

/** The text key of the reason a page limit gives for a type that cannot be added. */
export const paletteReasons = {
  table: 'shell.studio.palette.oneTable',
  recordWidget: 'shell.studio.palette.oneRecordWidget',
  depth: 'shell.studio.palette.tooDeep',
} as const

/** The words that name a part of a container, such as `Column 2` or `Tab 1`. A section has none. */
export function usePartLabel() {
  const t = useText()
  return (part: ListPart): string | undefined => {
    if (part.kind === 'section') {
      return undefined
    }
    return `${t(part.kind === 'column' ? 'shell.studio.page.column' : 'shell.studio.page.tab')} ${part.index + 1}`
  }
}

/** The names a step adds to a list's path, such as `columns 1` and `Column 2`. */
export function stepLabels(step: PathStep, partLabel: (part: ListPart) => string | undefined): string[] {
  const part = partLabel(step.part)
  return part === undefined ? [`${step.type} ${step.position}`] : [`${step.type} ${step.position}`, part]
}

/**
 * What the live region says after a widget of `type` is inserted at `index` of `list`: the list by its path from the
 * page, and the position counted from 1. `widgets` are the page's widgets before the insert.
 */
export function landedText(
  t: (key: string) => string,
  partLabel: (part: ListPart) => string | undefined,
  widgets: unknown[],
  list: string,
  index: number,
  type: string,
): string {
  const path = widgetLists(widgets).find((item) => item.pointer === list)?.path ?? []
  const label = [t('shell.studio.page.pageList'), ...path.flatMap((step) => stepLabels(step, partLabel))].join(' › ')
  return `${t('shell.studio.palette.inserted')} ${type} · ${label} · ${t('shell.studio.palette.position')} ${index + 1}`
}
