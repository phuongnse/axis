import type { ChartKind } from '../platform/chartOption'
import studioFixtures from './studioFixtures.json'
import type { CurrentUser } from '../platform/currentUser'
import type { NotificationPage } from '../platform/notifications'
import type { HistoryPage, RecordItem, RecordPage } from '../platform/records'
import type { DeadLetterPage, InstancePage } from '../platform/inspection'
import type {
  ApplicationSite,
  DataSourceMetadata,
  EntityMetadata,
  EntityWidgetMetadata,
  FieldMetadata,
  FieldType,
  FormMetadata,
  PageMetadata,
  PageWidgetMetadata,
  SiteListItem,
  SiteMetadata,
  WidgetMetadata,
} from '../platform/site'
import type { TaskPage } from '../platform/tasks'
import type {
  AddAuthenticatorContext,
  AddAuthenticatorTexts,
  ResetPasswordContext,
  ResetPasswordTexts,
  SignInContext,
  SignInTexts,
  TwoStepContext,
  TwoStepTexts,
  UpdatePasswordContext,
  UpdatePasswordTexts,
  TermsContext,
  TermsTexts,
} from '../identitySignIn/signInContext'

/** The fixture application. Its name is also the marker that tells a design-system build from the production SPA. */
export const application = 'DesignSystemSample'
/** The path of the fixture application's site. */
export const sitePath = 'sample'
export const locale = 'en'

const apiPath = `/api/apps/${application}`
const requestsPath = `${apiPath}/entities/Request/records`
const loadingPath = `${apiPath}/entities/LoadingRequest/records`
const failingPath = `${apiPath}/entities/FailingRequest/records`
const categoriesPath = `${apiPath}/entities/Category/records`
const formStatePath = (form: string) => `${apiPath}/forms/${form}/state`
const tasksPath = `${apiPath}/tasks`
const instancesPath = `${apiPath}/processes/Approve/instances`
const deadLettersPath = `${apiPath}/processes/Approve/dead-letters`
const requestsByStatusPath = `${apiPath}/data-sources/RequestsByStatus/rows`
const requestTotalsPath = `${apiPath}/data-sources/RequestTotals/rows`
const emptyRequestsPath = `${apiPath}/entities/EmptyRequest/records`
const blankRequestsPath = `${apiPath}/entities/BlankRequest/records`
const emptyTasksPath = `${apiPath}/empty-inbox/tasks`
const notificationsPath = `${apiPath}/notifications`
/** The rows of a data source with no rows, for the empty charts and kpi. */
export const noRowsPath = `${apiPath}/data-sources/NoRequests/rows`

export const requestId = '6f1c2a3b-4d5e-4f60-8a71-92b3c4d5e6f7'
const categoryId = '0b9e8d7c-6a5f-4e3d-9c2b-1a0f9e8d7c6b'
const releaseId = '3d2c1b0a-9f8e-4d7c-8b6a-5f4e3d2c1b0a'

function field(name: string, type: FieldType, labelKey: string): FieldMetadata {
  return {
    name,
    type,
    labelKey,
    required: false,
    unique: false,
    computed: false,
    sequence: false,
    creator: false,
    maxLength: null,
    precision: null,
    scale: null,
    min: null,
    max: null,
    minLength: null,
    values: null,
    target: null,
    fields: null,
  }
}

/** A field type the design system previews: all but money and references. */
export type PreviewFieldType = Exclude<FieldType, 'money' | 'references' | 'file'>

/** One field of each type but money and references, which the previews do not draw. The names are the labels' keys without their prefix. */
export const fields: Record<PreviewFieldType, FieldMetadata> = {
  text: { ...field('title', 'text', 'request.title'), maxLength: 200 },
  integer: field('quantity', 'integer', 'request.quantity'),
  decimal: { ...field('amount', 'decimal', 'request.amount'), precision: 18, scale: 2 },
  boolean: field('urgent', 'boolean', 'request.urgent'),
  date: field('neededOn', 'date', 'request.neededOn'),
  'date-time': field('submittedAt', 'date-time', 'request.submittedAt'),
  enum: { ...field('status', 'enum', 'request.status'), values: ['draft', 'submitted', 'approved'] },
  reference: {
    ...field('category', 'reference', 'request.category'),
    target: { entity: 'Category', displayField: 'name', recordsPath: categoriesPath },
  },
  'child-collection': {
    ...field('lines', 'child-collection', 'request.lines'),
    fields: [
      { ...field('description', 'text', 'requestLine.description'), maxLength: 200 },
      field('quantity', 'integer', 'requestLine.quantity'),
      { ...field('unitPrice', 'decimal', 'requestLine.unitPrice'), precision: 18, scale: 2 },
      { ...field('amount', 'decimal', 'requestLine.amount'), precision: 18, scale: 2, computed: true },
      {
        ...field('category', 'reference', 'requestLine.category'),
        target: { entity: 'Category', displayField: 'name', recordsPath: categoriesPath },
      },
    ],
  },
}

export const entity: EntityMetadata = {
  name: 'Request',
  labelKey: 'request.label',
  displayField: 'title',
  recordsPath: requestsPath,
  fields: Object.values(fields),
}

/** The entity cut down to one field, so a preview shows only that field's input. */
export function entityWithField(
  type: PreviewFieldType,
  required = false,
  helpKey: string | null = null,
): EntityMetadata {
  return { ...entity, fields: [{ ...fields[type], required, helpKey }] }
}

const widgetBase: WidgetMetadata = {
  type: 'table',
  entity: null,
  dataSource: null,
  form: null,
  tasksPath: null,
  process: null,
  instancesPath: null,
  deadLettersPath: null,
  actions: [],
  rowActions: [],
  toolbarActions: [],
  parameters: {},
  access: null,
  detail: null,
  kpi: null,
  chart: null,
}

