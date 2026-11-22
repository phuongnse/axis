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
import type { FixtureRoute } from './fixtureFetch'
import { IdentitySignIn } from './IdentitySignIn'
import { SignUpPreview } from './SignUpPreview'
import { StatusTagPreview } from './StatusTagPreview'
import { SubmittedOnMount } from './SubmittedOnMount'
import { ClickOnMount, ShellPreview, TypedOnMount } from './ShellPreview'
import {
  studioFixtures,
  typicalEntityId,
  typicalFormId,
  typicalPageId,
  typicalProcessId,
  typicalRevisionId,
} from './studioFixtures'
import {
  application,
  blankDetailWidget,
  categoryId,
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
  missingRequestId,
  missingTaskId,
  loadingRoutes,
  loadingTableWidget,
  instanceId,
  noRowsPath,
  type PreviewFieldType,
  locale,
  processInspectorWidget,
  requestId,
  sectionWidgets,
  shellRequestId,
  shellUserId,
  signUpApplications,
  signUpDocuments,
  signUpRegion,
  siteTexts,
  sitePath,
  tableWidget,
  tabsWidget,
  taskId,
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
    <TableWidget sitePath={sitePath} pageName="Requests" widget={widget} locale={locale} pageValues={{}} />
  ) : (
    <TaskInboxWidget sitePath={sitePath} pageName="Tasks" widget={widget} locale={locale} fallbackLocale={locale} />
  )
}

/** One state of a preview: its caption, what it draws, and the fixture answers only it uses. */
export interface PreviewState {
  caption: string
  draw: () => ReactNode
  routes?: Record<string, FixtureRoute>
  /** Answers on paths the preview's other states answer too, so the state shows alone. */
  fixtures?: Record<string, FixtureRoute>
  /** The address the state starts at. */
  entry?: InitialEntry
  /**
   * The least height of the screen the state draws in. The screen fills the window down to the canvas's padding, and
   * a taller page scrolls inside it.
   */
  frameHeight?: number
}

