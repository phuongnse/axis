import type {
  StudioApplication,
  StudioBindings,
  StudioCatalog,
  StudioDiagnostic,
  StudioNameEntity,
  StudioNames,
  StudioPolicyMatrix,
  StudioReleases,
  StudioResource,
  StudioResourceItem,
} from '../platform/studio'
import { application, locale } from './fixtures'

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
    resource(pageId, 'page', 'RequestList', 'pages/request-list.json', { [locale]: 'Requests' }, 1),
    resource(formId, 'form', 'RequestForm', 'forms/request-form.json', { [locale]: 'Request' }, 1),
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

function names(folder: Folder): StudioNames {
  const own = folder.bare !== true
  return {
    locales: [locale],
    entities: entityNames(folder),
    dataSources: [],
    processes: [],
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
  accessDenied: { [appsPath]: () => new Response(null, { status: 403 }) },
} satisfies Record<string, Record<string, () => unknown>>
