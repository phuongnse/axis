import type { ReactNode } from 'react'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { ChartWidget } from '../platform/ChartWidget'
import { DetailWidget } from '../platform/DetailWidget'
import { FormWidget } from '../platform/FormWidget'
import { KpiWidget } from '../platform/KpiWidget'
import { ProcessInspectorWidget } from '../platform/ProcessInspectorWidget'
import type { EntityWidgetMetadata, WidgetMetadata } from '../platform/site'
import { TableWidget } from '../platform/TableWidget'
import { TaskInboxWidget } from '../platform/TaskInboxWidget'
import { WidgetTree } from '../platform/WidgetTree'
import { FieldPreview } from './FieldPreview'
import { FormPageShellPreview, PlatformShellPreview, SiteShellPreview } from './ShellPreview'
import type { PreviewSession } from './shellFixtures'
import { SubmittedOnMount } from './SubmittedOnMount'
import {
  blankDetailWidget,
  chartWidget,
  columnsWidget,
  detailWidget,
  emptyKpiWidget,
  emptyLinesRecord,
  emptyTableWidget,
  emptyTaskInboxWidget,
  failingRoutes,
  failingTableWidget,
  fields,
  invalidFormRoutes,
  invalidRequestForm,
  kpiWidget,
  lockedFormRoutes,
  lockedRequestForm,
  loadingRoutes,
  loadingTableWidget,
  noRowsPath,
  type PreviewFieldType,
  locale,
  processInspectorWidget,
  requestId,
  sectionWidget,
  siteTexts,
  sitePath,
  tableWidget,
  tabsWidget,
  taskInboxWidget,
  wizardFormRoutes,
  wizardRequestForm,
} from './fixtures'