/** The caption of a state in kebab case, which `&state=` names. */
export function stateSlug(caption: string): string {
  return caption.toLowerCase().replaceAll(/[^a-z0-9]+/g, '-')
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
  return <TableWidget sitePath={sitePath} pageName="Requests" widget={widget} locale={locale} pageValues={{}} />
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

/**
 * A Studio card that opens one resource of the Typical folder in its designer, in the one Typical state. The cards are
 * listed in docs/reference/widget-previews.md#the-cards.
 */
function studioDesignerStates(resourceId: string): readonly PreviewState[] {
  return [
    {
      caption: 'Typical',
      fixtures: studioFixtures.typical,
      entry: `/studio?app=${application}&resource=${resourceId}`,
      draw: () => <ShellPreview />,
      frameHeight: 640,
    },
  ]
}

/** The least height of every `shell` state's frame, so more of each page shows in a short window. */
const shellFrameHeight = 800

/** What a preview draws: its states, each the shared widget component over fixture metadata. */
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
  section: typical(() => <WidgetTree widgets={sectionWidgets} renderLeaf={renderLeaf} />),
  // Loading and the load error look the same in every widget, so they are drawn once, in the table.
  states: [
    { caption: 'Loading', draw: () => requestTable(loadingTableWidget), routes: loadingRoutes },
    { caption: 'Load error', draw: () => requestTable(failingTableWidget), routes: failingRoutes },
  ],
  statusTag: typical(() => <StatusTagPreview />),
  ...Object.fromEntries(
    fieldTypes.map((type) => [
      `field-${type}`,
      type === 'child-collection' ? childCollectionStates : fieldStates(type),
    ]),
  ),
  shell: [
    {
      caption: 'Application site',
      draw: () => <ShellPreview />,
      entry: '/sample/requests',
      frameHeight: shellFrameHeight,
    },
    {
      caption: 'Navigation collapsed',
      draw: () => (
        <ClickOnMount selector='button[aria-controls="shell-sidebar"][aria-expanded="true"]'>
          <ShellPreview />
        </ClickOnMount>
      ),
      entry: '/sample/requests',
      frameHeight: shellFrameHeight,
    },
    {
      caption: 'Form page',
      draw: () => <ShellPreview />,
      entry: { pathname: '/sample/request/new', state: { from: '/sample/requests' } },
      frameHeight: shellFrameHeight,
    },
    {
      caption: 'Default record page',
      draw: () => <ShellPreview />,
      entry: { pathname: `/sample/records/category/${categoryId}`, state: { from: '/sample/requests' } },
      frameHeight: shellFrameHeight,
    },
    {
      caption: 'Saved record',
      draw: () => <ShellPreview />,
      entry: { pathname: `/sample/request/${shellRequestId}`, state: { from: '/sample/requests' } },
      frameHeight: shellFrameHeight,
    },
    {
      caption: 'Saved record with a change',
      draw: () => (
        <TypedOnMount selector="#title" text=" (revised)">
          <ShellPreview />
        </TypedOnMount>
      ),
      entry: { pathname: `/sample/request/${shellRequestId}`, state: { from: '/sample/requests' } },
      frameHeight: shellFrameHeight,
    },
    {
      caption: 'Record not found',
      draw: () => <ShellPreview />,
      entry: { pathname: `/sample/request/${missingRequestId}`, state: { from: '/sample/requests' } },
      frameHeight: shellFrameHeight,
    },
    {
      caption: 'Task',
      draw: () => <ShellPreview />,
      entry: { pathname: `/sample/tasks/${taskId}`, state: { from: '/sample/tasks' } },
      frameHeight: shellFrameHeight,
    },
    {
      caption: 'Task not found',
      draw: () => <ShellPreview />,
      entry: { pathname: `/sample/tasks/${missingTaskId}`, state: { from: '/sample/tasks' } },
      frameHeight: shellFrameHeight,
    },
    { caption: 'Dashboard', draw: () => <ShellPreview />, entry: '/sample/dashboard', frameHeight: shellFrameHeight },
    {
      caption: 'Process instance',
      draw: () => <ShellPreview />,
      entry: { pathname: `/sample/instances/${instanceId}`, state: { from: '/sample/instances' } },
      frameHeight: shellFrameHeight,
    },
    { caption: 'Platform site', draw: () => <ShellPreview />, entry: '/', frameHeight: shellFrameHeight },
    {
      caption: 'App switcher',
      draw: () => (
        <ClickOnMount selector='button[aria-label="Switch to"]'>
          <ShellPreview />
        </ClickOnMount>
      ),
      entry: '/sample/requests',
      frameHeight: shellFrameHeight,
    },
    {
      caption: 'Language and theme',
      draw: () => (
        <ClickOnMount selector='button[aria-label="Language and theme"]'>
          <ShellPreview />
        </ClickOnMount>
      ),
      entry: '/sample/requests',
      frameHeight: shellFrameHeight,
    },
    { caption: 'Account page', draw: () => <ShellPreview />, entry: '/account', frameHeight: shellFrameHeight },
    {
      caption: 'Account notifications',
      draw: () => (
        <ClickOnMount selector='[data-node-key="notifications"] [role="tab"]'>
          <ShellPreview />
        </ClickOnMount>
      ),
      entry: '/account',
      frameHeight: shellFrameHeight,
    },
    { caption: 'Users page', draw: () => <ShellPreview />, entry: '/users', frameHeight: shellFrameHeight },
    {
      caption: 'User page',
      draw: () => <ShellPreview />,
      entry: `/users/${shellUserId}`,
      frameHeight: shellFrameHeight,
    },
  ],
  studio: [
    { caption: 'Typical', fixtures: studioFixtures.typical, entry: `/studio?app=${application}` },
    { caption: 'Empty', fixtures: studioFixtures.empty, entry: '/studio' },
    { caption: 'Loading', fixtures: studioFixtures.loading, entry: '/studio' },
    { caption: 'Failed', fixtures: studioFixtures.failed, entry: '/studio' },
    {
      caption: 'Many rows',
      fixtures: studioFixtures.manyRows,
      entry: `/studio?app=${application}&kind=entity`,
    },
    { caption: 'Long names', fixtures: studioFixtures.longNames, entry: `/studio?app=${application}` },
    { caption: 'Access denied', fixtures: studioFixtures.accessDenied, entry: '/studio' },
    {
      caption: 'Overview with errors and changes not live',
      fixtures: studioFixtures.withErrors,
      entry: `/studio?app=${application}`,
    },
    {
      // Proposed, #4934: the page scrolls in its own frame beside the side panel, and the panel's body under its head.
      caption: 'Problems panel with many problems',
      fixtures: studioFixtures.manyProblems,
      entry: `/studio?app=${application}`,
      clickOnMount: '[data-testid="studio-problems"]',
    },
    {
      caption: 'Overview of a new application',
      fixtures: studioFixtures.newApplication,
      entry: `/studio?app=${application}`,
    },
    {
      caption: 'Entity',
      fixtures: studioFixtures.typical,
      entry: `/studio?app=${application}&resource=${typicalEntityId}`,
    },
    {
      caption: 'Entity with expressions',
      fixtures: studioFixtures.withExpressions,
      entry: `/studio?app=${application}&resource=${typicalEntityId}`,
    },
    { caption: 'Policy matrix', fixtures: studioFixtures.typical, entry: `/studio?app=${application}&view=policies` },
    { caption: 'Releases', fixtures: studioFixtures.typical, entry: `/studio?app=${application}&view=releases` },
    { caption: 'History', fixtures: studioFixtures.typical, entry: `/studio?app=${application}&view=history` },
    {
      caption: 'Revision',
      fixtures: studioFixtures.typical,
      entry: `/studio?app=${application}&view=history&revision=${typicalRevisionId}`,
    },
    {
      caption: 'Release page',
      fixtures: studioFixtures.releasePage,
      entry: `/studio?app=${application}&view=release&release=2`,
    },
  ].map(
    ({
      clickOnMount,
      ...state
    }: {
      caption: string
      fixtures: Record<string, FixtureRoute>
      entry: string
      clickOnMount?: string
    }) => ({
      ...state,
      draw: () =>
        clickOnMount === undefined ? (
          <ShellPreview />
        ) : (
          <ClickOnMount selector={clickOnMount}>
            <ShellPreview />
          </ClickOnMount>
        ),
      frameHeight: 640,
    }),
  ),
  studioPageDesign: studioDesignerStates(typicalPageId),
  studioFormDesign: studioDesignerStates(typicalFormId),
  studioProcessDesign: studioDesignerStates(typicalProcessId),
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
  identitySignIn: [
    { caption: 'Sign in', draw: () => <IdentitySignIn page="signIn" />, frameHeight: 600 },
    { caption: 'Wrong password', draw: () => <IdentitySignIn page="wrongPassword" />, frameHeight: 600 },
    {
      caption: 'Fields left empty',
      draw: () => (
        <SubmittedOnMount>
          <IdentitySignIn page="fieldsEmpty" />
        </SubmittedOnMount>
      ),
      frameHeight: 600,
    },
    { caption: 'Organization sign-in only', draw: () => <IdentitySignIn page="organizationOnly" />, frameHeight: 600 },
    { caption: 'Two-step verification', draw: () => <IdentitySignIn page="twoStep" />, frameHeight: 600 },
    { caption: 'Forgot password', draw: () => <IdentitySignIn page="resetPassword" />, frameHeight: 600 },
    { caption: 'Set password', draw: () => <IdentitySignIn page="updatePassword" />, frameHeight: 600 },
    { caption: 'Add an authenticator', draw: () => <IdentitySignIn page="addAuthenticator" />, frameHeight: 760 },
    {
      caption: 'Choose how to sign in',
      draw: () => <IdentitySignIn page="selectAuthenticator" />,
      frameHeight: 600,
    },
    { caption: 'Update your profile', draw: () => <IdentitySignIn page="updateProfile" />, frameHeight: 720 },
    { caption: 'Terms and privacy', draw: () => <IdentitySignIn page="terms" />, frameHeight: 600 },
    {
      caption: 'Terms not accepted',
      draw: () => (
        <SubmittedOnMount>
          <IdentitySignIn page="terms" />
        </SubmittedOnMount>
      ),
      frameHeight: 600,
    },
    { caption: 'Sign out', draw: () => <IdentitySignIn page="signOut" />, frameHeight: 600 },
    { caption: 'Notice', draw: () => <IdentitySignIn page="notice" />, frameHeight: 600 },
    { caption: 'Error', draw: () => <IdentitySignIn page="error" />, frameHeight: 600 },
    {
      caption: 'Language and theme',
      draw: () => (
        <ClickOnMount selector='button[aria-label="Language and theme"]'>
          <IdentitySignIn page="signIn" />
        </ClickOnMount>
      ),
      frameHeight: 600,
    },
  ],
  signUp: [
    {
      caption: 'Typical',
      draw: () => <SignUpPreview />,
      frameHeight: 1200,
      routes: {
        '/api/sign-up/applications': () => signUpApplications,
        // No challenge, so the page sends the form without a proof of work and solves nothing in the preview.
        '/api/sign-up/challenge': () => new Response(null, { status: 404 }),
        '/api/sign-up/documents': () => signUpDocuments,
        '/api/sign-up/region': () => signUpRegion,
      },
    },
  ],
}