const access = {
  actions: ['read', 'create', 'update', 'delete'] as ('read' | 'create' | 'update' | 'delete')[],
  createFields: entity.fields.map(({ name }) => name),
  updateFields: entity.fields.map(({ name }) => name),
}

export const tableWidget: WidgetMetadata = {
  ...widgetBase,
  type: 'table',
  entity,
  access,
  toolbarActions: [
    {
      type: 'navigate',
      page: 'Request',
      labelKey: 'request.new',
      parameters: {},
      record: null,
      opensForm: true,
      paths: {},
      visibleWhen: null,
    },
  ],
  rowActions: [
    {
      type: 'navigate',
      page: 'Request',
      labelKey: 'request.open',
      parameters: {},
      record: 'id',
      opensForm: true,
      paths: {},
      visibleWhen: null,
    },
    { type: 'delete', labelKey: 'request.delete', recordsPath: requestsPath, visibleWhen: null },
  ],
}

/** The request table with four row actions besides Open, so the table folds them into its "…" menu. */
export const manyActionsTableWidget: WidgetMetadata = {
  ...tableWidget,
  rowActions: [
    ...tableWidget.rowActions.slice(0, 1),
    {
      type: 'navigate',
      page: 'Request',
      labelKey: 'request.edit',
      parameters: {},
      record: 'id',
      opensForm: true,
      paths: {},
      visibleWhen: null,
    },
    {
      type: 'startProcess',
      process: 'Approve',
      labelKey: 'request.submit',
      instancesPath,
      visibleWhen: null,
    },
    ...tableWidget.rowActions.slice(1),
  ],
}

/** The request table over a path whose request never finishes, so it stays loading. */
export const loadingTableWidget: WidgetMetadata = {
  ...tableWidget,
  entity: { ...entity, recordsPath: loadingPath },
}

/** The request table over a path whose request fails, so it shows the load error. */
export const failingTableWidget: WidgetMetadata = {
  ...tableWidget,
  entity: { ...entity, recordsPath: failingPath },
}

/** The request table over a path with no requests, so it shows its empty text. */
export const emptyTableWidget: WidgetMetadata = {
  ...tableWidget,
  entity: { ...entity, recordsPath: emptyRequestsPath },
}

/** The answers only the loading state uses: a request that never settles. */
export const loadingRoutes: Record<string, () => unknown> = {
  [loadingPath]: () => new Promise<never>(() => {}),
}

/** The answers only the load error state uses: a request that fails with status 500. */
export const failingRoutes: Record<string, () => unknown> = {
  [failingPath]: () => new Response(null, { status: 500 }),
}

const form: FormMetadata = {
  name: 'RequestForm',
  sections: [
    {
      name: 'general',
      titleKey: 'request.section.general',
      fields: ['title', 'category', 'status', 'urgent'].map((name) => ({ name, readOnly: false })),
    },
    {
      name: 'details',
      titleKey: 'request.section.details',
      fields: ['quantity', 'amount', 'neededOn', 'submittedAt', 'lines'].map((name) => ({ name, readOnly: false })),
    },
  ],
  steps: [],
}

export const formWidget: EntityWidgetMetadata = {
  ...widgetBase,
  type: 'form',
  entity,
  form,
  access,
  actions: [
    {
      type: 'startProcess',
      process: 'Approve',
      labelKey: 'request.submit',
      instancesPath,
      visibleWhen: null,
    },
  ],
}

/** A detail widget of a request: the form's sections, read-only, with the start button. */
export const detailWidget: EntityWidgetMetadata = { ...formWidget, type: 'detail', detail: { record: 'request' } }

/** The form with a name of its own, so its state and save answers are its own fixtures. */
const namedForm = (
  name: string,
  steps: FormMetadata['steps'] = [],
  sections: FormMetadata['sections'] = form.sections,
): EntityWidgetMetadata => ({
  ...formWidget,
  form: { ...form, name, steps, sections },
})

/** The sections of the form, with each field at a width, so fields of several widths share rows. */
const columnsSections: FormMetadata['sections'] = [
  {
    name: 'general',
    titleKey: 'request.section.general',
    fields: [
      { name: 'title', readOnly: false, width: 8 },
      { name: 'status', readOnly: false, width: 4 },
      { name: 'category', readOnly: false, width: 6 },
      { name: 'urgent', readOnly: false, width: 6 },
    ],
  },
  {
    name: 'details',
    titleKey: 'request.section.details',
    fields: [
      { name: 'quantity', readOnly: false, width: 4 },
      { name: 'amount', readOnly: false, width: 4 },
      { name: 'neededOn', readOnly: false, width: 4 },
      { name: 'submittedAt', readOnly: false, width: 6 },
    ],
  },
]

/** The form whose save the fixture answers with a validation problem, so its messages show. */
export const invalidRequestForm = namedForm('InvalidRequestForm')
/** The form whose state locks the amount, as a form condition does. */
export const lockedRequestForm = namedForm('LockedRequestForm')
/** The form with two wizard steps, the general section and the details section. */
export const wizardRequestForm = namedForm('WizardRequestForm', [
  { titleKey: 'request.section.general', sections: ['general'] },
  { titleKey: 'request.section.details', sections: ['details'] },
])

/** The form with fields of several widths on shared rows, whose save the fixture answers with messages. */
export const columnsRequestForm = namedForm('ColumnsRequestForm', [], columnsSections)

/** The fields of the many-steps form, one in each section and each step. */
const manyStepsFields = [
  fields.text,
  fields.reference,
  fields.enum,
  fields.integer,
  fields.decimal,
  fields.date,
  fields['date-time'],
]

