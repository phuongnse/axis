import type { ReactNode } from 'react'
import { ChartWidget } from './ChartWidget'
import { DetailWidget } from './DetailWidget'
import { FormWidget } from './FormWidget'
import { KpiWidget } from './KpiWidget'
import { detailRecordId } from './pageParameters'
import { ProcessInspectorWidget } from './ProcessInspectorWidget'
import type { EntityWidgetMetadata, WidgetMetadata } from './site'
import { TableWidget } from './TableWidget'
import { TaskInboxWidget } from './TaskInboxWidget'

/** What a widget needs to know about the page that holds it. */
export interface PageLeafProps {
  sitePath: string
  pageName: string
  /** The names of the site's navigation pages, in navigation order. */
  pages: readonly string[]
  locale: string
  fallbackLocale: string
  pageValues: Record<string, string>
  recordId: string | null
  returnTo: string
}

// The compiler binds a form or detail widget to an entity, never to a data source.
function hasEntity(widget: WidgetMetadata): widget is EntityWidgetMetadata {
  return widget.entity !== null
}

/** Draws one widget that shows something, with the widget component of its type. The site and the page designer share it. */
export function PageLeaf({
  widget,
  sitePath,
  pageName,
  pages,
  locale,
  fallbackLocale,
  pageValues,
  recordId,
  returnTo,
}: PageLeafProps & { widget: WidgetMetadata }): ReactNode {
  const detailId = detailRecordId(widget, pageValues)
  switch (widget.type) {
    case 'table':
      return <TableWidget sitePath={sitePath} widget={widget} locale={locale} pageValues={pageValues} />
    case 'form':
      return (
        hasEntity(widget) && (
          <FormWidget
            widget={widget}
            sitePath={sitePath}
            pages={pages}
            recordId={recordId}
            returnTo={returnTo}
            locale={locale}
          />
        )
      )
    case 'detail':
      return (
        hasEntity(widget) &&
        detailId !== undefined && (
          <DetailWidget key={detailId} widget={widget} sitePath={sitePath} recordId={detailId} locale={locale} />
        )
      )
    case 'taskInbox':
      return (
        <TaskInboxWidget
          sitePath={sitePath}
          pageName={pageName}
          widget={widget}
          locale={locale}
          fallbackLocale={fallbackLocale}
        />
      )
    case 'processInspector':
      return <ProcessInspectorWidget sitePath={sitePath} pageName={pageName} widget={widget} locale={locale} />
    case 'kpi':
      return <KpiWidget sitePath={sitePath} widget={widget} locale={locale} pageValues={pageValues} />
    case 'chart':
      return <ChartWidget widget={widget} locale={locale} pageValues={pageValues} />
  }
}