/** The fixture answers that the states of the previews bring, in one record. */
export function stateRoutes(all: Record<string, readonly PreviewState[]>): Record<string, FixtureRoute> {
  return Object.fromEntries(
    Object.values(all).flatMap((states) => states.flatMap((state) => Object.entries(state.routes ?? {}))),
  )
}

/** The answers of the state that the preview shows. */
let chosen: Record<string, FixtureRoute> = {}

/** Makes `state` the one whose `fixtures` answer, before it is drawn. */
export function chooseFixtures(state: PreviewState): void {
  chosen = state.fixtures ?? {}
}

/**
 * The fixture paths that states answer for themselves, each answered by the chosen state. A path the chosen state lacks
 * is logged as a missing fixture and answered with a 404, as a path with no route is.
 */
export function chosenRoutes(all: Record<string, readonly PreviewState[]>): Record<string, FixtureRoute> {
  const paths = Object.values(all).flatMap((states) => states.flatMap((state) => Object.keys(state.fixtures ?? {})))
  return Object.fromEntries(
    paths.map((path) => [
      path,
      (init) => {
        const answer = chosen[path]
        if (answer) {
          return answer(init)
        }
        console.error('No design-system fixture for', path)
        return new Response(null, { status: 404 })
      },
    ]),
  )
}

export const catalogs = [
  { texts: siteTexts, fallbackTexts: siteTexts },
  { texts: platformEnglish, fallbackTexts: platformEnglish },
]