/** The form with seven wizard steps, one field each, so the steps bar shows numbers. */
export const manyStepsRequestForm = namedForm(
  'ManyStepsRequestForm',
  manyStepsFields.map(({ name, labelKey }) => ({ titleKey: labelKey ?? name, sections: [name] })),
  manyStepsFields.map(({ name, labelKey }) => ({
    name,
    titleKey: labelKey ?? name,
    fields: [{ name, readOnly: false }],
  })),
)

const noStateAnswer = () => ({ sections: [], fields: {}, errors: {}, defaults: {} })

/** A save that fails with the given messages, by JSON Pointer. */
const validationFailed = (errors: Record<string, string[]>) => () =>
  new Response(JSON.stringify({ title: 'Validation failed', errors }), {
    status: 400,
    headers: { 'Content-Type': 'application/json' },
  })

/** The answers only the validation errors state uses: no state, and a save that fails on two fields. */
export const invalidFormRoutes: Record<string, () => unknown> = {
  [formStatePath('InvalidRequestForm')]: noStateAnswer,
  [`${requestsPath}?form=InvalidRequestForm`]: validationFailed({
    '/values/title': ['preview.error.title'],
    '/values/quantity': ['preview.error.field'],
  }),
}

/** The answers only the columns state uses: no state, and a save that fails on three fields. */
export const columnsFormRoutes: Record<string, () => unknown> = {
  [formStatePath('ColumnsRequestForm')]: noStateAnswer,
  [`${requestsPath}?form=ColumnsRequestForm`]: validationFailed({
    '/values/title': ['preview.error.title'],
    '/values/amount': ['preview.error.field'],
    '/values/neededOn': ['preview.error.field'],
  }),
}

/** The answers only the read-only state uses: the form state locks the amount. */
export const lockedFormRoutes: Record<string, () => unknown> = {
  [formStatePath('LockedRequestForm')]: () => ({
    sections: [],
    fields: { amount: { visible: true, required: false, readOnly: true } },
    errors: {},
    defaults: {},
  }),
}

/** The answers only the wizard state uses: no state, as the steps are the only difference. */
export const wizardFormRoutes: Record<string, () => unknown> = {
  [formStatePath('WizardRequestForm')]: noStateAnswer,
}

/** The answers only the many-steps state uses: no state, as the number of steps is the only difference. */
export const manyStepsFormRoutes: Record<string, () => unknown> = {
  [formStatePath('ManyStepsRequestForm')]: noStateAnswer,
}

/** The detail widget over a record whose values are all empty, so the Lines table has no rows. */
export const blankDetailWidget: EntityWidgetMetadata = {
  ...detailWidget,
  entity: { ...entity, recordsPath: blankRequestsPath },
}

export const taskInboxWidget: WidgetMetadata = { ...widgetBase, type: 'taskInbox', tasksPath }

export const emptyTaskInboxWidget: WidgetMetadata = { ...taskInboxWidget, tasksPath: emptyTasksPath }

export const processInspectorWidget: WidgetMetadata = {
  ...widgetBase,
  type: 'processInspector',
  process: 'Approve',
  instancesPath,
  deadLettersPath,
}

const requestsByStatus: DataSourceMetadata = {
  name: 'RequestsByStatus',
  rowsPath: requestsByStatusPath,
  entity: 'Request',
  parameters: [],
  pageSize: 100,
  search: false,
  columns: [
    {
      name: 'status',
      type: 'enum',
      labelKey: 'request.status',
      values: ['draft', 'submitted', 'approved'],
      target: null,
    },
    { name: 'requests', type: 'integer', labelKey: null, values: null, target: null },
    { name: 'urgentRequests', type: 'integer', labelKey: null, values: null, target: null },
  ],
}

/** A chart of the requests by status: bars and lines show two counts, and a pie shows one. */
export function chartWidget(kind: ChartKind, rowsPath = requestsByStatusPath): WidgetMetadata {
  const requests = { name: 'requests', labelKey: 'chart.requests' }
  const urgent = { name: 'urgentRequests', labelKey: 'chart.urgentRequests' }
  return {
    ...widgetBase,
    type: 'chart',
    dataSource: { ...requestsByStatus, rowsPath },
    chart: {
      kind,
      titleKey: 'chart.requestsByStatus',
      category: 'status',
      measures: kind === 'pie' ? [requests] : [requests, urgent],
    },
  }
}

const requestTotals: DataSourceMetadata = {
  name: 'RequestTotals',
  rowsPath: requestTotalsPath,
  entity: 'Request',
  parameters: [],
  pageSize: 1,
  search: false,
  columns: [
    { name: 'requests', type: 'integer', labelKey: null, values: null, target: null },
    { name: 'amountSum', type: 'decimal', labelKey: null, values: null, target: null },
  ],
}

/** A kpi tile of the total amount, with the request count as its comparison and no navigate action. */
export const kpiWidget: WidgetMetadata = {
  ...widgetBase,
  type: 'kpi',
  dataSource: requestTotals,
  kpi: {
    measure: 'amountSum',
    labelKey: 'kpi.amount',
    comparison: { measure: 'requests', labelKey: 'kpi.requests' },
    navigate: null,
  },
}

/** The kpi tile over a data source with no rows, so it shows its empty value. */
export const emptyKpiWidget: WidgetMetadata = {
  ...kpiWidget,
  dataSource: { ...requestTotals, rowsPath: noRowsPath },
}

/** A columns widget: the request table at width 8 and a section with the task inbox at width 4. */
export const columnsWidget: PageWidgetMetadata = {
  type: 'columns',
  columns: [
    { width: 8, widgets: [tableWidget] },
    {
      width: 4,
      widgets: [{ type: 'section', titleKey: 'preview.tasks', collapsed: false, widgets: [taskInboxWidget] }],
    },
  ],
}

/** A tabs widget: the request table in one tab and the task inbox in another. */
export const tabsWidget: PageWidgetMetadata = {
  type: 'tabs',
  tabs: [
    { labelKey: 'preview.requests', widgets: [tableWidget] },
    { labelKey: 'preview.tasks', widgets: [taskInboxWidget] },
  ],
}

