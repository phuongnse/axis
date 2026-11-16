import type {
  StudioApplication,
  StudioBindings,
  StudioCatalog,
  StudioDiagnostic,
  StudioFormMetadataAnswer,
  StudioNameEntity,
  StudioNames,
  StudioPageMetadataAnswer,
  StudioPolicyMatrix,
  StudioReleases,
  StudioResource,
  StudioResourceItem,
  StudioResourceReferences,
} from '../platform/studio'
import { application, entity as requestEntity, fields, formWidget, locale, tableWidget } from './fixtures'

// The answers of the `studio` preview. Its states answer the same `/api/studio/apps` path, so each state brings its own
// routes, and the preview shows one state at a time, see docs/reference/widget-previews.md#the-bundle.

const appsPath = '/api/studio/apps'
const folderPath = `${appsPath}/${application}`

/** An id in the form of a resource id, made from a number. */
function id(number: number): string {
  return `5d1e0000-0000-4000-8000-${number.toString(16).padStart(12, '0')}`
}

const manifestId = id(1)
const siteId = id(2)
const pageId = id(3)
const formId = id(4)
const roleId = id(5)
const policyId = id(6)
const approverRoleId = id(7)
const buyerRoleId = id(8)
const processId = id(9)
const firstEntityId = 100

/** What a state's folder holds: its applications, resources and names. */
interface Folder {
  apps: StudioApplication[]
  /** The title of the open application, which the application list shows too. */
  title: string
  entities: { name: string; title: string }[]
  /** Set for a folder that holds only its manifest, as a new application does. */
  bare?: boolean
}

function resource(
  itemId: string,
  kind: string,
  name: string,
  file: string,
  titles: Record<string, string>,
  usedBy = 0,
): StudioResourceItem {
  return { id: itemId, kind, name, file, version: `v-${itemId.slice(-4)}`, usedBy, titles }
}

const pageItem = resource(pageId, 'page', 'RequestList', 'pages/request-list.json', { [locale]: 'Requests' }, 1)
const formItem = resource(formId, 'form', 'RequestForm', 'forms/request-form.json', { [locale]: 'Request' }, 1)
const processItem = resource(processId, 'process', 'RequestApproval', 'processes/request-approval.json', {
  [locale]: 'Request approval',
})

function entityItems(folder: Folder): StudioResourceItem[] {
  return folder.entities.map((entity, index) =>
    resource(
      id(firstEntityId + index),
      'entity',
      entity.name,
      `entities/${entity.name}.json`,
      { [locale]: entity.title },
      index === 0 ? 2 : 0,
    ),
  )
}

function items(folder: Folder): StudioResourceItem[] {
  if (folder.bare) {
    return [resource(manifestId, 'application', application, 'application.json', {})]
  }
  return [
    resource(manifestId, 'application', application, 'application.json', {}),
    resource(siteId, 'site', 'Requests', 'sites/requests.json', { [locale]: folder.title }, 0),
    ...entityItems(folder),
    pageItem,
    formItem,
    processItem,
    resource(roleId, 'role', 'Requester', 'roles/requester.json', { [locale]: 'Requester' }, 1),
    resource(approverRoleId, 'role', 'Approver', 'roles/approver.json', { [locale]: 'Approver' }),
    resource(buyerRoleId, 'role', 'Buyer', 'roles/buyer.json', { [locale]: 'Buyer' }),
    resource(policyId, 'policy', 'RequesterPolicy', 'policies/requester.json', { [locale]: 'Requester' }),
  ]
}

const manifest: StudioResource = {
  ...resource(manifestId, 'application', application, 'application.json', {}),
  content: {
    id: manifestId,
    kind: 'application',
    name: application,
    formatVersion: 1,
    label: { textKey: 'application.title' },
    description: { textKey: 'application.description' },
    timeZone: 'Asia/Ho_Chi_Minh',
    modules: [{ id: 'organization', version: '^1.0.0', roles: { admin: 'Requester' } }],
  },
}

const typicalDiagnostics = [
  {
    code: 'AX0412',
    severity: 'warning',
    message: 'The policy grants no action on any entity.',
    file: 'policies/requester.json',
    path: '/grants',
    resourceId: policyId,
  },
] satisfies StudioDiagnostic[]

