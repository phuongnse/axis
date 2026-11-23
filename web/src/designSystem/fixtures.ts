import type { ChartKind } from '../platform/chartOption'
import type { CurrentUser } from '../platform/currentUser'
import type { LegalLinks } from '../platform/LegalLine'
import type { NotificationPage } from '../platform/notifications'
import type { OwnAi, OwnAiState } from '../platform/ownAi'
import type { OwnAuthenticators } from '../platform/ownAuthenticators'
import type { OwnNotificationPreferences } from '../platform/ownNotificationPreferences'
import type { OwnSessions } from '../platform/ownSessions'
import type { HistoryPage, RecordItem, RecordPage } from '../platform/records'
import type { DeadLetterPage, InstanceDetail, InstancePage } from '../platform/inspection'
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
import type { TaskItem, TaskPage } from '../platform/tasks'
import type {
  AddAuthenticatorContext,
  AddAuthenticatorTexts,
  ErrorContext,
  FrameTexts,
  FrontChannelSignOutContext,
  NoticeContext,
  PageExpiredContext,
  ResetPasswordContext,
  ResetPasswordTexts,
  SamlPostContext,
  SelectAuthenticatorContext,
  SelectAuthenticatorTexts,
  SignInContext,
  SignInTexts,
  SignOutContext,
  TermsContext,
  TermsTexts,
  TwoStepContext,
  TwoStepTexts,
  UpdatePasswordContext,
  UpdatePasswordTexts,
  UpdateProfileContext,
  UpdateProfileTexts,
} from '../identitySignIn/signInContext'
import type { UserList, UserRoles } from '../platform/users'
import type { FixtureRoute } from './fixtureFetch'

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
export const categoryId = '0b9e8d7c-6a5f-4e3d-9c2b-1a0f9e8d7c6b'
/** The id of the saved request that the `shell` preview opens, whose Urgent box is not checked. */
export const shellRequestId = '9d4f5a6b-7c81-4d93-a4b5-c6d7e8f90a1b'
/** The first person of the users page, whose page the `shell` preview opens. */
export const shellUserId = 'b5e6f708-1c2d-4e3f-8a4b-5c6d7e8f9a0b'
/** A well-formed request id that no record has, which the fixtures answer `404` for. */
export const missingRequestId = 'c2d3e4f5-a6b7-4c8d-9e0f-1a2b3c4d5e6f'
/** The first task of the inbox, which the `shell` preview opens. */
export const taskId = '9c4f5d6e-7081-4c93-9da4-c5e6f708192a'
/** A well-formed task id that no task has, which the fixtures answer `404` for. */
export const missingTaskId = 'e4f5a6b7-c8d9-4e0f-8a1b-2c3d4e5f6a7b'
/** The waiting instance that the `shell` preview opens, which the instance list shows as its second row. */
export const instanceId = 'be6b7f80-92a3-4eb5-bfc6-e708192a3b4c'
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