/** A section widget around the task inbox. */
export const sectionWidget: PageWidgetMetadata = {
  type: 'section',
  titleKey: 'preview.tasks',
  collapsed: false,
  widgets: [taskInboxWidget],
}

const savedRequest = (id: string, title: string, status: string): RecordItem => ({
  id,
  version: 1,
  values: {
    title,
    quantity: '12',
    amount: '1250.50',
    urgent: true,
    neededOn: '2026-03-18',
    submittedAt: '2026-03-04T09:30:00.000000Z',
    status,
    category: categoryId,
    lines: [
      { description: 'Standing desk', quantity: '2', unitPrice: '850.00', amount: '1700.00', category: categoryId },
      { description: 'Desk lamp', quantity: '10', unitPrice: '-5.00', amount: '-50.00', category: categoryId },
    ],
  },
  labels: {
    category: 'Office furniture',
    lines: [{ category: 'Office furniture' }, { category: 'Office furniture' }],
  },
})

export const savedRecord = savedRequest(requestId, 'Standing desks for the new office', 'submitted')

/** The saved request with no line items, which the empty state of the child collection draws. */
export const emptyLinesRecord: RecordItem = {
  ...savedRecord,
  values: { ...savedRecord.values, lines: [] },
  labels: { category: 'Office furniture' },
}

const recordPage: RecordPage = {
  items: [
    savedRecord,
    savedRequest('7a2d3b4c-5e6f-4a71-9b82-a3c4d5e6f708', 'Replacement laptop chargers', 'approved'),
    savedRequest('8b3e4c5d-6f70-4b82-8c93-b4d5e6f70819', 'Whiteboard markers', 'draft'),
    savedRequest(
      '5e6f7a8b-9c0d-4e1f-8a2b-3c4d5e6f7a8b',
      'Replace the worn carpet tiles on the third floor, the meeting room chairs and the kitchen lights before the April safety audit and the board visit',
      'submitted',
    ),
  ],
  page: 1,
  pageSize: 20,
  totalCount: 4,
}

const emptyRecordPage: RecordPage = { items: [], page: 1, pageSize: 20, totalCount: 0 }

const blankRecord: RecordItem = {
  id: requestId,
  version: 1,
  values: {
    title: null,
    quantity: null,
    amount: null,
    urgent: null,
    neededOn: null,
    submittedAt: null,
    status: null,
    category: null,
    lines: [],
  },
  labels: {},
}

const history: HistoryPage = { items: [], page: 1, pageSize: 20, totalCount: 0 }

const taskPage: TaskPage = {
  items: [
    {
      id: '9c4f5d6e-7081-4c93-9da4-c5e6f708192a',
      releaseId,
      process: 'Approve',
      step: 'Review',
      labelKey: 'request.task.review',
      subject: { entity: 'Request', id: requestId, label: 'Standing desks for the new office' },
      dueAt: '2026-03-06T17:00:00.000000Z',
      claimedBy: null,
      overdue: true,
      createdAt: '2026-03-04T09:31:00.000000Z',
    },
    {
      id: 'ad5a6e7f-8192-4da4-aeb5-d6f708192a3b',
      releaseId,
      process: 'Approve',
      step: 'Review',
      labelKey: 'request.task.review',
      subject: { entity: 'Request', id: '8b3e4c5d-6f70-4b82-8c93-b4d5e6f70819', label: 'Whiteboard markers' },
      dueAt: null,
      claimedBy: '3f8e9d0c-1b2a-4c3d-8e4f-5a6b7c8d9e0f',
      overdue: false,
      createdAt: '2026-03-05T14:02:00.000000Z',
    },
  ],
  page: 1,
  pageSize: 20,
  totalCount: 2,
}

const emptyTaskPage: TaskPage = { items: [], page: 1, pageSize: 20, totalCount: 0 }

const instancePage: InstancePage = {
  items: [
    {
      id: '0a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d',
      subjectId: '5e6f7a8b-9c0d-4e1f-8a2b-3c4d5e6f7a8b',
      subjectLabel: 'Whiteboard markers',
      state: 'failed',
      step: 'Notify',
      releaseId,
      releaseNumber: 3,
      createdAt: '2026-03-05T10:00:00.000000Z',
      endedAt: '2026-03-05T10:20:00.000000Z',
    },
    {
      id: 'be6b7f80-92a3-4eb5-bfc6-e708192a3b4c',
      subjectId: requestId,
      subjectLabel: 'Standing desks for the new office',
      state: 'waiting',
      step: 'Review',
      releaseId,
      releaseNumber: 3,
      createdAt: '2026-03-04T09:31:00.000000Z',
      endedAt: null,
    },
    {
      id: 'cf7c8091-a3b4-4fc6-80d7-f8192a3b4c5d',
      subjectId: '7a2d3b4c-5e6f-4a71-9b82-a3c4d5e6f708',
      subjectLabel: 'Replace the worn carpet tiles',
      state: 'completed',
      step: 'Done',
      releaseId,
      releaseNumber: 3,
      createdAt: '2026-03-02T11:15:00.000000Z',
      endedAt: '2026-03-03T08:40:00.000000Z',
    },
  ],
  page: 1,
  pageSize: 20,
  totalCount: 3,
}

const deadLetterPage: DeadLetterPage = {
  items: [
    {
      id: 'd08d91a2-b4c5-4ad7-91e8-092a3b4c5d6e',
      kind: 'event.deliver',
      eventId: 'e19ea2b3-c5d6-4be8-a2f9-1a3b4c5d6e7f',
      triggerId: 'f2afb3c4-d6e7-4cf9-b30a-2b4c5d6e7f80',
      processInstanceId: null,
      emailId: null,
      webhookId: null,
      occurrenceAt: '2026-03-04T09:31:00.000000Z',
      attempts: 5,
      lastError: 'System.InvalidOperationException',
      state: 'open',
      reason: null,
      settledBy: null,
      createdAt: '2026-03-04T09:36:00.000000Z',
      settledAt: null,
      commands: ['redeliver', 'discard'],
    },
  ],
  page: 1,
  pageSize: 20,
  totalCount: 1,
}