/** An entity with the content the entity designer opens. It has plain fields only, so the designer asks for no expression scope. */
function entityResource(folder: Folder, index: number): StudioResource {
  const entity = folder.entities[index]
  const key = entity.name.toLowerCase()
  return {
    ...entityItems(folder)[index],
    content: {
      id: id(firstEntityId + index),
      kind: 'entity',
      name: entity.name,
      formatVersion: 1,
      label: { textKey: `${key}.label` },
      displayField: 'title',
      fields: [
        { name: 'title', type: 'text', maxLength: 200, label: { textKey: `${key}.title` } },
        { name: 'amount', type: 'decimal', precision: 12, scale: 2, label: { textKey: `${key}.amount` } },
      ],
    },
  }
}

function entityNames(folder: Folder): StudioNameEntity[] {
  return folder.entities.map((entity, index) => ({
    id: id(firstEntityId + index),
    name: entity.name,
    module: null,
    fields: [
      { name: 'title', type: 'text', target: null, values: null },
      { name: 'amount', type: 'decimal', target: null, values: null },
    ],
  }))
}

function entityTexts(folder: Folder): StudioNames['texts'] {
  return folder.entities.flatMap((entity) => {
    const key = entity.name.toLowerCase()
    return [
      { key: `${key}.label`, texts: { [locale]: entity.title }, usedBy: 1 },
      { key: `${key}.title`, texts: { [locale]: 'Title' }, usedBy: 1 },
      { key: `${key}.amount`, texts: { [locale]: 'Amount' }, usedBy: 1 },
    ]
  })
}

/** The texts of the page, form and process that the designers of the Typical folder open. */
const pageAndProcessTexts: StudioNames['texts'] = [
  { key: 'requestList.title', texts: { [locale]: 'Requests' }, usedBy: 1 },
  { key: 'request.sections.details', texts: { [locale]: 'Details' }, usedBy: 1 },
  { key: 'requestApproval.review', texts: { [locale]: 'Review the request' }, usedBy: 1 },
  { key: 'task.approve', texts: { [locale]: 'Approve' }, usedBy: 1 },
  { key: 'task.reject', texts: { [locale]: 'Reject' }, usedBy: 1 },
]

/** The page of the Typical folder: a table of its Request entity, which the page designer opens. */
const pageResource: StudioResource = {
  ...pageItem,
  content: {
    id: pageId,
    kind: 'page',
    name: 'RequestList',
    formatVersion: 1,
    title: { textKey: 'requestList.title' },
    widgets: [{ type: 'table', name: 'requests', entity: 'Request' }],
  },
}

/** The form of the Typical folder: the title and the amount of a request, which the form designer opens. */
const formResource: StudioResource = {
  ...formItem,
  content: {
    id: formId,
    kind: 'form',
    name: 'RequestForm',
    formatVersion: 1,
    entity: 'Request',
    sections: [
      {
        name: 'general',
        title: { textKey: 'request.sections.details' },
        fields: [{ field: 'title' }, { field: 'amount' }],
      },
    ],
  },
}

/** The process of the Typical folder: a large request goes to an approver, and a small one ends at once. */
const processResource: StudioResource = {
  ...processItem,
  content: {
    id: processId,
    kind: 'process',
    name: 'RequestApproval',
    formatVersion: 1,
    entity: 'Request',
    start: 'checkAmount',
    steps: [
      {
        name: 'checkAmount',
        type: 'decision',
        branches: [{ name: 'large', when: 'amount >= 1000', next: 'review' }],
        otherwise: 'done',
      },
      {
        name: 'review',
        type: 'task',
        label: { textKey: 'requestApproval.review' },
        assignee: { role: 'Approver' },
        form: 'RequestForm',
        outcomes: [
          { name: 'approve', label: { textKey: 'task.approve' }, next: 'done' },
          { name: 'reject', label: { textKey: 'task.reject' }, next: 'done' },
        ],
      },
      { name: 'done', type: 'end' },
    ],
  },
}

/** The page designer's canvas of the page: the table, drawn with the site's widgets over placeholder data. */
const pageMetadata = {
  page: {
    name: 'RequestList',
    titleKey: 'requestList.title',
    parameters: [],
    widgets: [{ ...tableWidget, path: '/widgets/0' }],
  },
  diagnostics: [],
} satisfies StudioPageMetadataAnswer

