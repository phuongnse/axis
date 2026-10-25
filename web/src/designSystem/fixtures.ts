import type { ChartKind } from '../platform/chartOption'
import type { HistoryPage, RecordItem, RecordPage } from '../platform/records'
import type { InstancePage } from '../platform/inspection'
import type {
  DataSourceMetadata,
  EntityMetadata,
  EntityWidgetMetadata,
  FieldMetadata,
  FieldType,
  FormMetadata,
  PageWidgetMetadata,
  WidgetMetadata,
} from '../platform/site'
import type { TaskPage } from '../platform/tasks'

/** The fixture application. Its name is also the marker that tells a design-system build from the production SPA. */
export const application = 'DesignSystemSample'
/** The path of the fixture application's site. */
export const sitePath = 'sample'
export const locale = 'en'

const apiPath = `/api/apps/${application}`
const requestsPath = `${apiPath}/entities/Request/records`
const categoriesPath = `${apiPath}/entities/Category/records`
const formStatePath = `${apiPath}/forms/RequestForm/state`
const tasksPath = `${apiPath}/tasks`
const instancesPath = `${apiPath}/processes/Approve/instances`
const requestsByStatusPath = `${apiPath}/data-sources/RequestsByStatus/rows`
const requestTotalsPath = `${apiPath}/data-sources/RequestTotals/rows`

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

/** One field of each type. The names are the labels' keys without their prefix. */
export const fields: Record<FieldType, FieldMetadata> = {
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
      { ...field('description', 'text', 'requestLine.description'), maxLength: 100 },
      field('count', 'integer', 'requestLine.count'),
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
export function entityWithField(type: FieldType): EntityMetadata {
  return { ...entity, fields: [fields[type]] }
}

const widgetBase: WidgetMetadata = {
  type: 'table',
  entity: null,
  dataSource: null,
  form: null,
  tasksPath: null,
  process: null,
  instancesPath: null,
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

export const taskInboxWidget: WidgetMetadata = { ...widgetBase, type: 'taskInbox', tasksPath }

export const processInspectorWidget: WidgetMetadata = {
  ...widgetBase,
  type: 'processInspector',
  process: 'Approve',
  instancesPath,
}

const requestsByStatus: DataSourceMetadata = {
  name: 'RequestsByStatus',
  rowsPath: requestsByStatusPath,
  entity: 'Request',
  parameters: [],
  pageSize: 100,
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
export function chartWidget(kind: ChartKind): WidgetMetadata {
  const requests = { name: 'requests', labelKey: 'chart.requests' }
  const urgent = { name: 'urgentRequests', labelKey: 'chart.urgentRequests' }
  return {
    ...widgetBase,
    type: 'chart',
    dataSource: requestsByStatus,
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
      { description: 'Standing desk', count: '2' },
      { description: 'Desk lamp', count: '10' },
    ],
  },
  labels: { category: 'Office furniture' },
})

export const savedRecord = savedRequest(requestId, 'Standing desks for the new office', 'submitted')

const recordPage: RecordPage = {
  items: [
    savedRecord,
    savedRequest('7a2d3b4c-5e6f-4a71-9b82-a3c4d5e6f708', 'Replacement laptop chargers', 'approved'),
    savedRequest('8b3e4c5d-6f70-4b82-8c93-b4d5e6f70819', 'Whiteboard markers', 'draft'),
  ],
  page: 1,
  pageSize: 20,
  totalCount: 3,
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
      dueAt: '2026-03-20T17:00:00.000000Z',
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
      createdAt: '2026-03-05T14:02:00.000000Z',
    },
  ],
  page: 1,
  pageSize: 20,
  totalCount: 2,
}

const instancePage: InstancePage = {
  items: [
    {
      id: 'be6b7f80-92a3-4eb5-bfc6-e708192a3b4c',
      subjectId: requestId,
      state: 'waiting',
      step: 'Review',
      releaseId,
      createdAt: '2026-03-04T09:31:00.000000Z',
      endedAt: null,
    },
    {
      id: 'cf7c8091-a3b4-4fc6-80d7-f8192a3b4c5d',
      subjectId: '7a2d3b4c-5e6f-4a71-9b82-a3c4d5e6f708',
      state: 'completed',
      step: 'Done',
      releaseId,
      createdAt: '2026-03-02T11:15:00.000000Z',
      endedAt: '2026-03-03T08:40:00.000000Z',
    },
  ],
  page: 1,
  pageSize: 20,
  totalCount: 2,
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
  'requestLine.count': 'Count',
  'request.section.general': 'General',
  'request.section.details': 'Details',
  'request.new': 'New request',
  'request.open': 'Open',
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
}

/** The API paths that the fixtures answer. A request outside them is not a fixture's to answer. */
export const fixtureScopes = [`${apiPath}/`, `/api/sites/${sitePath}/`] as const

/**
 * The answers of the fixture application's APIs, by path or by path and query. The page's own
 * queries (paging, sorting) are answered by the path alone.
 */
export const routes: Record<string, () => unknown> = {
  [requestsPath]: () => recordPage,
  [`${requestsPath}/${requestId}`]: () => savedRecord,
  [`${requestsPath}/${requestId}/history`]: () => history,
  [formStatePath]: () => ({ sections: [], fields: {}, errors: {} }),
  [categoriesPath]: () => categoryPage,
  [tasksPath]: () => taskPage,
  [instancesPath]: () => instancePage,
  [requestsByStatusPath]: () => requestsByStatusPage,
  [requestTotalsPath]: () => requestTotalsPage,
  [`/api/sites/${sitePath}/texts/${locale}`]: () => ({ texts: siteTexts }),
}