const categoryPage: RecordPage = {
  items: [{ id: categoryId, version: 1, values: { name: 'Office furniture' }, labels: {} }],
  page: 1,
  pageSize: 20,
  totalCount: 1,
}

const statusGroup = (id: string, status: string, requests: string, urgentRequests: string): RecordItem => ({
  id,
  version: 1,
  values: { status, requests, urgentRequests },
  labels: {},
})

const requestsByStatusPage: RecordPage = {
  items: [
    statusGroup('1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d', 'draft', '4', '1'),
    statusGroup('2b3c4d5e-6f7a-4b8c-9d0e-1f2a3b4c5d6e', 'submitted', '7', '3'),
    statusGroup('3c4d5e6f-7a8b-4c9d-8e1f-2a3b4c5d6e7f', 'approved', '12', '5'),
  ],
  page: 1,
  pageSize: 100,
  totalCount: 3,
}

const requestTotalsPage: RecordPage = {
  items: [
    {
      id: '4d5e6f70-8b9c-4d0e-8f2a-3b4c5d6e7f80',
      version: 1,
      values: { requests: '23', amountSum: '16945.75' },
      labels: {},
    },
  ],
  page: 1,
  pageSize: 1,
  totalCount: 1,
}

/** The platform site, with one Home entry, so the platform shell has no sidebar. */
export const platformSite: SiteMetadata = {
  name: 'Axis',
  titleKey: 'shell.title',
  locales: { default: locale, fallback: locale, available: [locale] },
  navigation: [{ key: 'home', path: '/', labelKey: 'shell.nav.home', icon: 'home' }],
}

/** The application site, with two navigation pages, so its shell has a sidebar that collapses. */
export const applicationSite: ApplicationSite = {
  path: sitePath,
  titleKey: 'site.title',
  locales: { default: locale, fallback: locale, available: [locale] },
  navigation: [
    { page: 'Requests', labelKey: 'nav.requests', icon: 'list' },
    { page: 'Tasks', labelKey: 'nav.tasks', icon: 'tasks' },
  ],
  notificationsPath,
  legal: { privacyUrl: '#privacy', termsUrl: '#terms' },
}

const requestsPage: PageMetadata = {
  name: 'Requests',
  titleKey: 'page.requests',
  parameters: [],
  widgets: [tableWidget],
}

const requestPage: PageMetadata = {
  name: 'Request',
  titleKey: 'request.new',
  parameters: [],
  widgets: [formWidget],
}

const currentUser: CurrentUser = {
  id: '3f8e9d0c-1b2a-4c3d-8e4f-5a6b7c8d9e0f',
  displayName: 'Alex Morgan',
  roles: ['Author', 'Operator', 'Administrator'],
  studio: true,
  // Proposed: the Studio cards show every action an author who publishes, creates and tests sees.
  studioPublisher: true,
  studioCreate: true,
  studioPreviewHost: 'preview.example.com',
  studioTest: true,
  console: true,
  users: true,
}

const purchaseApplication = {
  name: application,
  titles: { [locale]: 'Purchase requests' },
  descriptions: { [locale]: "Runs the company's purchases, from the request to approval and ordering." },
}
const serviceApplication = { name: 'ServiceDesk', titles: { [locale]: 'Service desk' }, descriptions: null }

/** Three sites in two applications, for the platform home page's cards. */
const listedSites: SiteListItem[] = [
  {
    path: sitePath,
    titleKey: 'site.title',
    titles: { [locale]: 'Purchase requests' },
    descriptions: { [locale]: 'Request purchases and take them through approval.' },
    application: purchaseApplication,
  },
  {
    path: 'archive',
    titleKey: 'site.title',
    titles: { [locale]: 'Archived requests' },
    descriptions: null,
    application: purchaseApplication,
  },
  {
    path: 'desk',
    titleKey: 'site.title',
    titles: { [locale]: 'Service desk' },
    descriptions: null,
    application: serviceApplication,
  },
]

const notificationPage: NotificationPage = { items: [], page: 1, pageSize: 10, totalCount: 0, unreadCount: 2 }

/** The texts of the identity provider's sign-in page, in English. */
export const identitySignInTexts: SignInTexts = {
  continueWith: 'Continue with {provider}',
  providerOnly: 'Your organization signs in only with {provider}.',
  or: 'or',
  username: 'Email or username',
  password: 'Password',
  usernameRequired: 'Enter your email or username.',
  passwordRequired: 'Enter your password.',
  forgotPassword: 'Forgot password?',
  keepSignedIn: 'Keep me signed in',
  signIn: 'Sign in',
}

/** What the identity provider's sign-in page draws in the design system. */
export const identitySignInContext: SignInContext = {
  page: 'signIn',
  title: 'Sign in',
  realmName: 'Contoso',
  providers: [{ alias: 'contoso', label: 'Contoso ID', loginUrl: '#contoso' }],
  accounts: true,
  username: 'alex.morgan@contoso.example',
  loginAction: '#sign-in',
  rememberMe: true,
  forgotPasswordUrl: '#forgot-password',
  preferences: {
    locale: 'en',
    locales: [
      { tag: 'en', url: '#en' },
      { tag: 'vi', url: '#vi' },
    ],
    texts: {
      label: 'Language and theme',
      language: 'Language',
      theme: 'Theme',
      system: 'System',
      light: 'Light',
      dark: 'Dark',
    },
  },
  legal: {
    links: {
      privacy: '#privacy',
      terms: '#terms',
      licences: '#licences',
    },
    texts: {
      label: 'Legal',
      privacy: 'Privacy',
      terms: 'Terms',
      licences: 'Open source licences',
    },
  },
  texts: identitySignInTexts,
}

