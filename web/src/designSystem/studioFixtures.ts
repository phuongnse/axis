import type {
  ExpressionScope,
  StudioApplication,
  StudioBindings,
  StudioCatalog,
  StudioDiagnostic,
  StudioFormMetadataAnswer,
  StudioNameEntity,
  StudioNames,
  StudioPageMetadataAnswer,
  StudioPolicyMatrix,
  StudioRelease,
  StudioReleases,
  StudioResource,
  StudioResourceActivity,
  StudioResourceItem,
  StudioResourceReferences,
  StudioRevision,
  StudioRevisionDiff,
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

/** Each resource's activity: a mix of statuses, and a writer and a time on the first resource. */
function activity(folder: Folder): { items: StudioResourceActivity[] } {
  const statuses = ['new', 'changed', 'published', null] as const
  return {
    items: items(folder).map((item, index) => ({
      id: item.id,
      module: null,
      status: statuses[index % statuses.length],
      titles: null,
      descriptions: null,
      modifiedBy: index === 0 ? 'Linh Nguyen' : null,
      modifiedAt: index === 0 ? '2026-11-02T09:30:00Z' : null,
    })),
  }
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

/**
 * An entity with the content the entity designer opens. Its fields have plain types and no default, so the designer asks
 * for the scope of each Default editor and checks no expression.
 */
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
  partChanges: [],
} satisfies StudioReleases

/** The revisions of Typical, newest first: a person in Studio, a command-line client, and the server's own start of history. */
const typicalRevisions = {
  items: [
    {
      id: 'r3',
      summary: 'Added the approval step',
      author: 'Alex Morgan',
      userId: '3f8e9d0c-1b2a-4c3d-8e4f-5a6b7c8d9e0f',
      client: 'studio',
      request: null,
      time: '2026-10-21T09:30:00Z',
    },
    {
      id: 'r2',
      summary: 'Added the supplier entity',
      author: 'Alex Morgan',
      userId: '3f8e9d0c-1b2a-4c3d-8e4f-5a6b7c8d9e0f',
      client: 'cli',
      request: null,
      time: '2026-10-18T14:05:00Z',
    },
    {
      id: 'r1',
      summary: 'Created the application',
      author: '',
      userId: null,
      client: null,
      request: null,
      time: '2026-10-14T08:00:00Z',
    },
  ],
} satisfies { items: StudioRevision[] }

/** The newest revision of Typical with the three files it changed, which the Revision state opens. */
const typicalRevisionDiff = {
  revision: typicalRevisions.items[0],
  resources: [
    {
      file: 'pages/request-list.json',
      change: 'changed',
      id: pageId,
      kind: 'page',
      name: 'RequestList',
      before: JSON.stringify({
        kind: 'page',
        name: 'RequestList',
        title: { textKey: 'requestList.title' },
        widgets: [],
      }),
      after: JSON.stringify({
        kind: 'page',
        name: 'RequestList',
        title: { textKey: 'requestList.title' },
        widgets: [{ type: 'table', name: 'requests', entity: 'Request' }],
      }),
    },
    {
      file: 'roles/approver.json',
      change: 'added',
      id: approverRoleId,
      kind: 'role',
      name: 'Approver',
      before: null,
      after: JSON.stringify({ kind: 'role', name: 'Approver', label: { textKey: 'role.approver' } }),
    },
    {
      file: 'forms/old-request.json',
      change: 'removed',
      id: null,
      kind: 'form',
      name: 'OldRequest',
      before: JSON.stringify({ kind: 'form', name: 'OldRequest', entity: 'Request', sections: [] }),
      after: null,
    },
  ],
} satisfies StudioRevisionDiff

/** Release 2 of Typical's history, which is live: it pins the Organization module and compares with release 1. */
const typicalRelease: StudioRelease = {
  number: 2,
  previous: 1,
  previousIsLive: false,
  live: true,
  changes: typicalReleases.changes,
  partChanges: [],
  modules: [{ id: 'organization', label: 'Organization', version: '1.2.0', previousVersion: '1.0.0' }],
  packages: [],
  blockers: [],
}

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
  releases: { active: null, folderContentHash: 'n1', items: [], changes: null, partChanges: null },
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
                  shared: true,
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
    [`${folderPath}/resource-activity`]: () => activity(folder),
    [`${folderPath}/diagnostics`]: () =>
      ({ diagnostics: answers.diagnostics }) satisfies { diagnostics: StudioDiagnostic[] },
    // A field's default has no field in scope, and no state types an expression, so the scope is empty.
    [`${folderPath}/expressions/scope`]: () => ({ type: null, fields: [], functions: [] }) satisfies ExpressionScope,
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
    [`${folderPath}/revisions`]: () => typicalRevisions,
    [`${folderPath}/revisions/${typicalRevisionId}`]: () => typicalRevisionDiff,
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

/** The id of the newest revision of Typical, which the Revision state opens at `&revision=<id>`. */
export const typicalRevisionId = typicalRevisions.items[0].id

/** The id of the Request entity of the Typical folder, which the Entity state opens at `&resource=<id>`. */
export const typicalEntityId = id(firstEntityId)

/** The id of the RequestList page of the Typical folder, which the page design card opens at `&resource=<id>`. */
export const typicalPageId = pageId

/** The id of the RequestForm form of the Typical folder, which the form design card opens at `&resource=<id>`. */
export const typicalFormId = formId

/** The id of the RequestApproval process of the Typical folder, which the process design card opens at `&resource=<id>`. */
export const typicalProcessId = processId

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
  releasePage: { ...workspaceRoutes(typicalFolder), [`${folderPath}/releases/2`]: () => typicalRelease },
  accessDenied: { [appsPath]: () => new Response(null, { status: 403 }) },
} satisfies Record<string, Record<string, () => unknown>>