const fieldTypes: readonly PreviewFieldType[] = [
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

/** One state of a preview: its caption, what it draws, and the fixture answers only it uses. */
export interface PreviewState {
  caption: string
  draw: () => ReactNode
  routes?: Record<string, () => unknown>
  /** The address the state opens at, for a state that reads its path or query. */
  at?: string
}

/** A preview with one state, the typical one. */
function typical(draw: () => ReactNode): readonly PreviewState[] {
  return [{ caption: 'Typical', draw }]
}

/** A preview of a data widget: with the fixture data, and with none. */
function withAndWithoutData(data: () => ReactNode, empty: () => ReactNode): readonly PreviewState[] {
  return [
    { caption: 'With data', draw: data },
    { caption: 'Empty', draw: empty },
  ]
}

/** The request table over the given metadata, with the same props as the table preview. */
function requestTable(widget: WidgetMetadata): ReactNode {
  return <TableWidget sitePath={sitePath} widget={widget} locale={locale} pageValues={{}} />
}

function inbox(widget: WidgetMetadata): ReactNode {
  return (
    <TaskInboxWidget sitePath={sitePath} pageName="Tasks" widget={widget} locale={locale} fallbackLocale={locale} />
  )
}

function detail(widget: EntityWidgetMetadata): ReactNode {
  return <DetailWidget widget={widget} sitePath={sitePath} recordId={requestId} locale={locale} />
}

/** The request form over the given metadata, for a new record or the saved one, as the form preview draws it. */
function requestForm(widget: EntityWidgetMetadata, recordId: string | null): ReactNode {
  return (
    <FormWidget widget={widget} sitePath={sitePath} pages={[]} recordId={recordId} returnTo="/ds" locale={locale} />
  )
}

/** The states of a field's preview: typical, required, read-only, and a validation error under the input. */
function fieldStates(type: PreviewFieldType): readonly PreviewState[] {
  const problem = { [`/values/${fields[type].name}`]: ['preview.error.field'] }
  return [
    { caption: 'Typical', draw: () => <FieldPreview type={type} /> },
    { caption: 'Required', draw: () => <FieldPreview type={type} required /> },
    { caption: 'Read-only', draw: () => <FieldPreview type={type} readOnly /> },
    { caption: 'Validation error', draw: () => <FieldPreview type={type} problem={problem} /> },
  ]
}

const childCollectionProblem = { '/values/lines/1/unitPrice': ['preview.error.unitPrice'] }

/** The line items' states: reference and computed cells, a cell error, read-only, and no rows. */
const childCollectionStates: readonly PreviewState[] = [
  { caption: 'Reference and computed cells', draw: () => <FieldPreview type="child-collection" /> },
  { caption: 'Cell error', draw: () => <FieldPreview type="child-collection" problem={childCollectionProblem} /> },
  { caption: 'Read-only', draw: () => <FieldPreview type="child-collection" readOnly /> },
  { caption: 'Empty', draw: () => <FieldPreview type="child-collection" record={emptyLinesRecord} /> },
]

/** What a preview draws: its states, each the shared widget component over fixture metadata. */
export const previews: Record<string, readonly PreviewState[]> = {
  table: withAndWithoutData(
    () => requestTable(tableWidget),
    () => requestTable(emptyTableWidget),
  ),
  form: [
    {
      caption: 'Validation errors',
      draw: () => <SubmittedOnMount>{requestForm(invalidRequestForm, null)}</SubmittedOnMount>,
      routes: invalidFormRoutes,
    },
    {
      caption: 'Read-only by a condition',
      draw: () => requestForm(lockedRequestForm, requestId),
      routes: lockedFormRoutes,
    },
    { caption: 'Wizard steps', draw: () => requestForm(wizardRequestForm, requestId), routes: wizardFormRoutes },
  ],
  taskInbox: withAndWithoutData(
    () => inbox(taskInboxWidget),
    () => inbox(emptyTaskInboxWidget),
  ),
  processInspector: typical(() => (
    <ProcessInspectorWidget sitePath={sitePath} pageName="Instances" widget={processInspectorWidget} locale={locale} />
  )),
  detail: withAndWithoutData(
    () => detail(detailWidget),
    () => detail(blankDetailWidget),
  ),
  ...Object.fromEntries(
    (['bar', 'line', 'pie'] as const).map((kind) => [
      `chart-${kind}`,
      withAndWithoutData(
        () => <ChartWidget widget={chartWidget(kind)} locale={locale} />,
        () => <ChartWidget widget={chartWidget(kind, noRowsPath)} locale={locale} />,
      ),
    ]),
  ),
  kpi: withAndWithoutData(
    () => <KpiWidget sitePath={sitePath} widget={kpiWidget} locale={locale} pageValues={{}} />,
    () => <KpiWidget sitePath={sitePath} widget={emptyKpiWidget} locale={locale} pageValues={{}} />,
  ),
  columns: typical(() => <WidgetTree widgets={[columnsWidget]} renderLeaf={renderLeaf} />),
  tabs: typical(() => <WidgetTree widgets={[tabsWidget]} renderLeaf={renderLeaf} />),
  section: typical(() => <WidgetTree widgets={[sectionWidget]} renderLeaf={renderLeaf} />),
  // Loading and the load error look the same in every widget, so they are drawn once, in the table.
  states: [
    { caption: 'Loading', draw: () => requestTable(loadingTableWidget), routes: loadingRoutes },
    { caption: 'Load error', draw: () => requestTable(failingTableWidget), routes: failingRoutes },
  ],
  ...Object.fromEntries(
    fieldTypes.map((type) => [
      `field-${type}`,
      type === 'child-collection' ? childCollectionStates : fieldStates(type),
    ]),
  ),
  shell: [
    { caption: 'Application site', draw: () => <SiteShellPreview />, at: `/${sitePath}/requests` },
    {
      caption: 'Navigation collapsed',
      draw: () => <SiteShellPreview collapsed />,
      at: `/${sitePath}/requests`,
    },
    { caption: 'Form page', draw: () => <FormPageShellPreview />, at: `/${sitePath}/requests/new` },
    { caption: 'Platform site', draw: () => <PlatformShellPreview />, at: '/' },
  ],
  signIn: [
    { caption: 'Signed out', draw: () => <PlatformShellPreview />, at: '/' },
    { caption: 'Sign-in refused', draw: () => <PlatformShellPreview />, at: '/?signIn=failed' },
  ],
}

/** The previews that draw nobody signed in. Every other preview draws a signed-in user. */
export const previewSessions: Partial<Record<string, PreviewSession>> = { signIn: 'signedOut' }

/** The fixture answers that the states of the previews bring, in one record. */
export function stateRoutes(all: Record<string, readonly PreviewState[]>): Record<string, () => unknown> {
  return Object.assign({}, ...Object.values(all).flatMap((states) => states.map((state) => state.routes ?? {})))
}

export const catalogs = [
  { texts: siteTexts, fallbackTexts: siteTexts },
  { texts: platformEnglish, fallbackTexts: platformEnglish },
]