/** The message of the sign-in page after a wrong password. */
export const identityInvalid = 'Invalid email, username or password.'

/** The texts of the two-step verification page, in English. */
export const identityTwoStepTexts: TwoStepTexts = {
  title: 'Two-step verification',
  message: 'Enter the 6-digit code from your authenticator app.',
  code: 'Verification code',
  verify: 'Verify',
  tryAnotherWay: 'Try another way',
  backToSignIn: 'Back to sign in',
}

/** What the identity provider's two-step verification page draws in the design system. */
export const identityTwoStepContext: TwoStepContext = {
  page: 'twoStep',
  title: identityTwoStepTexts.title,
  realmName: identitySignInContext.realmName,
  loginAction: '#verify',
  restartUrl: '#sign-in',
  tryAnotherWay: true,
  preferences: identitySignInContext.preferences,
  legal: identitySignInContext.legal,
  texts: identityTwoStepTexts,
}

/** The texts of the "Forgot password?" page, in English. */
export const identityResetPasswordTexts: ResetPasswordTexts = {
  title: 'Reset your password',
  message: 'Enter your email or username and we will send you a link to set a new password.',
  username: 'Email or username',
  usernameRequired: 'Enter your email or username.',
  sendLink: 'Send link',
  backToSignIn: 'Back to sign in',
}

/** What the identity provider's "Forgot password?" page draws in the design system. */
export const identityResetPasswordContext: ResetPasswordContext = {
  page: 'resetPassword',
  title: identityResetPasswordTexts.title,
  realmName: identitySignInContext.realmName,
  username: identitySignInContext.username,
  loginAction: '#reset',
  loginUrl: '#sign-in',
  preferences: identitySignInContext.preferences,
  legal: identitySignInContext.legal,
  texts: identityResetPasswordTexts,
}

/** The texts of the page that sets a password, in English. */
export const identityUpdatePasswordTexts: UpdatePasswordTexts = {
  title: 'Set your password',
  message: 'Choose a new password for your account.',
  newPassword: 'New password',
  confirmPassword: 'Confirm password',
  newPasswordRequired: 'Enter a new password.',
  passwordsDiffer: 'The passwords do not match.',
  setPassword: 'Set password',
}

/** What the identity provider's page that sets a password draws in the design system. */
export const identityUpdatePasswordContext: UpdatePasswordContext = {
  page: 'updatePassword',
  title: identityUpdatePasswordTexts.title,
  realmName: identitySignInContext.realmName,
  loginAction: '#set-password',
  preferences: identitySignInContext.preferences,
  legal: identitySignInContext.legal,
  texts: identityUpdatePasswordTexts,
}

/** Proposed: the texts of the terms page, in English. */
export const identityTermsTexts: TermsTexts = {
  title: 'Terms and privacy',
  message: 'Before you continue, read and accept the terms that apply to your account.',
  agreeBoth: 'I have read and agree to the {terms} and the {privacy}.',
  agreeTerms: 'I have read and agree to the {terms}.',
  agreePrivacy: 'I have read and agree to the {privacy}.',
  termsLink: 'Terms of service',
  privacyLink: 'Privacy policy',
  agreeRequired: 'Tick the box to accept before you continue.',
  accept: 'Continue',
  decline: 'Decline and sign out',
}

/** Proposed: what the terms page draws in the design system. */
export const identityTermsContext: TermsContext = {
  page: 'terms',
  title: identityTermsTexts.title,
  realmName: identitySignInContext.realmName,
  loginAction: '#terms',
  preferences: identitySignInContext.preferences,
  legal: identitySignInContext.legal,
  texts: identityTermsTexts,
}

/** The texts of the page that adds an authenticator, in English. */
export const identityAddAuthenticatorTexts: AddAuthenticatorTexts = {
  title: 'Add an authenticator',
  message: 'Scan the QR code with your authenticator app, then enter the 6-digit code it shows.',
  qrCode: 'QR code for your authenticator app',
  manualKey: 'Cannot scan it? Enter this key instead.',
  code: 'Verification code',
  codeRequired: 'Enter the 6-digit code from your authenticator app.',
  deviceName: 'Device name',
  deviceNameRequired: 'Enter a name for this device.',
  submit: 'Add authenticator',
}

