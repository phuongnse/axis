import type { ReactNode } from 'react'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { ChartWidget } from '../platform/ChartWidget'
import { DetailWidget } from '../platform/DetailWidget'
import { FormWidget } from '../platform/FormWidget'
import { KpiWidget } from '../platform/KpiWidget'
import { ProcessInspectorWidget } from '../platform/ProcessInspectorWidget'
import type { FieldType, WidgetMetadata } from '../platform/site'
import { TableWidget } from '../platform/TableWidget'
import { TaskInboxWidget } from '../platform/TaskInboxWidget'
import { WidgetTree } from '../platform/WidgetTree'
import {
  chartWidget,
  columnsWidget,
  detailWidget,
  fieldWidget,
  formWidget,
  kpiWidget,
  locale,
  processInspectorWidget,
  requestId,
  sectionWidget,
  siteTexts,
  sitePath,
  tableWidget,
  tabsWidget,
  taskInboxWidget,
} from './fixtures'

const fieldTypes: readonly FieldType[] = [
  'text',
  'integer',
  'decimal',
  'boolean',
  'date',
  'date-time',
  'enum',
  'reference',
  'child-collection',
]

/** Draws the table or the task inbox of a container preview, with the same fixtures as their own previews. */
function renderLeaf(widget: WidgetMetadata): ReactNode {
  return widget.type === 'table' ? (
    <TableWidget sitePath={sitePath} widget={widget} locale={locale} pageValues={{}} />
  ) : (
    <TaskInboxWidget sitePath={sitePath} pageName="Tasks" widget={widget} locale={locale} fallbackLocale={locale} />
  )
}

/** What a preview draws: the shared widget component over fixture metadata. */
export const previews: Record<string, () => ReactNode> = {
  table: () => <TableWidget sitePath={sitePath} widget={tableWidget} locale={locale} pageValues={{}} />,
  form: () => (
    <FormWidget
      widget={formWidget}
      sitePath={sitePath}
      pages={[]}
      recordId={requestId}
      returnTo="/ds"
      locale={locale}
    />
  ),
  taskInbox: () => (
    <TaskInboxWidget
      sitePath={sitePath}
      pageName="Tasks"
      widget={taskInboxWidget}
      locale={locale}
      fallbackLocale={locale}
    />
  ),
  processInspector: () => (
    <ProcessInspectorWidget sitePath={sitePath} pageName="Instances" widget={processInspectorWidget} locale={locale} />
  ),
  detail: () => <DetailWidget widget={detailWidget} sitePath={sitePath} recordId={requestId} locale={locale} />,
  ...Object.fromEntries(
    (['bar', 'line', 'pie'] as const).map((kind) => [
      `chart-${kind}`,
      () => <ChartWidget widget={chartWidget(kind)} locale={locale} />,
    ]),
  ),
  kpi: () => <KpiWidget sitePath={sitePath} widget={kpiWidget} locale={locale} pageValues={{}} />,
  columns: () => <WidgetTree widgets={[columnsWidget]} renderLeaf={renderLeaf} />,
  tabs: () => <WidgetTree widgets={[tabsWidget]} renderLeaf={renderLeaf} />,
  section: () => <WidgetTree widgets={[sectionWidget]} renderLeaf={renderLeaf} />,
  ...Object.fromEntries(
    fieldTypes.map((type) => [
      `field-${type}`,
      () => (
        <FormWidget
          widget={fieldWidget(type)}
          sitePath={sitePath}
          pages={[]}
          recordId={requestId}
          returnTo="/ds"
          locale={locale}
        />
      ),
    ]),
  ),
}

export const catalogs = [
  { texts: siteTexts, fallbackTexts: siteTexts },
  { texts: platformEnglish, fallbackTexts: platformEnglish },
]