/** The form designer's canvas of the form: the title and the amount, with no action. */
const formMetadata = {
  widget: {
    ...formWidget,
    actions: [],
    entity: { ...requestEntity, fields: [fields.text, fields.decimal] },
    form: {
      name: 'RequestForm',
      steps: [],
      sections: [
        {
          name: 'general',
          titleKey: 'request.sections.details',
          path: '/sections/0',
          fields: [
            { name: 'title', readOnly: false, path: '/sections/0/fields/0' },
            { name: 'amount', readOnly: false, path: '/sections/0/fields/1' },
          ],
        },
      ],
    },
  },
  diagnostics: [],
} satisfies StudioFormMetadataAnswer

/** The form's uses: the process's approval task uses it, so the form designer lists the process. */
const formReferences = {
  uses: [],
  usedBy: [
    {
      relation: 'task form',
      file: 'processes/request-approval.json',
      path: '/steps/1/form',
      node: {
        type: 'resource',
        id: processId,
        kind: 'process',
        name: 'RequestApproval',
        file: 'processes/request-approval.json',
      },
    },
  ],
} satisfies StudioResourceReferences

function names(folder: Folder): StudioNames {
  const own = folder.bare !== true
  return {
    locales: [locale],
    entities: entityNames(folder),
    dataSources: [],
    processes: own
      ? [
          {
            id: processId,
            name: 'RequestApproval',
            module: null,
            entity: 'Request',
            parameters: [],
            steps: [
              { name: 'checkAmount', type: 'decision', outcomes: [] },
              { name: 'review', type: 'task', outcomes: ['approve', 'reject'] },
              { name: 'done', type: 'end', outcomes: [] },
            ],
          },
        ]
      : [],
    rules: [],
    connectors: [],
    events: [],
    roles: own
      ? [
          { id: roleId, name: 'Requester' },
          { id: approverRoleId, name: 'Approver' },
          { id: buyerRoleId, name: 'Buyer' },
        ]
      : [],
    sequences: [],
    forms: own ? [{ id: formId, name: 'RequestForm', module: null }] : [],
    pages: own ? [{ id: pageId, name: 'RequestList', module: null, parameters: [] }] : [],
    policies: own ? [{ id: policyId, name: 'RequesterPolicy', module: null }] : [],
    contracts: [],
    texts: [
      { key: 'application.title', texts: { [locale]: folder.title }, usedBy: 1 },
      { key: 'application.description', texts: { [locale]: 'Request purchases and approve them.' }, usedBy: 1 },
      ...entityTexts(folder),
      ...(own ? pageAndProcessTexts : []),
    ],
    seeds: [],
    triggers: [],
    scenarios: [],
    textResources: [],
  } satisfies StudioNames
}

const typicalReleases = {
  active: { releaseId: '7c1d0000-0000-4000-8000-000000000002', number: 2, contentHash: 'c2' },
  folderContentHash: 'c3',
  items: [
    {
      releaseId: '7c1d0000-0000-4000-8000-000000000002',
      number: 2,
      contentHash: 'c2',
      activatedAt: '2026-10-21T09:30:00Z',
      actor: '3f8e9d0c-1b2a-4c3d-8e4f-5a6b7c8d9e0f',
      actorName: 'Alex Morgan',
      label: 'Approval step',
      note: null,
      requestId: null,
      offStandard: false,
      relaxed: [],
    },
    {
      releaseId: '7c1d0000-0000-4000-8000-000000000001',
      number: 1,
      contentHash: 'c1',
      activatedAt: '2026-10-14T14:05:00Z',
      actor: '3f8e9d0c-1b2a-4c3d-8e4f-5a6b7c8d9e0f',
      actorName: 'Alex Morgan',
      label: 'First release',
      note: null,
      requestId: null,
      offStandard: false,
      relaxed: [],
    },
  ],
  changes: [{ path: 'pages/request-list.json', change: 'changed', kind: null, name: null, titles: null, locale: null }],
} satisfies StudioReleases

/** What a state's folder answers for its diagnostics and its releases. */
interface StateAnswers {
  diagnostics: StudioDiagnostic[]
  releases: StudioReleases
}

const typicalAnswers: StateAnswers = { diagnostics: typicalDiagnostics, releases: typicalReleases }