/** A small PNG with a QR-like pattern. It shows the layout of the page and does not scan. */
export const identityQrCode =
  'iVBORw0KGgoAAAANSUhEUgAAAGQAAABkCAAAAABVicqIAAAEYUlEQVR42u2a25LbMAxD+f8/jV5siQeUrERZTR86yUMnu43NUCQBEN7QP3jFN8g3yH8SJJav35/788/1Nv5ed/24vG4viK5b6g4R15UvQuwHuT55Rbs+3vM6FaQdkq6s+t1fXDcNMi3afa8Wwg/Rbza57v0geb+7JJmVjgZptY5WeQQ/EkS9o6630YrzaSZDU7Hs6rOhGmR23VYQhd32DqnQySCt0ixRtKk5FiTa9N2T3+Kpfu5HmdyAZcCFsT+TSQfJntXdaSdrAiwRB/1oTZBIjmYoDmdCfsPvztUkMUVt2CWr0qEWZpAeQTqbSVh1+qB8VpM5CoPm88pwjP4xCjeeB4pNGPBnQfpwk0ky1qlMkgobrJSavB1kqYkw7tLs0H6sVgRc7CU6rLvaeKOT9UYqn+iunEJRVG4EseHqwJRcBXrP2ifXtG4T8GGi6oMqJ8tLSmy01QmS9NX/v4eYHVcn1vbFFUMXCS2QY68eOQeqfP9MRNDU/StTeyWpR+1pJSS0os2OCyqEWh6ypOqhsHxxQZZoOC7X1CJtZBmCNQrI4n7cBR68uzjX2UkhYhfFKr5rlzFhRZrVpP8Q6FDIB3aUhDaLoBTPg3yoCTsv0NeJ9UjEmyQbYHlcIxZzIHKM8miyhgQ2NuvYwtYtQity2TKdHRDeXR8TBcY9HrfPnaoeAkaWJ+Nr33hGBpAmQDn4XNwLwQc6JUXsAlYqRAC7iCjQqGVHkrXZdE5MzTGMdTcITPQMXMbMCh9sEKoSHyUMg9GM93hMawINjXPrDRZkHKKWd1w58knhC+OwiVJyuXEjJpMapuCKk1but4kYbnhkW8tFBskrYMKMhQ8uHtjehw0Lq2m4kQPkiKeaBIsQhhSZpKnJCjAxByycFiogkQK9MYFgOSvQ4WXmS01ATCBBKl/Xp/AnuO3L+be+ddOPXOV1d6KRbWDcVx9ghQ1mpO2cAdOQ2JWE92x0EpvApfYbohVwMw2SsGVpCGLCD5AriCmMQDBPcWt94HhfasrohStW1B1s1VtQMWx+bwju7BbogICvCtdTVE1bQaqfwhq7xnMi2gxi6IcABsbeI7Fpo5OfqB1RKnMVyu7wrpsKRQcWjnJ7WOHxJCRWbqrJzIB9K+564tBo2ywQISUhMFDzUWhsHZfMSTcYk2yXk/l725Ztrb8Fmq0v+0FUHKNhXXOJsJj4pWVL8gLRO0W4w7vv3Blq4JZcd7kCrrprYT7LMsICZjtBYGv7xEZHI3MvIzrTqd4tvDG96bRw9w6ySNrOBHu+SCVOhP44b7cmpG7mRlKGoopnPlkdV33gJx87IJZVZqeF/RGmwzocGSqxT4LQDvAnpsRh4tcm1PuewgeB/D5hEDRfsV8/Aix+SJRNjgbZ0x6/zsSXfNg0rrSolbVPWmWaiSJUzbKFc093Fb8izFSTfB3ScuJfSiK/J0bVzMU1/S4NbmCVb8ZOzxq0yvsy1Xxg2lE0D4s5Et8/KfkG+QZ5//ULrcxm3U9sM1QAAAAASUVORK5CYII='

/** What the page that adds an authenticator draws in the design system. */
export const identityAddAuthenticatorContext: AddAuthenticatorContext = {
  page: 'addAuthenticator',
  title: identityAddAuthenticatorTexts.title,
  realmName: identitySignInContext.realmName,
  loginAction: '#new-authenticator',
  qrCode: identityQrCode,
  manualKey: 'JBSW Y3DP EHPK 3PXP',
  secret: 'fixture-secret',
  deviceNameRequired: false,
  preferences: identitySignInContext.preferences,
  legal: identitySignInContext.legal,
  texts: identityAddAuthenticatorTexts,
}

/** The site texts, in English. */
export const siteTexts: Record<string, string> = {
  'request.label': 'Request',
  'request.title': 'Title',
  'request.quantity': 'Quantity',
  'request.amount': 'Amount',
  'request.urgent': 'Urgent',
  'request.neededOn': 'Needed on',
  'request.submittedAt': 'Submitted at',
  'request.status': 'Status',
  'request.category': 'Category',
  'request.lines': 'Lines',
  'requestLine.description': 'Description',
  'requestLine.quantity': 'Quantity',
  'requestLine.unitPrice': 'Unit price',
  'requestLine.amount': 'Amount',
  'requestLine.category': 'Category',
  'request.section.general': 'General',
  'request.section.details': 'Details',
  'request.new': 'New request',
  'request.open': 'Open',
  'request.edit': 'Edit',
  'request.delete': 'Delete',
  'request.submit': 'Submit for approval',
  'request.task.review': 'Review the request',
  'chart.requestsByStatus': 'Requests by status',
  'chart.requests': 'Requests',
  'chart.urgentRequests': 'Urgent requests',
  'kpi.amount': 'Total amount',
  'kpi.requests': 'Requests',
  'preview.requests': 'Requests',
  'preview.tasks': 'Tasks',
  'preview.help': 'A short note that explains the field.',
  'preview.error.field': 'This value is not valid.',
  'preview.error.title': 'Enter a title.',
  'preview.error.unitPrice': 'Unit price cannot be negative.',
  'site.title': 'Purchase requests',
  'nav.requests': 'Requests',
  'nav.tasks': 'Tasks',
  'page.requests': 'Requests',
}

/** The API paths that the fixtures answer. A request outside them is not a fixture's to answer. */
export const fixtureScopes = [
  `${apiPath}/`,
  '/api/sites',
  '/api/me',
  '/api/about',
  '/health/ready',
  '/api/studio',
] as const

/**
 * The answers of the fixture application's APIs, by path or by path and query. The page's own
 * queries (paging, sorting) are answered by the path alone.
 */

const studioBlankApp = { name: 'Inventory', titles: { en: 'Inventory', vi: 'Kho hàng' }, descriptions: null }

/** A resource of a Studio fixture folder. */
const studioItem = (id: string, kind: string, name: string, file: string) => ({
  id,
  kind,
  name,
  file,
  version: id,
  usedBy: 0,
})

