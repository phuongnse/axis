import type { ReactNode } from 'react'
import type { InitialEntry } from 'react-router'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { SignInCard } from '../pages/HomePage'
import { ChartWidget } from '../platform/ChartWidget'
import { DetailWidget } from '../platform/DetailWidget'
import { FormWidget } from '../platform/FormWidget'
import { KpiWidget } from '../platform/KpiWidget'
import { ProcessInspectorWidget } from '../platform/ProcessInspectorWidget'
import { SignInPromptDialog } from '../platform/SignInPrompt'
import type { EntityWidgetMetadata, WidgetMetadata } from '../platform/site'
import { TableWidget } from '../platform/TableWidget'
import { TaskInboxWidget } from '../platform/TaskInboxWidget'
import { WidgetTree } from '../platform/WidgetTree'
import { FieldPreview } from './FieldPreview'
import { IdentitySignIn } from './IdentitySignIn'
import { SubmittedOnMount } from './SubmittedOnMount'
import { ClickOnMount, ShellPreview } from './ShellPreview'
import {
  blankDetailWidget,
  chartWidget,
  columnsFormRoutes,
  columnsRequestForm,
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
  manyActionsTableWidget,
  manyStepsFormRoutes,
  manyStepsRequestForm,
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
  /** The address the state starts at. */
  entry?: InitialEntry
  /** The height of the frame the state draws in, with its menus and dialogs inside it. */
  frameHeight?: number
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

/** The states of a field's preview: editable, read-only, and the required mark, a help text and a message together. */
function fieldStates(type: PreviewFieldType): readonly PreviewState[] {
  const problem = { [`/values/${fields[type].name}`]: ['preview.error.field'] }
  return [
    { caption: 'Editable', draw: () => <FieldPreview type={type} /> },
    { caption: 'Read-only', draw: () => <FieldPreview type={type} readOnly /> },
    {
      caption: 'Required, help text and error',
      draw: () => <FieldPreview type={type} required helpKey="preview.help" problem={problem} />,
    },
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
// Proposed: the purchase request sample's resources that the Studio cards open.
const studioApp = 'PurchaseRequests'
const studioEntity = 'd9ccbf89-5004-4d52-8885-2ee377b7caa5'
const studioPage = 'e0ee63b0-ff11-4f06-95ee-e73922f2c1a9'
const studioForm = '77c7c183-7283-46a7-b56f-5cf5cec518aa'
const studioProcess = '4b60c03e-02d0-4329-80c6-75f972190399'
const studioResource = (id: string) => `/studio?app=${studioApp}&resource=${id}`

export const previews: Record<string, readonly PreviewState[]> = {
  table: [
    ...withAndWithoutData(
      () => requestTable(tableWidget),
      () => requestTable(emptyTableWidget),
    ),
    { caption: 'Many row actions', draw: () => requestTable(manyActionsTableWidget) },
  ],
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
    {
      caption: 'Fields in columns, with errors',
      draw: () => <SubmittedOnMount>{requestForm(columnsRequestForm, null)}</SubmittedOnMount>,
      routes: columnsFormRoutes,
    },
    { caption: 'Many steps', draw: () => requestForm(manyStepsRequestForm, requestId), routes: manyStepsFormRoutes },
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
    { caption: 'Application site', draw: () => <ShellPreview />, entry: '/sample/requests', frameHeight: 600 },
    {
      caption: 'Navigation collapsed',
      draw: () => (
        <ClickOnMount selector='button[aria-controls="shell-sidebar"][aria-expanded="true"]'>
          <ShellPreview />
        </ClickOnMount>
      ),
      entry: '/sample/requests',
      frameHeight: 600,
    },
    {
      caption: 'Form page',
      draw: () => <ShellPreview />,
      entry: { pathname: '/sample/request/new', state: { from: '/sample/requests' } },
      frameHeight: 600,
    },
    { caption: 'Platform site', draw: () => <ShellPreview />, entry: '/', frameHeight: 600 },
    {
      caption: 'App switcher',
      draw: () => (
        <ClickOnMount selector='button[aria-label="Switch to"]'>
          <ShellPreview />
        </ClickOnMount>
      ),
      entry: '/sample/requests',
      frameHeight: 600,
    },
    {
      caption: 'Language and theme',
      draw: () => (
        <ClickOnMount selector='button[aria-label="Language and theme"]'>
          <ShellPreview />
        </ClickOnMount>
      ),
      entry: '/sample/requests',
      frameHeight: 600,
    },
  ],
  signIn: [
    {
      caption: 'Sign-in refused',
      draw: () => <SignInCard state="refused" />,
      entry: '/?signIn=failed',
      frameHeight: 480,
    },
    { caption: 'Signed out', draw: () => <SignInCard state="signedOut" />, entry: '/?signedOut', frameHeight: 480 },
    {
      caption: 'Session ended',
      draw: () => (
        <>
          <ShellPreview />
          <SignInPromptDialog open />
        </>
      ),
      entry: '/sample/requests',
      frameHeight: 480,
    },
  ],
  // Proposed: a Studio group of cards, drawn from Studio's own pages over the purchase request sample.
  studio: [
    { caption: 'Applications', draw: () => <ShellPreview />, entry: '/studio', frameHeight: 600 },
    // Proposed: an application opens on its overview, which says what to do next.
    { caption: 'Overview', draw: () => <ShellPreview />, entry: `/studio?app=${studioApp}`, frameHeight: 600 },
    {
      caption: 'Overview with errors and changes not live',
      draw: () => <ShellPreview />,
      entry: '/studio?app=LeaveRequests',
      frameHeight: 600,
    },
    {
      caption: 'Overview of a new application',
      draw: () => <ShellPreview />,
      entry: '/studio?app=Inventory',
      frameHeight: 600,
    },
    {
      caption: 'Entities',
      draw: () => <ShellPreview />,
      entry: `/studio?app=${studioApp}&view=kind&kind=entity`,
      frameHeight: 600,
    },
    { caption: 'Entity', draw: () => <ShellPreview />, entry: studioResource(studioEntity), frameHeight: 600 },
    {
      caption: 'Releases',
      draw: () => <ShellPreview />,
      entry: `/studio?app=${studioApp}&view=releases`,
      frameHeight: 600,
    },
    {
      caption: 'Policy matrix',
      draw: () => <ShellPreview />,
      entry: `/studio?app=${studioApp}&view=policies`,
      frameHeight: 600,
    },
  ],
  studioPageDesign: [
    { caption: 'Page', draw: () => <ShellPreview />, entry: studioResource(studioPage), frameHeight: 600 },
  ],
  studioFormDesign: [
    { caption: 'Form', draw: () => <ShellPreview />, entry: studioResource(studioForm), frameHeight: 600 },
  ],
  studioProcessDesign: [
    { caption: 'Process', draw: () => <ShellPreview />, entry: studioResource(studioProcess), frameHeight: 600 },
  ],
  identitySignIn: [
    { caption: 'Sign in', draw: () => <IdentitySignIn page="signIn" />, frameHeight: 600 },
    { caption: 'Wrong password', draw: () => <IdentitySignIn page="wrongPassword" />, frameHeight: 600 },
    {
      // Proposed: each error keeps the whole gap before the next field.
      caption: 'Fields left empty',
      draw: () => (
        <SubmittedOnMount>
          <IdentitySignIn page="emptyFields" />
        </SubmittedOnMount>
      ),
      frameHeight: 600,
    },
    { caption: 'Organization sign-in only', draw: () => <IdentitySignIn page="organizationOnly" />, frameHeight: 600 },
    { caption: 'Two-step verification', draw: () => <IdentitySignIn page="twoStep" />, frameHeight: 600 },
    { caption: 'Forgot password', draw: () => <IdentitySignIn page="resetPassword" />, frameHeight: 600 },
    { caption: 'Set password', draw: () => <IdentitySignIn page="updatePassword" />, frameHeight: 600 },
    { caption: 'Add an authenticator', draw: () => <IdentitySignIn page="addAuthenticator" />, frameHeight: 760 },
  ],
}

/** The fixture answers that the states of the previews bring, in one record. */
export function stateRoutes(all: Record<string, readonly PreviewState[]>): Record<string, () => unknown> {
  return Object.fromEntries(
    Object.values(all).flatMap((states) => states.flatMap((state) => Object.entries(state.routes ?? {}))),
  )
}

export const catalogs = [
  { texts: siteTexts, fallbackTexts: siteTexts },
  { texts: platformEnglish, fallbackTexts: platformEnglish },
]