const errorAnswers: StateAnswers = {
  diagnostics: [
    {
      code: 'AX0101',
      severity: 'error',
      message: 'The display field does not name a field of the entity.',
      file: 'entities/Request.json',
      path: '/displayField',
      resourceId: id(firstEntityId),
    },
    {
      code: 'AX0207',
      severity: 'error',
      message: 'The page lists an entity that does not exist.',
      file: 'pages/request-list.json',
      path: '/content/entity',
      resourceId: pageId,
    },
    {
      code: 'AX0233',
      severity: 'error',
      message: 'The form binds a field that the entity does not have.',
      file: 'forms/request-form.json',
      path: '/fields/2/field',
      resourceId: formId,
    },
    ...typicalDiagnostics,
  ],
  releases: {
    ...typicalReleases,
    changes: [
      { path: 'entities/Request.json', change: 'changed', kind: null, name: null, titles: null, locale: null },
      { path: 'pages/request-list.json', change: 'changed', kind: null, name: null, titles: null, locale: null },
      { path: 'roles/approver.json', change: 'added', kind: null, name: null, titles: null, locale: null },
      {
        path: 'forms/old-request.json',
        change: 'removed',
        kind: 'form',
        name: 'OldRequestForm',
        titles: { [locale]: 'Old request' },
        locale,
      },
    ],
  },
}

const unpublishedAnswers: StateAnswers = {
  diagnostics: [],
  releases: { active: null, folderContentHash: 'n1', items: [], changes: null },
}

const catalog = {
  modules: [
    {
      id: 'organization',
      label: 'Organization',
      versions: ['1.2.0', '1.0.0'],
      roles: [{ name: 'admin', titles: { [locale]: 'Administrator' } }],
      connectors: [],
    },
  ],
  packages: [],
  timeZones: [
    { id: 'Asia/Ho_Chi_Minh', offset: 420 },
    { id: 'Europe/Paris', offset: 60 },
  ],
} satisfies StudioCatalog

const bindings = {
  url: ['PurchasingSystemUrl', 'PurchasingSystemTokenUrl'],
  secret: ['PurchasingSystemClientId', 'PurchasingSystemClientSecret'],
  storage: ['Files'],
} satisfies StudioBindings

const settings = { autoAccept: false } satisfies { autoAccept: boolean }

/** The routes of the open application, with the state's own applications, resources and names. */
function policyMatrix(folder: Folder) {
  const actions = ['read', 'create', 'update', 'delete', 'export', 'import']
  const granted: Record<string, string[]> = {
    Requester: ['read', 'create'],
    Approver: ['read', 'update'],
    Buyer: ['read', 'export'],
  }
  return {
    targets: folder.entities.map((entity, index) => ({
      kind: 'entity' as const,
      id: id(firstEntityId + index),
      name: entity.name,
      module: null,
      actions,
      entity: null,
    })),
    roles: Object.entries(granted).map(([name, roleActions], row) => ({
      id: [roleId, approverRoleId, buyerRoleId][row],
      name,
      accessPolicy: { name: `${name}Access`, id: null },
      editable: true,
      readOnlyReason: null,
      cells: folder.entities.map((entity, index) => ({
        kind: 'entity',
        target: entity.name,
        actions: index === 0 ? roleActions : roleActions.slice(0, 1),
        missingEntityRead: null,
        grants:
          name === 'Requester' && index === 0
            ? [
                {
                  policyId,
                  policy: 'RequesterPolicy',
                  index: 0,
                  actions: roleActions,
                  filter: null,
                  fields: null,
                  shared: false,
                  module: null,
                },
              ]
            : [],
      })),
    })),
  } satisfies StudioPolicyMatrix
}

/**
 * The routes of the open application, with the state's own applications, resources and names. `answers` holds the
 * diagnostics and releases of the state, and Typical's by default.
 */