const leaveEntity = '5f0c6a1e-2b7d-4c1a-9e3f-0a8b7c6d5e41'
const leavePage = '5f0c6a1e-2b7d-4c1a-9e3f-0a8b7c6d5e42'
const leaveResources = [
  studioItem('5f0c6a1e-2b7d-4c1a-9e3f-0a8b7c6d5e40', 'application', 'LeaveRequests', 'application.json'),
  studioItem(leaveEntity, 'entity', 'LeaveRequest', 'entities/leave-request.json'),
  studioItem(leavePage, 'page', 'Requests', 'pages/requests.json'),
  studioItem('5f0c6a1e-2b7d-4c1a-9e3f-0a8b7c6d5e43', 'form', 'LeaveRequestForm', 'forms/leave-request-form.json'),
  studioItem('5f0c6a1e-2b7d-4c1a-9e3f-0a8b7c6d5e44', 'site', 'LeaveRequests', 'sites/leave-requests.json'),
  studioItem('5f0c6a1e-2b7d-4c1a-9e3f-0a8b7c6d5e45', 'role', 'Employee', 'roles/employee.json'),
  studioItem(
    '5f0c6a1e-2b7d-4c1a-9e3f-0a8b7c6d5e46',
    'identityProvider',
    'accounts',
    'identity-providers/accounts.json',
  ),
]
const leaveDiagnostics = [
  {
    code: 'AXC0027',
    severity: 'warning',
    message: "The text key 'pages.requests.empty' is missing from locale 'vi'; locale 'en' has it.",
    file: 'texts/vi.json',
    path: '/texts',
    resourceId: null,
  },
  {
    code: 'AXC0021',
    severity: 'error',
    message: "The widget's entity 'LeaveRequests' names no loaded entity.",
    file: 'pages/requests.json',
    path: '/widgets/0/entity',
    resourceId: leavePage,
  },
]
const leaveReleases = {
  active: { releaseId: '01a18bb7-ef25-7408-8a42-9716776650a2', number: 2, contentHash: 'a2' },
  folderContentHash: 'a3',
  items: [
    {
      releaseId: '01a18bb7-ef25-7408-8a42-9716776650a2',
      number: 2,
      contentHash: 'a2',
      activatedAt: '2026-10-27T16:05:00+00:00',
      actor: 'author',
      actorName: 'Alex Morgan',
      label: 'Half days',
      note: null,
      requestId: null,
    },
  ],
}

const blankResources = [
  studioItem('7d2e4f60-1a3b-4c5d-8e9f-0b1c2d3e4f50', 'application', 'Inventory', 'application.json'),
  studioItem('7d2e4f60-1a3b-4c5d-8e9f-0b1c2d3e4f51', 'site', 'Inventory', 'sites/inventory.json'),
  studioItem('7d2e4f60-1a3b-4c5d-8e9f-0b1c2d3e4f52', 'page', 'Home', 'pages/home.json'),
  studioItem(
    '7d2e4f60-1a3b-4c5d-8e9f-0b1c2d3e4f53',
    'identityProvider',
    'accounts',
    'identity-providers/accounts.json',
  ),
  studioItem('7d2e4f60-1a3b-4c5d-8e9f-0b1c2d3e4f54', 'text', 'en', 'texts/en.json'),
]

/** The answers Studio loads for one application's folder. */
function studioFolder(name: string, items: unknown[], diagnostics: unknown[], releases: unknown) {
  const base = `/api/studio/apps/${name}`
  const names = {
    locales: ['en'],
    entities: [],
    dataSources: [],
    processes: [],
    rules: [],
    connectors: [],
    events: [],
    roles: [],
    sequences: [],
    forms: [],
    pages: [],
    texts: [],
  }
  return {
    [`${base}/resources`]: () => ({ items }),
    [`${base}/diagnostics`]: () => ({ diagnostics }),
    [`${base}/names`]: () => names,
    [`${base}/releases`]: () => releases,
  }
}

export const routes: Record<string, () => unknown> = {
  [requestsPath]: () => recordPage,
  [`${requestsPath}/${requestId}`]: () => savedRecord,
  [`${requestsPath}/${requestId}/history`]: () => history,
  [categoriesPath]: () => categoryPage,
  [tasksPath]: () => taskPage,
  [instancesPath]: () => instancePage,
  [deadLettersPath]: () => deadLetterPage,
  [requestsByStatusPath]: () => requestsByStatusPage,
  [requestTotalsPath]: () => requestTotalsPage,
  [emptyRequestsPath]: () => emptyRecordPage,
  [`${blankRequestsPath}/${requestId}`]: () => blankRecord,
  [emptyTasksPath]: () => emptyTaskPage,
  [noRowsPath]: () => emptyRecordPage,
  [`/api/sites/${sitePath}/texts/${locale}`]: () => ({ texts: siteTexts }),
  [`/api/sites/${sitePath}`]: () => applicationSite,
  [`/api/sites/${sitePath}/pages/requests`]: () => requestsPage,
  [`/api/sites/${sitePath}/pages/request`]: () => requestPage,
  [formStatePath('RequestForm')]: noStateAnswer,
  [notificationsPath]: () => notificationPage,
  '/api/sites': () => ({ sites: listedSites }),
  '/api/me': () => currentUser,
  '/api/about': () => ({ version: '0.1.0' }),
  '/health/ready': () => new Response(null, { status: 200 }),
  // Proposed: Studio's answers for the purchase request sample, as the local stack gave them, with a short release history.
  ...Object.fromEntries(Object.entries(studioFixtures).map(([path, body]) => [path, () => body])),
  // Proposed: two more applications for Studio's overview: one with errors and changes that are not live yet, and one
  // just created, with nothing but what a blank application holds.
  '/api/studio/apps': () => ({ items: [...studioFixtures['/api/studio/apps'].items, studioBlankApp] }),
  ...studioFolder('LeaveRequests', leaveResources, leaveDiagnostics, leaveReleases),
  ...studioFolder('Inventory', blankResources, [], { active: null, folderContentHash: 'b1', items: [] }),
}