/** The entity of a category, which the fixture site names no page for, so its records open on the default record page. */
const categoryEntity: EntityMetadata = {
  name: 'Category',
  labelKey: 'category.label',
  displayField: 'name',
  recordsPath: categoriesPath,
  fields: [{ ...field('name', 'text', 'category.name'), maxLength: 200 }],
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
  priorities: {},
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

/** The kpi tile with a navigate action, so the whole tile is a link to the Requests page. */
export const linkedKpiWidget: WidgetMetadata = {
  ...kpiWidget,
  kpi: {
    measure: 'requests',
    labelKey: 'kpi.requests',
    comparison: null,
    navigate: {
      type: 'navigate',
      page: 'Requests',
      labelKey: 'kpi.showRequests',
      parameters: {},
      record: null,
      opensForm: false,
      paths: {},
      visibleWhen: null,
    },
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

/** A section that does not fold, around the task inbox, then one marked `collapsed`, around the request table. */
export const sectionWidgets: PageWidgetMetadata[] = [
  { type: 'section', titleKey: 'preview.tasks', collapsed: false, widgets: [taskInboxWidget] },
  { type: 'section', titleKey: 'preview.requests', collapsed: true, widgets: [tableWidget] },
]

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

/**
 * The saved request as the `shell` preview opens it, with Urgent unchecked. A checked box in dark mode is a listed miss
 * of tests/e2e/contrast-baseline.json for the form previews, and the shell's states are not listed there.
 */
const shellRecord: RecordItem = {
  ...savedRecord,
  id: shellRequestId,
  values: { ...savedRecord.values, urgent: false },
}

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
      id: taskId,
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
      id: instanceId,
      subjectId: requestId,
      subjectLabel: 'Standing desks for the new office',
      state: 'waiting',
      step: 'confirmReceipt',
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
    { page: 'Dashboard', labelKey: 'nav.dashboard', icon: 'dashboard' },
    { page: 'Instances', labelKey: 'nav.instances', icon: 'process' },
  ],
  notificationsPath,
  // Request has a form page. Category has none, so it opens on the default record page.
  recordPages: { Request: `/${sitePath}/request`, Category: `/${sitePath}/records/category` },
}

const requestsPage: PageMetadata = {
  name: 'Requests',
  titleKey: 'page.requests',
  parameters: [],
  widgets: [tableWidget],
}

/** The Approvals page: the process inspector over the Approve process, whose instances open on their own page. */
const instancesPage: PageMetadata = {
  name: 'Instances',
  titleKey: 'page.instances',
  parameters: [],
  widgets: [processInspectorWidget],
}

/** A dashboard: two kpi tiles side by side, one of them a link, and a bar chart under them. */
const dashboardPage: PageMetadata = {
  name: 'Dashboard',
  titleKey: 'page.dashboard',
  parameters: [],
  widgets: [
    {
      type: 'columns',
      columns: [
        { width: 6, widgets: [kpiWidget] },
        { width: 6, widgets: [linkedKpiWidget] },
      ],
    },
    chartWidget('bar'),
  ],
}

const tasksPage: PageMetadata = {
  name: 'Tasks',
  titleKey: 'page.tasks',
  parameters: [],
  widgets: [taskInboxWidget],
}

const requestPage: PageMetadata = {
  name: 'Request',
  titleKey: 'request.label',
  parameters: [],
  widgets: [formWidget],
}

/** The default record page of a category: one form over every field, as the record page route answers it. */
const categoryRecordPage: PageMetadata = {
  name: 'records/Category',
  titleKey: 'category.label',
  parameters: [],
  widgets: [
    {
      ...formWidget,
      entity: categoryEntity,
      form: null,
      actions: [],
      access: {
        actions: ['read', 'create', 'update', 'delete'],
        createFields: ['name'],
        updateFields: ['name'],
      },
    },
  ],
}

const categoryRecord: RecordItem = {
  id: categoryId,
  version: 1,
  values: { name: 'Office furniture' },
  labels: {},
}

const currentUser: CurrentUser = {
  id: '3f8e9d0c-1b2a-4c3d-8e4f-5a6b7c8d9e0f',
  displayName: 'Alex Morgan',
  roles: ['Author', 'Operator', 'Administrator'],
  studio: true,
  studioPublisher: false,
  studioCreate: false,
  studioPreviewHost: null,
  studioTest: false,
  studioAi: false,
  studioDeveloper: false,
  console: true,
  users: true,
  tenantStatus: 'active',
  billingUrl: null,
  tenantExports: false,
}

/** The texts of the instance's release: its process's title and its steps' labels, in English. */
const approveTexts: Record<string, string> = {
  'process.approve.title': 'Purchase approval',
  'process.approve.checkBudget': 'Check the budget',
  'process.approve.withinBudget': 'Within budget',
  'process.approve.review': 'Review the request',
  'process.approve.createOrder': 'Create the order',
  'process.approve.arrangeDelivery': 'Arrange delivery',
  'process.approve.confirmReceipt': 'Confirm receipt',
  'process.approve.done': 'Done',
  'process.approve.outcome.approve': 'Approve',
  'process.approve.outcome.reject': 'Reject',
  'process.delivery.title': 'Delivery',
}

/**
 * The waiting instance that the Process instance state opens. Its path has a decision that took its fallback, a review
 * that a person completed, an order call that succeeded on its third attempt, a sub-process that completed, and an open
 * task, so History has each kind of run. Its Data tab reads that it had no input and has not ended.
 */
const instanceDetail: InstanceDetail = {
  id: instanceId,
  process: 'Approve',
  processTitleKey: 'process.approve.title',
  subjectId: requestId,
  subjectEntity: 'Request',
  subjectLabel: 'Standing desks for the new office',
  releaseId,
  releaseNumber: 3,
  state: 'waiting',
  step: 'confirmReceipt',
  timerDueAt: null,
  createdAt: '2026-03-04T09:31:00.000000Z',
  endedAt: null,
  startedBy: { kind: 'person', name: currentUser.displayName, id: currentUser.id, via: 'request.submit' },
  input: null,
  result: null,
  studio: null,
  graph: {
    start: 'checkBudget',
    steps: [
      {
        name: 'checkBudget',
        type: 'decision',
        labelKey: 'process.approve.checkBudget',
        event: null,
        eventLabelKey: null,
        transitions: [
          {
            next: 'createOrder',
            on: '0',
            branch: 'withinBudget',
            labelKey: 'process.approve.withinBudget',
            after: null,
          },
          { next: 'review', on: 'otherwise', branch: null, labelKey: null, after: null },
        ],
      },
      {
        name: 'review',
        type: 'task',
        labelKey: 'process.approve.review',
        event: null,
        eventLabelKey: null,
        transitions: [
          {
            next: 'createOrder',
            on: 'approve',
            branch: null,
            labelKey: 'process.approve.outcome.approve',
            after: null,
          },
          { next: 'done', on: 'reject', branch: null, labelKey: 'process.approve.outcome.reject', after: null },
        ],
      },
      {
        name: 'createOrder',
        type: 'operation',
        labelKey: 'process.approve.createOrder',
        event: null,
        eventLabelKey: null,
        transitions: [{ next: 'arrangeDelivery', on: null, branch: null, labelKey: null, after: null }],
      },
      {
        name: 'arrangeDelivery',
        type: 'subProcess',
        labelKey: 'process.approve.arrangeDelivery',
        event: null,
        eventLabelKey: null,
        transitions: [{ next: 'confirmReceipt', on: null, branch: null, labelKey: null, after: null }],
      },
      {
        name: 'confirmReceipt',
        type: 'task',
        labelKey: 'process.approve.confirmReceipt',
        event: null,
        eventLabelKey: null,
        transitions: [
          { next: 'done', on: 'approve', branch: null, labelKey: 'process.approve.outcome.approve', after: null },
        ],
      },
      {
        name: 'done',
        type: 'end',
        labelKey: 'process.approve.done',
        event: null,
        eventLabelKey: null,
        transitions: [],
      },
    ],
  },
  path: [
    {
      step: 'checkBudget',
      startedAt: '2026-03-04T09:31:00.000000Z',
      finishedAt: '2026-03-04T09:31:00.010000Z',
      decision: 'otherwise',
      output: null,
      error: null,
      problem: null,
      traceId: null,
    },
    {
      step: 'review',
      startedAt: '2026-03-04T09:31:00.020000Z',
      finishedAt: '2026-03-04T09:31:00.030000Z',
      decision: null,
      output: { state: 'waiting', taskId: 'c5d6e7f8-0a1b-4c2d-8e3f-4a5b6c7d8e9f' },
      error: null,
      problem: null,
      traceId: null,
    },
    {
      step: 'review',
      startedAt: '2026-03-04T15:12:00.000000Z',
      finishedAt: '2026-03-04T15:12:00.010000Z',
      decision: 'approve',
      output: { state: 'completed', taskId: 'c5d6e7f8-0a1b-4c2d-8e3f-4a5b6c7d8e9f' },
      error: null,
      problem: null,
      traceId: null,
    },
    {
      step: 'createOrder',
      startedAt: '2026-03-04T15:12:00.020000Z',
      finishedAt: '2026-03-04T15:12:03.000000Z',
      decision: null,
      output: { state: 'succeeded' },
      error: null,
      problem: null,
      traceId: null,
    },
    {
      step: 'arrangeDelivery',
      startedAt: '2026-03-04T15:12:03.100000Z',
      finishedAt: '2026-03-04T15:20:00.050000Z',
      decision: null,
      output: { state: 'completed', childId: 'e7f8a9b0-2c3d-4e4f-8a5b-6c7d8e9f0a1b' },
      error: null,
      problem: null,
      traceId: null,
    },
    {
      step: 'confirmReceipt',
      startedAt: '2026-03-04T15:20:00.100000Z',
      finishedAt: '2026-03-04T15:20:00.120000Z',
      decision: null,
      output: { state: 'waiting', taskId: 'd6e7f8a9-1b2c-4d3e-9f4a-5b6c7d8e9f0a' },
      error: null,
      problem: null,
      traceId: null,
    },
  ],
  tasks: [
    {
      id: 'c5d6e7f8-0a1b-4c2d-8e3f-4a5b6c7d8e9f',
      step: 'review',
      assigneeKind: 'role',
      assignee: 'Manager',
      assigneeId: currentUser.id,
      state: 'completed',
      outcome: 'approve',
      completedBy: currentUser.displayName,
      completedById: currentUser.id,
      createdAt: '2026-03-04T09:31:00.020000Z',
      dueAt: '2026-03-05T17:00:00.000000Z',
      completedAt: '2026-03-04T15:12:00.000000Z',
    },
    {
      id: 'd6e7f8a9-1b2c-4d3e-9f4a-5b6c7d8e9f0a',
      step: 'confirmReceipt',
      assigneeKind: 'role',
      assignee: 'Operator',
      assigneeId: null,
      state: 'open',
      outcome: null,
      completedBy: null,
      completedById: null,
      createdAt: '2026-03-04T15:20:00.120000Z',
      dueAt: '2026-03-06T17:00:00.000000Z',
      completedAt: null,
    },
  ],
  calls: [
    {
      id: 'f8a9b0c1-3d4e-4f5a-9b6c-7d8e9f0a1b2c',
      kind: 'connector',
      step: 'createOrder',
      connector: 'ERP',
      operation: 'createPurchaseOrder',
      state: 'succeeded',
      attempts: [
        {
          kind: 'attempt',
          number: 1,
          startedAt: '2026-03-04T15:12:00.100000Z',
          finishedAt: '2026-03-04T15:12:01.000000Z',
          outcome: 'retryable',
          httpStatus: 503,
          error: null,
          problem: null,
          traceId: null,
        },
        {
          kind: 'attempt',
          number: 2,
          startedAt: '2026-03-04T15:12:01.500000Z',
          finishedAt: '2026-03-04T15:12:02.000000Z',
          outcome: 'retryable',
          httpStatus: 503,
          error: null,
          problem: null,
          traceId: null,
        },
        {
          kind: 'attempt',
          number: 3,
          startedAt: '2026-03-04T15:12:02.500000Z',
          finishedAt: '2026-03-04T15:12:03.000000Z',
          outcome: 'succeeded',
          httpStatus: 201,
          error: null,
          problem: null,
          traceId: null,
          response: {
            status: 201,
            headers: { 'Content-Type': 'application/json' },
            body: '{"orderId":"PO-2201"}',
            truncated: false,
          },
        },
      ],
      input: { item: 'Standing desks', quantity: 4, amount: '1250.50' },
      inputHidden: false,
      request: {
        method: 'POST',
        url: 'https://erp.example.com/api/purchase-orders',
        query: {},
        headers: { 'Content-Type': 'application/json' },
        body: '{"item":"Standing desks","quantity":4,"amount":"1250.50"}',
        truncated: false,
      },
      resolvable: false,
    },
  ],
  parent: null,
  subProcesses: [
    {
      id: 'e7f8a9b0-2c3d-4e4f-8a5b-6c7d8e9f0a1b',
      process: 'Delivery',
      processTitleKey: 'process.delivery.title',
      step: 'arrangeDelivery',
      state: 'completed',
      createdAt: '2026-03-04T15:12:03.250000Z',
      endedAt: '2026-03-04T15:20:00.000000Z',
    },
  ],
  commands: ['cancel'],
}

/**
 * The first task of the inbox as the task page reads it: open, claimed by the signed-in person and overdue. Its subject
 * is the shell's request, with Urgent unchecked, as `shellRecord` says.
 */
const openTask: TaskItem = {
  ...taskPage.items[0],
  subject: { entity: 'Request', id: shellRequestId, label: shellRecord.values.title as string },
  instanceId: '5a6b7c8d-9e0f-4a1b-8c2d-3e4f5a6b7c8d',
  state: 'open',
  assignee: { user: null, role: 'Operator' },
  claimedBy: currentUser.id,
  overdue: true,
  commands: ['release', 'reassign'],
  reassignRoles: [],
  outcomes: [
    { name: 'approve', labelKey: 'request.outcome.approve' },
    { name: 'reject', labelKey: 'request.outcome.reject' },
  ],
  form: { ...formWidget.form!, entity: formWidget.entity! },
  completedAt: null,
  completedBy: null,
  outcome: null,
}

const ownAuthenticators: OwnAuthenticators = {
  account: true,
  authenticators: [
    { id: '7a1f0c2e-3b4d-4e5f-8a6b-7c8d9e0f1a2b', label: 'Phone', createdAt: '2026-10-02T09:15:00Z' },
    { id: '8b2e1d3f-4c5a-4f6b-9b7c-8d9e0f1a2b3c', label: 'Tablet', createdAt: '2026-11-04T14:30:00Z' },
  ],
}

/**
 * Proposed (#3926): the account page's AI, as `GET /api/me/ai` would answer it, with no AI connected. No API gives it yet.
 * The provider is the installation's `anthropic` binding.
 */
const ownAi: OwnAi = {
  providers: [{ id: 'anthropic', name: 'Anthropic' }],
  connection: null,
}

/** Proposed (#3926): a change, a connect or a remove of the person's AI answers with its status alone, so 204 is enough. */
const ownAiChanged = (init?: RequestInit) => init?.method === 'PUT' || init?.method === 'DELETE'

/**
 * Proposed (#3926): the person's AI, with no AI connected, or with an Anthropic key connected in the key's `state`, for
 * the AI tab's states. Each state gives its own, so the path is in no shared route.
 */
export function ownAiRoute(state?: OwnAiState): FixtureRoute {
  return (init) =>
    ownAiChanged(init)
      ? new Response(null, { status: 204 })
      : { ...ownAi, connection: state ? { provider: 'anthropic', ending: 'x7Qa', state } : null }
}

/**
 * Proposed (#3926): a connect whose key the provider refuses at the agent host's test, answered as `422` problem details
 * with the `reason` `key-refused`, as a quota refusal names its reason.
 */
export const ownAiConnectRefused: FixtureRoute = (init) =>
  init?.method === 'PUT'
    ? new Response(JSON.stringify({ title: 'The provider refused the key.', status: 422, reason: 'key-refused' }), {
        status: 422,
        headers: { 'Content-Type': 'application/problem+json' },
      })
    : ownAi

const ownSessions: OwnSessions = {
  endAllowed: true,
  sessions: [
    {
      id: '9c3f2e4a-5d6b-4a7c-8c8d-9e0f1a2b3c4d',
      signedInAt: '2026-11-19T07:45:00Z',
      lastRequestAt: '2026-11-19T08:10:00Z',
      current: true,
    },
    {
      id: 'ad4a3f5b-6e7c-4b8d-9d9e-0f1a2b3c4d5e',
      signedInAt: '2026-11-17T18:20:00Z',
      lastRequestAt: '2026-11-18T21:05:00Z',
      current: false,
    },
  ],
}

const shellUsers: UserList = {
  page: 1,
  pageSize: 20,
  totalCount: 3,
  items: [
    {
      id: shellUserId,
      displayName: 'Priya Raman',
      email: 'priya.raman@example.com',
      provider: null,
      status: 'active',
      lastSignedInAt: '2026-11-19T07:45:00Z',
    },
    {
      id: 'c6f70819-2d3e-4f40-9b5c-6d7e8f9a0b1c',
      displayName: 'Tomas Berg',
      email: 'tomas.berg@example.com',
      provider: 'google',
      status: 'active',
      lastSignedInAt: '2026-11-18T16:20:00Z',
    },
    {
      id: 'd708192a-3e4f-4051-8c6d-7e8f9a0b1c2d',
      displayName: 'Lena Fischer',
      email: 'lena.fischer@example.com',
      provider: null,
      status: 'invited',
      lastSignedInAt: null,
    },
  ],
}

const shellApplicationId = '5b0d1f52-8d0e-4a47-9c1b-2f7e5a9d4c01'

const shellUserRoles: UserRoles = {
  displayName: 'Priya Raman',
  email: 'priya.raman@example.com',
  given: [
    { applicationId: shellApplicationId, role: 'buyer', declared: true },
    { applicationId: null, role: 'author', declared: true },
  ],
  applications: [{ id: shellApplicationId, name: 'Purchase requests', roles: ['approver', 'buyer'] }],
  platformRoles: ['author', 'publisher', 'developer', 'operator', 'administrator'],
}

// Process has the daily digest on, so the hour shows. Authoring requests have no email.
const ownNotificationPreferences: OwnNotificationPreferences = {
  sources: {
    task: { channels: { application: true, email: true }, digest: false },
    mention: { channels: { application: true, email: true }, digest: false },
    process: { channels: { application: true, email: true }, digest: true },
    authoringRequest: { channels: { application: true, email: false }, digest: false },
  },
  digestHour: 8,
  timeZone: 'UTC',
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
const tenantNoticePage: NotificationPage = { items: [], page: 1, pageSize: 10, totalCount: 0, unreadCount: 0 }

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

/** The texts of the identity provider's "Language and theme" gear, in English. */
export const identityFrameTexts: FrameTexts = {
  languageAndTheme: 'Language and theme',
  language: 'Language',
  theme: 'Theme',
  system: 'System',
  light: 'Light',
  dark: 'Dark',
}

/** The legal line under the identity provider's cards in the design system, in English. */
export const identityLegal: LegalLinks = {
  privacyUrl: '#privacy',
  termsUrl: '#terms',
  licencesUrl: '#licences',
  texts: { legal: 'Legal', privacy: 'Privacy', terms: 'Terms', licences: 'Licences' },
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
  locale: 'en',
  locales: [
    { tag: 'en', label: 'English', url: '#en' },
    { tag: 'vi', label: 'Tiếng Việt', url: '#vi' },
  ],
  frameTexts: identityFrameTexts,
  legal: identityLegal,
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
  locale: identitySignInContext.locale,
  locales: identitySignInContext.locales,
  frameTexts: identityFrameTexts,
  legal: identityLegal,
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
  locale: identitySignInContext.locale,
  locales: identitySignInContext.locales,
  frameTexts: identityFrameTexts,
  legal: identityLegal,
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
  cancel: 'Cancel',
}

/** What the identity provider's page that sets a password draws in the design system. */
export const identityUpdatePasswordContext: UpdatePasswordContext = {
  page: 'updatePassword',
  title: identityUpdatePasswordTexts.title,
  realmName: identitySignInContext.realmName,
  loginAction: '#set-password',
  locale: identitySignInContext.locale,
  locales: identitySignInContext.locales,
  frameTexts: identityFrameTexts,
  legal: identityLegal,
  texts: identityUpdatePasswordTexts,
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
  cancel: 'Cancel',
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
  locale: identitySignInContext.locale,
  locales: identitySignInContext.locales,
  frameTexts: identityFrameTexts,
  legal: identityLegal,
  texts: identityAddAuthenticatorTexts,
}

/** The texts of the page behind "Try another way", in English. */
export const identitySelectAuthenticatorTexts: SelectAuthenticatorTexts = {
  title: 'Choose how to sign in',
  message: 'Pick another way to verify that it is you.',
  backToSignIn: 'Back to sign in',
}

/** What the identity provider's page behind "Try another way" draws in the design system. */
export const identitySelectAuthenticatorContext: SelectAuthenticatorContext = {
  page: 'selectAuthenticator',
  title: identitySelectAuthenticatorTexts.title,
  realmName: identitySignInContext.realmName,
  loginAction: '#select-authenticator',
  restartUrl: '#restart',
  methods: [
    { id: 'otp', label: 'Authenticator app', help: 'Enter a verification code from your authenticator app.' },
    { id: 'password', label: 'Password', help: 'Sign in with your password.' },
  ],
  locale: identitySignInContext.locale,
  locales: identitySignInContext.locales,
  frameTexts: identityFrameTexts,
  legal: identityLegal,
  texts: identitySelectAuthenticatorTexts,
}

/** The texts of the pages that update a profile, in English. */
export const identityUpdateProfileTexts: UpdateProfileTexts = {
  submit: 'Save',
  fieldRequired: '{field} is required.',
}

/** What the identity provider's update profile page draws in the design system. */
export const identityUpdateProfileContext: UpdateProfileContext = {
  page: 'updateProfile',
  title: 'Update your profile',
  realmName: identitySignInContext.realmName,
  notice: 'You need to update your user profile to activate your account.',
  loginAction: '#update-profile',
  fields: [
    {
      name: 'email',
      label: 'Email',
      value: 'alex.morgan@contoso.example',
      required: true,
      readOnly: false,
      autoComplete: 'email',
    },
    { name: 'firstName', label: 'First name', value: 'Alex', required: true, readOnly: false },
    { name: 'lastName', label: 'Last name', value: 'Morgan', required: true, readOnly: false },
  ],
  locale: identitySignInContext.locale,
  locales: identitySignInContext.locales,
  frameTexts: identityFrameTexts,
  legal: identityLegal,
  texts: identityUpdateProfileTexts,
}

/** The texts of the terms page, in English, as the theme's messages give them. */
export const identityTermsTexts: TermsTexts = {
  title: 'Accept the terms',
  accept: 'I accept the {terms} and the {privacy}.',
  acceptTermsOnly: 'I accept the {terms}.',
  termsOfService: 'Terms of service',
  privacyPolicy: 'Privacy policy',
  required: 'Accept the terms to continue.',
  continue: 'Continue',
  decline: 'Decline and sign out',
}

/** What the identity provider's terms page draws in the design system. Its links use their own addresses, apart from the footer's. */
export const identityTermsContext: TermsContext = {
  page: 'terms',
  title: identityTermsTexts.title,
  realmName: identitySignInContext.realmName,
  loginAction: '#accept-terms',
  termsUrl: '#terms-of-service',
  privacyUrl: '#privacy-policy',
  locale: identitySignInContext.locale,
  locales: identitySignInContext.locales,
  frameTexts: identityFrameTexts,
  legal: identityLegal,
  texts: identityTermsTexts,
}

/** What the identity provider's sign-out question draws in the design system. */
export const identitySignOutContext: SignOutContext = {
  page: 'signOut',
  title: 'Sign out',
  realmName: identitySignInContext.realmName,
  action: '#sign-out',
  sessionCode: 'fixture-session-code',
  backUrl: '#application',
  locale: identitySignInContext.locale,
  locales: identitySignInContext.locales,
  frameTexts: identityFrameTexts,
  legal: identityLegal,
  texts: { message: 'Do you want to sign out?', signOut: 'Sign out', backToApplication: 'Back to application' },
}

/** What the identity provider's notice page draws in the design system. */
export const identityNoticeContext: NoticeContext = {
  page: 'notice',
  title: 'Notice',
  realmName: identitySignInContext.realmName,
  message: 'Your account has been updated.',
  linkUrl: '#application',
  linkText: 'Back to application',
  locale: identitySignInContext.locale,
  locales: identitySignInContext.locales,
  frameTexts: identityFrameTexts,
  legal: identityLegal,
}

/** The message of the identity provider's error page in the design system. */
export const identityErrorMessage = 'The link is no longer valid.'

/** What the identity provider's error page draws in the design system. */
export const identityErrorContext: ErrorContext = {
  page: 'error',
  title: 'Something went wrong',
  realmName: identitySignInContext.realmName,
  error: identityErrorMessage,
  backUrl: '#application',
  locale: identitySignInContext.locale,
  locales: identitySignInContext.locales,
  frameTexts: identityFrameTexts,
  legal: identityLegal,
  texts: { backToApplication: 'Back to application' },
}

/** What the identity provider's expired page draws in the design system. */
export const identityPageExpiredContext: PageExpiredContext = {
  page: 'pageExpired',
  title: 'Page expired',
  realmName: identitySignInContext.realmName,
  continueUrl: '#continue',
  restartUrl: '#start-again',
  locale: identitySignInContext.locale,
  locales: identitySignInContext.locales,
  frameTexts: identityFrameTexts,
  legal: identityLegal,
  texts: {
    message: 'This page took too long and has expired. Continue where you were, or start again.',
    continue: 'Continue',
    startAgain: 'Start again',
  },
}

/** What the identity provider's SAML redirect draws in the design system. */
export const identitySamlPostContext: SamlPostContext = {
  page: 'samlPost',
  title: 'Redirecting',
  realmName: identitySignInContext.realmName,
  action: '#saml',
  samlResponse: 'fixture-saml-response',
  relayState: 'fixture-relay-state',
  locale: identitySignInContext.locale,
  locales: identitySignInContext.locales,
  frameTexts: identityFrameTexts,
  legal: identityLegal,
  texts: { message: 'Taking you to the next step. If nothing happens, select Continue.', continue: 'Continue' },
}

/** What the identity provider's front-channel sign-out draws in the design system. */
export const identityFrontChannelSignOutContext: FrontChannelSignOutContext = {
  page: 'frontChannelSignOut',
  title: 'Signing out',
  realmName: identitySignInContext.realmName,
  clients: [
    { name: 'Purchase requests', url: '#purchase-requests-sign-out' },
    { name: 'Leave requests', url: '#leave-requests-sign-out' },
  ],
  redirectUrl: '#signed-out',
  locale: identitySignInContext.locale,
  locales: identitySignInContext.locales,
  frameTexts: identityFrameTexts,
  legal: identityLegal,
  texts: { message: 'Signing you out of your applications. This page goes on by itself.', continue: 'Continue' },
}

/** The site texts, in English. */
export const siteTexts: Record<string, string> = {
  'category.label': 'Category',
  'category.name': 'Name',
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
  'kpi.showRequests': 'Show requests',
  'preview.requests': 'Requests',
  'preview.tasks': 'Tasks',
  'preview.help': 'A short note that explains the field.',
  'preview.error.field': 'This value is not valid.',
  'preview.error.title': 'Enter a title.',
  'preview.error.unitPrice': 'Unit price cannot be negative.',
  'site.title': 'Purchase requests',
  'nav.requests': 'Requests',
  'nav.tasks': 'Tasks',
  'nav.dashboard': 'Dashboard',
  'nav.instances': 'Approvals',
  'page.requests': 'Requests',
  'page.dashboard': 'Dashboard',
  'page.tasks': 'Tasks',
  'page.instances': 'Approvals',
  'request.outcome.approve': 'Approve',
  'request.outcome.reject': 'Reject',
}

export const signUpApplications = {
  applications: [
    {
      id: 'approvals',
      titles: { en: 'Approvals', vi: 'Phê duyệt' },
      descriptions: {
        en: 'Requests that people approve in steps.',
        vi: 'Các yêu cầu được duyệt theo từng bước.',
      },
    },
  ],
}

export const signUpDocuments = {
  terms: {
    version: '2026-11-01',
    urls: { en: 'https://axis.example.com/terms', vi: 'https://axis.example.com/vi/dieu-khoan' },
  },
  dpa: {
    version: '2026-11-01',
    urls: { en: 'https://axis.example.com/dpa', vi: 'https://axis.example.com/vi/xu-ly-du-lieu' },
  },
  privacy: {
    version: '2026-11-01',
    urls: { en: 'https://axis.example.com/privacy', vi: 'https://axis.example.com/vi/quyen-rieng-tu' },
  },
  licencesUrl: 'https://axis.example.com/licences',
}

export const signUpRegion = { tenantDomain: 'vn.example.com' }

/** The API paths that the fixtures answer. A request outside them is not a fixture's to answer. */
export const fixtureScopes = [
  `${apiPath}/`,
  '/api/studio/',
  '/api/sign-up/',
  '/api/sites',
  '/api/me',
  '/api/about',
  '/api/notifications',
  '/api/users',
  '/api/tenant/',
] as const

/** A person's table choices when nothing is kept. */
export const noTableChoices = { columns: null, views: [] }

/**
 * The answers of the fixture application's APIs, by path or by path and query. The page's own
 * queries (paging, sorting) are answered by the path alone.
 */
export const routes: Record<string, FixtureRoute> = {
  [requestsPath]: () => recordPage,
  [`${requestsPath}/${requestId}`]: () => savedRecord,
  [`${requestsPath}/${requestId}/history`]: () => history,
  [`${requestsPath}/${shellRequestId}`]: () => shellRecord,
  [`${requestsPath}/${shellRequestId}/history`]: () => history,
  [`${requestsPath}/${missingRequestId}`]: () => new Response(null, { status: 404 }),
  [categoriesPath]: () => categoryPage,
  [`${categoriesPath}/${categoryId}`]: () => categoryRecord,
  [`${categoriesPath}/${categoryId}/history`]: () => history,
  [tasksPath]: () => taskPage,
  [`${tasksPath}/${taskId}`]: () => openTask,
  [`${tasksPath}/${taskId}/form-state`]: noStateAnswer,
  [`${tasksPath}/${missingTaskId}`]: () => new Response(null, { status: 404 }),
  [instancesPath]: () => instancePage,
  [`${instancesPath}/${instanceId}`]: () => instanceDetail,
  [deadLettersPath]: () => deadLetterPage,
  [requestsByStatusPath]: () => requestsByStatusPage,
  [requestTotalsPath]: () => requestTotalsPage,
  [emptyRequestsPath]: () => emptyRecordPage,
  [`${blankRequestsPath}/${requestId}`]: () => blankRecord,
  [emptyTasksPath]: () => emptyTaskPage,
  [noRowsPath]: () => emptyRecordPage,
  [`/api/me/tables/site.${sitePath}.requests`]: () => noTableChoices,
  [`/api/me/tables/site.${sitePath}.tasks`]: () => noTableChoices,
  [`/api/me/tables/site.${sitePath}.instances`]: () => noTableChoices,
  [`/api/sites/${sitePath}/texts/${locale}`]: () => ({ texts: siteTexts }),
  // A release's texts are the site's texts with its process's own, as a site reads a release it shows work from.
  [`/api/sites/${sitePath}/texts/${locale}?release=${releaseId}`]: () => ({
    locale,
    texts: { ...siteTexts, ...approveTexts },
  }),
  [`/api/sites/${sitePath}`]: () => applicationSite,
  [`/api/sites/${sitePath}/pages/requests`]: () => requestsPage,
  [`/api/sites/${sitePath}/pages/request`]: () => requestPage,
  [`/api/sites/${sitePath}/pages/tasks`]: () => tasksPage,
  [`/api/sites/${sitePath}/pages/dashboard`]: () => dashboardPage,
  [`/api/sites/${sitePath}/pages/instances`]: () => instancesPage,
  // The inspector links read each navigation page by the name its navigation gives, not by its address.
  [`/api/sites/${sitePath}/pages/Requests`]: () => requestsPage,
  [`/api/sites/${sitePath}/pages/Tasks`]: () => tasksPage,
  [`/api/sites/${sitePath}/pages/Dashboard`]: () => dashboardPage,
  [`/api/sites/${sitePath}/pages/Instances`]: () => instancesPage,
  [`/api/sites/${sitePath}/records/category`]: () => categoryRecordPage,
  [formStatePath('RequestForm')]: noStateAnswer,
  [notificationsPath]: () => notificationPage,
  '/api/sites': () => ({ sites: listedSites }),
  '/api/me': () => currentUser,
  '/api/me/authenticators': () => ownAuthenticators,
  '/api/me/sessions': () => ownSessions,
  // A save is a PUT, which the page answers with its status alone, so 204 is enough.
  '/api/me/notification-preferences': (init) =>
    init?.method === 'PUT' ? new Response(null, { status: 204 }) : ownNotificationPreferences,
  '/api/me/tables/users': () => noTableChoices,
  '/api/users': () => shellUsers,
  [`/api/users/${shellUserId}/roles`]: () => shellUserRoles,
  // The users page's tenant sections answer 404, so it shows only its people.
  '/api/tenant/outside-clients': () => new Response(null, { status: 404 }),
  '/api/tenant/usage': () => new Response(null, { status: 404 }),
  '/api/tenant/hosts': () => new Response(null, { status: 404 }),
  '/api/tenant/model-key': () => new Response(null, { status: 404 }),
  '/api/tenant/application-packages': () => new Response(null, { status: 404 }),
  '/api/about': () => ({ version: '0.1.0' }),
  '/api/notifications': () => tenantNoticePage,
}