function workspaceRoutes(folder: Folder, answers: StateAnswers = typicalAnswers): Record<string, () => unknown> {
  return {
    ...Object.fromEntries(
      folder.entities.map((_, index) => [
        `${folderPath}/resources/${id(firstEntityId + index)}`,
        () => entityResource(folder, index),
      ]),
    ),
    [`${folderPath}/policy-matrix`]: () => policyMatrix(folder),
    [appsPath]: () => ({ items: folder.apps }) satisfies { items: StudioApplication[] },
    [`${folderPath}/resources`]: () => ({ items: items(folder) }) satisfies { items: StudioResourceItem[] },
    [`${folderPath}/diagnostics`]: () =>
      ({ diagnostics: answers.diagnostics }) satisfies { diagnostics: StudioDiagnostic[] },
    [`${folderPath}/names`]: () => names(folder),
    '/api/studio/catalog': () => catalog,
    '/api/studio/bindings': () => bindings,
    [`${folderPath}/relaxed`]: () => ({ items: [] }),
    [`${folderPath}/resources/${manifestId}`]: () => manifest,
    [`${folderPath}/resources/${pageId}`]: () => pageResource,
    [`${folderPath}/resources/${pageId}/page-metadata`]: () => pageMetadata,
    [`${folderPath}/resources/${formId}`]: () => formResource,
    [`${folderPath}/resources/${formId}/page-metadata`]: () => formMetadata,
    [`${folderPath}/resources/${formId}/references`]: () => formReferences,
    [`${folderPath}/resources/${processId}`]: () => processResource,
    [`${folderPath}/releases`]: () => answers.releases,
    [`${folderPath}/settings`]: () => settings,
  }
}

function app(name: string, title: string, description: string | null): StudioApplication {
  return {
    name,
    titles: { [locale]: title },
    descriptions: description === null ? {} : { [locale]: description },
  }
}

const typicalFolder: Folder = {
  apps: [
    app(application, 'Purchase requests', 'Request purchases and approve them.'),
    app('LeaveRequests', 'Leave requests', 'Book time off and see who is away.'),
  ],
  title: 'Purchase requests',
  entities: [
    { name: 'Request', title: 'Purchase request' },
    { name: 'Supplier', title: 'Supplier' },
    { name: 'Category', title: 'Category' },
  ],
}

const manyFolder: Folder = {
  apps: [
    app(application, 'Purchase requests', 'Request purchases and approve them.'),
    ...Array.from({ length: 19 }, (_, index) =>
      app(`Application${index + 1}`, `Application ${index + 1}`, index % 2 === 0 ? 'A team’s own workspace.' : null),
    ),
  ],
  title: 'Purchase requests',
  entities: Array.from({ length: 40 }, (_, index) => ({
    name: `Entity${index + 1}`,
    title: `Entity ${index + 1}`,
  })),
}

const longTitle = 'Purchase requests, approvals and supplier orders for every office, branch and warehouse we run'
const longFolder: Folder = {
  apps: [
    app(
      application,
      longTitle,
      'Request purchases, take them through approval, order them from a supplier and receive the goods, with every step kept for the yearly audit.',
    ),
    app('LeaveRequests', 'Leave requests', null),
  ],
  title: longTitle,
  entities: [
    {
      name: 'SupplierInvoiceReconciliationLineItemForQuarterlyAuditWithManualOverride',
      title: 'Supplier invoice reconciliation line item for the quarterly audit with a manual override',
    },
    {
      name: 'PurchaseRequestApprovalEscalationForTheRegionalFinanceController',
      title: 'Purchase request approval escalation for the regional finance controller',
    },
    { name: 'Category', title: 'Category' },
  ],
}

const newFolder: Folder = {
  apps: [app(application, 'Equipment loans', null), app('LeaveRequests', 'Leave requests', null)],
  title: 'Equipment loans',
  entities: [],
  bare: true,
}

/** The id of the Request entity of the Typical folder, which the Entity state opens at `&resource=<id>`. */
export const typicalEntityId = id(firstEntityId)

/** The id of the RequestList page of the Typical folder, which the page design card opens at `&resource=<id>`. */
export const typicalPageId = pageId

/** The id of the RequestForm form of the Typical folder, which the form design card opens at `&resource=<id>`. */
export const typicalFormId = formId

/** The id of the RequestApproval process of the Typical folder, which the process design card opens at `&resource=<id>`. */
export const typicalProcessId = processId

// Proposed (#3265, step 1): an entity whose validations are expressions, so the entity designer
// shows the expression editor with the line that reads each expression back in words.

/** The Request entity with an enum, a date, a yes-or-no and a reference field, and validations that use them. */
function expressionEntity(folder: Folder): StudioResource {
  const plain = entityResource(folder, 0)
  return {
    ...plain,
    content: {
      ...plain.content,
      fields: [
        { name: 'title', type: 'text', maxLength: 200, label: { textKey: 'request.title' } },
        { name: 'amount', type: 'decimal', precision: 12, scale: 2, label: { textKey: 'request.amount' } },
        {
          name: 'status',
          type: 'enum',
          values: ['draft', 'submitted', 'approved'],
          valueLabels: {
            draft: { textKey: 'request.status.draft' },
            submitted: { textKey: 'request.status.submitted' },
            approved: { textKey: 'request.status.approved' },
          },
          label: { textKey: 'request.status' },
        },
        { name: 'urgent', type: 'boolean', label: { textKey: 'request.urgent' } },
        {
          name: 'dueDate',
          type: 'date',
          default: 'addDays(today(), 7)',
          label: { textKey: 'request.dueDate' },
        },
        { name: 'supplier', type: 'reference', target: 'Supplier', label: { textKey: 'request.supplier' } },
        {
          name: 'total',
          type: 'decimal',
          precision: 12,
          scale: 2,
          expression: 'amount * 1.1',
          label: { textKey: 'request.total' },
        },
        {
          name: 'summary',
          type: 'text',
          expression: "concat(title, ' – ', supplier.name)",
          label: { textKey: 'request.summary' },
        },
      ],
      validations: [
        {
          name: 'positiveAmount',
          expression: 'amount is null or amount > 0',
          message: { textKey: 'request.positiveAmount' },
          field: 'amount',
        },
        {
          name: 'urgentNeedsDate',
          expression: "not urgent or dueDate is not null and status in ('submitted', 'approved')",
          message: { textKey: 'request.urgentNeedsDate' },
          field: 'dueDate',
        },
        {
          name: 'dueAfterToday',
          expression: "(status == 'draft' and amount >= 1000) or dueDate >= today()",
          message: { textKey: 'request.dueAfterToday' },
          field: 'dueDate',
        },
        {
          name: 'notTooLarge',
          expression: "not (status == 'approved' or amount > 50000)",
          message: { textKey: 'request.notTooLarge' },
          field: 'amount',
        },
        {
          name: 'withTypo',
          expression: 'amout > 0 and urgent',
          message: { textKey: 'request.positiveAmount' },
          field: 'amount',
        },
        {
          name: 'newValidation',
          expression: '',
          message: { textKey: '' },
          field: '',
        },
        {
          name: 'supplierNamed',
          expression: 'length(supplier.name) > 2',
          message: { textKey: 'request.supplierNamed' },
          field: 'supplier',
        },
      ],
    },
  }
}

const expressionTexts: StudioNames['texts'] = Object.entries({
  'request.title': ['Title', 'Tiêu đề'],
  'request.amount': ['Amount', 'Số tiền'],
  'request.status': ['Status', 'Trạng thái'],
  'request.status.draft': ['Draft', 'Nháp'],
  'request.status.submitted': ['Submitted', 'Đã gửi'],
  'request.status.approved': ['Approved', 'Đã duyệt'],
  'request.urgent': ['Urgent', 'Gấp'],
  'request.dueDate': ['Due date', 'Hạn chót'],
  'request.supplier': ['Supplier', 'Nhà cung cấp'],
  'request.total': ['Total', 'Tổng tiền'],
  'request.summary': ['Summary', 'Tóm tắt'],
  'supplier.name': ['Name', 'Tên'],
  'request.positiveAmount': ['The amount must be more than zero.', 'Số tiền phải lớn hơn 0.'],
  'request.urgentNeedsDate': ['An urgent request needs a due date.', 'Yêu cầu gấp cần có hạn chót.'],
  'request.dueAfterToday': ['The due date has passed.', 'Hạn chót đã qua.'],
  'request.supplierNamed': ['The supplier needs a name.', 'Nhà cung cấp cần có tên.'],
  'request.notTooLarge': ['An approved request cannot change.', 'Yêu cầu đã duyệt không thể sửa.'],
}).map(([key, [en, vi]]) => ({ key, texts: { en, vi }, usedBy: 1 }))

/** The expression type of a field type, as the server answers it. */
const fieldTypes: Record<string, string> = { money: 'decimal', reference: 'reference' }

/**
 * The type the place of a check or scope request needs, as the server answers it: a validation yes or no, a computed
 * field or a default the field's type, and any type elsewhere.
 */
function placeType(folder: Folder, body: string): string | null {
  const path = (JSON.parse(body) as { path?: string }).path ?? ''
  if (path.startsWith('/validations/')) {
    return 'boolean'
  }
  const match = /^\/fields\/(\d+)\/(expression|default)$/.exec(path)
  const fields = expressionEntity(folder).content.fields as { type: string }[]
  const type = match ? fields[Number(match[1])]?.type : undefined
  return type === undefined ? null : (fieldTypes[type] ?? type)
}

const knownNames = new Set(['amount', 'title', 'status', 'urgent', 'duedate', 'supplier', 'name', 'total', 'summary'])
const languageWords = new Set(['and', 'or', 'not', 'is', 'in', 'null', 'true', 'false'])

/** The answer of a check: an unknown name is AXC0046, as the server says it, and any other expression is well typed. */
function checkAnswer(folder: Folder, body: string) {
  const expression = (JSON.parse(body) as { expression?: string }).expression ?? ''
  for (const match of expression.matchAll(/'(?:[^']|'')*'|([A-Za-z][A-Za-z0-9]*)(\s*\()?/g)) {
    const name = match[1]
    if (name && !match[2] && !languageWords.has(name.toLowerCase()) && !knownNames.has(name.toLowerCase())) {
      const start = match.index
      return {
        type: null,
        diagnostics: [{ code: 'AXC0046', message: `Unknown field '${name}'`, start, end: start + name.length }],
      }
    }
  }
  return { type: placeType(folder, body) ?? 'boolean', diagnostics: [] }
}

/** A few of the functions the server lists for a place, with their words. */
const scopeFunctions = [
  ['today', [], 'date', 'Dates', 'today()', "Today's date."],
  ['addDays', ['date', 'days'], 'date', 'Dates', 'addDays(today(), 7)', 'The date some days after another.'],
  ['round', ['number', 'places'], 'decimal', 'Numbers', 'round(amount, 2)', 'A number rounded to some decimals.'],
  ['coalesce', ['value', '…'], 'any', 'Logic', 'coalesce(amount, 0)', 'The first value that is not null.'],
  ['concat', ['text', '…'], 'text', 'Text', "concat(title, ' ', code)", 'Texts put together.'],
  ['length', ['text'], 'integer', 'Text', 'length(title)', 'The number of characters in a text.'],
].map(([name, parameters, result, group, example, description]) => ({
  name,
  parameters,
  result,
  group,
  example,
  description,
}))

function expressionRoutes(folder: Folder): Record<string, () => unknown> {
  const routes = workspaceRoutes(folder)
  const typicalNames = names(folder)
  return {
    ...routes,
    [`${folderPath}/resources/${id(firstEntityId)}`]: () => expressionEntity(folder),
    [`${folderPath}/names`]: () => ({
      ...typicalNames,
      locales: ['en', 'vi'],
      entities: typicalNames.entities.map((entity) =>
        entity.name === 'Supplier'
          ? {
              ...entity,
              fields: [{ name: 'name', labelKey: 'supplier.name', type: 'text', target: null, values: null }],
            }
          : entity,
      ),
      texts: [...typicalNames.texts, ...expressionTexts],
    }),
    [`${folderPath}/expressions/scope`]: (init?: RequestInit) => ({
      type: placeType(folder, String(init?.body ?? '{}')),
      fields: (
        expressionEntity(folder).content.fields as { name: string; type: string; label: { textKey: string } }[]
      ).map((field) => ({
        name: field.name,
        labelKey: field.label.textKey,
        type: fieldTypes[field.type] ?? field.type,
      })),
      functions: scopeFunctions,
    }),
    [`${folderPath}/expressions/check`]: (init?: RequestInit) => checkAnswer(folder, String(init?.body ?? '{}')),
  }
}

/** The applications Studio lists in each state, with the routes of the open application where the state opens one. */
export const studioFixtures = {
  typical: workspaceRoutes(typicalFolder),
  empty: { [appsPath]: () => ({ items: [] }) satisfies { items: StudioApplication[] } },
  loading: { [appsPath]: () => new Promise<never>(() => {}) },
  failed: { [appsPath]: () => new Response(null, { status: 500 }) },
  manyRows: workspaceRoutes(manyFolder),
  longNames: workspaceRoutes(longFolder),
  withErrors: workspaceRoutes(typicalFolder, errorAnswers),
  newApplication: workspaceRoutes(newFolder, unpublishedAnswers),
  expressions: expressionRoutes(typicalFolder),
  accessDenied: { [appsPath]: () => new Response(null, { status: 403 }) },
} satisfies Record<string, Record<string, () => unknown>>
