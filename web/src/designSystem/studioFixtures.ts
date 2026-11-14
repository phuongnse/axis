import type {
  StudioApplication,
  StudioAuthoringRequest,
  StudioAuthoringRequestDetail,
  StudioAiConnection,
  StudioAuthoringRequestList,
  StudioAuthoringRequestStep,
  StudioAuthoringStepChecks,
  StudioLiveChange,
  StudioRequest,
  StudioRevisionChange,
  StudioBindings,
  StudioCatalog,
  StudioDiagnostic,
  StudioNameEntity,
  StudioNames,
  StudioReleases,
  StudioResource,
  StudioResourceItem,
  StudioPlan,
  StudioReview,
} from '../platform/studio'
import type {
  NotificationFilter,
  NotificationItem,
  NotificationKind,
  NotificationPage,
} from '../platform/notifications'
import type { FixtureRoute } from './fixtureFetch'
import { application, currentUser, locale } from './fixtures'
import { requestTitle } from '../pages/authoringRequests'

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
/** The application's scenarios, which a request's checks run. */
const scenarioIds: Record<string, string> = { RequestSavesWithSupplier: id(7), RequestIsApproved: id(8) }
const firstEntityId = 100

/** What a state's folder holds: its applications, resources and names. */
interface Folder {
  apps: StudioApplication[]
  /** The title of the open application, which the application list shows too. */
  title: string
  entities: { name: string; title: string }[]
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
  return [
    resource(manifestId, 'application', application, 'application.json', {}),
    resource(siteId, 'site', 'Requests', 'sites/requests.json', { [locale]: folder.title }, 0),
    ...entityItems(folder),
    resource(pageId, 'page', 'RequestList', 'pages/request-list.json', { [locale]: 'Requests' }, 1),
    resource(formId, 'form', 'RequestForm', 'forms/request-form.json', { [locale]: 'Request' }, 1),
    resource(roleId, 'role', 'Requester', 'roles/requester.json', { [locale]: 'Requester' }, 1),
    resource(policyId, 'policy', 'RequesterPolicy', 'policies/requester.json', { [locale]: 'Requester' }),
    ...Object.entries(scenarioIds).map(([name, scenarioId]) =>
      resource(scenarioId, 'scenario', name, `scenarios/${name}.json`, {}),
    ),
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

// Proposed: the request entity and the request list, which a review's findings open, see the review on #3263.
const requestEntityResource: StudioResource = {
  ...resource(id(firstEntityId), 'entity', 'Request', 'entities/Request.json', { [locale]: 'Purchase request' }, 2),
  content: {
    id: id(firstEntityId),
    kind: 'entity',
    name: 'Request',
    formatVersion: 1,
    label: { textKey: 'request.label' },
    displayField: 'title',
    fields: [
      { name: 'title', type: 'text', required: true, maxLength: 200, label: { textKey: 'request.title' } },
      { name: 'amount', type: 'decimal', precision: 18, scale: 2, label: { textKey: 'request.amount' } },
      { name: 'costCentre', type: 'reference', target: 'CostCentre', label: { textKey: 'request.costCentre' } },
    ],
  },
}

const requestListResource: StudioResource = {
  ...resource(pageId, 'page', 'RequestList', 'pages/request-list.json', { [locale]: 'Requests' }, 1),
  content: {
    id: pageId,
    kind: 'page',
    name: 'RequestList',
    formatVersion: 1,
    title: { textKey: 'pages.requests.title' },
    widgets: [{ type: 'table', name: 'requests', entity: 'Request', columns: ['title', 'amount'] }],
  },
}

const diagnostics = [
  {
    code: 'AX0412',
    severity: 'warning',
    message: 'The policy grants no action on any entity.',
    file: 'policies/requester.json',
    path: '/grants',
    resourceId: policyId,
  },
] satisfies StudioDiagnostic[]

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

function names(folder: Folder): StudioNames {
  return {
    locales: [locale],
    entities: entityNames(folder),
    dataSources: [],
    processes: [],
    rules: [],
    connectors: [],
    events: [],
    roles: [{ id: roleId, name: 'Requester' }],
    sequences: [],
    forms: [{ id: formId, name: 'RequestForm', module: null }],
    pages: [{ id: pageId, name: 'RequestList', module: null, parameters: [] }],
    policies: [{ id: policyId, name: 'RequesterPolicy', module: null }],
    contracts: [],
    texts: [
      { key: 'application.title', texts: { [locale]: folder.title }, usedBy: 1 },
      { key: 'application.description', texts: { [locale]: 'Request purchases and approve them.' }, usedBy: 1 },
    ],
    seeds: [],
    triggers: [],
    scenarios: [],
    textResources: [],
  } satisfies StudioNames
}

const releases = {
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
  changes: [
    { path: 'entities/Request.json', change: 'changed', kind: null, name: null, titles: null, locale: null },
    { path: 'pages/request-list.json', change: 'changed', kind: null, name: null, titles: null, locale: null },
    { path: 'entities/Supplier.json', change: 'added', kind: null, name: null, titles: null, locale: null },
  ],
} satisfies StudioReleases

// The preview's publish, which runs for two seconds and then succeeds.
const publishRequestId = 'b0b00000-0000-4000-8000-000000000001'
let publishedAt = 0
const publishedReleaseId = '7c1d0000-0000-4000-8000-000000000003'
const published = () => publishedAt > 0 && Date.now() - publishedAt > 2000

/** The release history, with release 3 live once the preview's publish has succeeded. */
function releasesNow(): StudioReleases {
  if (!published()) {
    return releases
  }
  return {
    ...releases,
    active: { releaseId: publishedReleaseId, number: 3, contentHash: 'c3' },
    items: [
      {
        ...releases.items[0],
        releaseId: publishedReleaseId,
        number: 3,
        contentHash: 'c3',
        activatedAt: new Date(publishedAt).toISOString(),
        actorName: 'Alex Morgan',
        actor: '3f8e9d0c-1b2a-4c3d-8e4f-5a6b7c8d9e0f',
        label: null,
        note: null,
      },
      ...releases.items,
    ],
    changes: [],
  }
}
function publishRequest() {
  return {
    id: publishRequestId,
    application,
    kind: 'publish',
    target: 'tenant',
    status: published() ? 'succeeded' : 'running',
    label: null,
    note: null,
    releaseId: published() ? publishedReleaseId : null,
    diagnostics: [],
    scenarios: null,
  } satisfies StudioRequest
}

// Proposed: the diff from the live release, with who changed each file since, see the review on #3263. Alex changed
// the request list, Linh Tran the request entity and added Supplier, and an accepted AI request changed both of the first.
const renameRequest = {
  kind: 'request',
  request: 'a1e00000-0000-4000-8000-000000000003',
  text: 'Rename the Category field to Spending category.',
} as const
const purchaseRequestBefore = {
  kind: 'entity',
  name: 'Request',
  fields: [
    { name: 'title', type: 'text', required: true },
    { name: 'category', type: 'choice', label: 'Category', choices: ['Office', 'Travel', 'IT'] },
    { name: 'amount', type: 'money', currency: 'VND', required: true },
    { name: 'neededBy', type: 'date' },
    { name: 'notes', type: 'text', multiline: true },
  ],
}
const liveDiff: StudioLiveChange[] = [
  {
    file: 'entities/Request.json',
    change: 'changed',
    id: null,
    kind: 'entity',
    name: 'Request',
    before: JSON.stringify(purchaseRequestBefore),
    after: JSON.stringify({
      ...purchaseRequestBefore,
      fields: purchaseRequestBefore.fields
        .map((field) =>
          field.name === 'category'
            ? {
                ...field,
                name: 'spendingCategory',
                label: 'Spending category',
                choices: ['Office', 'Travel', 'IT', 'Training'],
              }
            : field,
        )
        .filter((field) => field.name !== 'notes'),
    }),
    by: [{ kind: 'person', id: '8a2b0c1d-4e5f-4a6b-9c7d-0e1f2a3b4c5d', name: 'Linh Tran' }, renameRequest],
  },
  {
    file: 'pages/request-list.json',
    change: 'changed',
    id: null,
    kind: 'page',
    name: 'RequestList',
    before: JSON.stringify({
      kind: 'page',
      name: 'RequestList',
      columns: ['title', 'category', 'amount'],
      pageSize: 20,
    }),
    after: JSON.stringify({
      kind: 'page',
      name: 'RequestList',
      columns: ['title', 'spendingCategory', 'amount', 'neededBy'],
      pageSize: 50,
    }),
    by: [renameRequest, { kind: 'person', id: '3f8e9d0c-1b2a-4c3d-8e4f-5a6b7c8d9e0f', name: 'Alex Morgan' }],
  },
  {
    file: 'entities/Supplier.json',
    change: 'added',
    id: null,
    kind: 'entity',
    name: 'Supplier',
    before: null,
    after: JSON.stringify({
      kind: 'entity',
      name: 'Supplier',
      fields: [
        { name: 'name', type: 'text', required: true },
        { name: 'taxCode', type: 'text' },
      ],
    }),
    by: [{ kind: 'person', id: '8a2b0c1d-4e5f-4a6b-9c7d-0e1f2a3b4c5d', name: 'Linh Tran' }],
  },
]

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
function workspaceRoutes(folder: Folder): Record<string, () => unknown> {
  return {
    [appsPath]: () => ({ items: folder.apps }) satisfies { items: StudioApplication[] },
    [`${folderPath}/resources`]: () => ({ items: items(folder) }) satisfies { items: StudioResourceItem[] },
    [`${folderPath}/diagnostics`]: () => ({ diagnostics }) satisfies { diagnostics: StudioDiagnostic[] },
    [`${folderPath}/names`]: () => names(folder),
    '/api/studio/catalog': () => catalog,
    '/api/studio/bindings': () => bindings,
    [`${folderPath}/relaxed`]: () => ({ items: [] }),
    [`${folderPath}/resources/${manifestId}`]: () => manifest,
    [`${folderPath}/resources/${id(firstEntityId)}`]: () => requestEntityResource,
    [`${folderPath}/resources/${pageId}`]: () => requestListResource,
    [`${folderPath}/releases`]: () => releasesNow(),
    [`${folderPath}/releases/live/diff`]: () => ({ resources: published() ? [] : liveDiff }),
    [`${folderPath}/publish`]: () => {
      publishedAt = Date.now()
      return publishRequest()
    },
    [`${folderPath}/requests/${publishRequestId}`]: () => publishRequest(),
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

// Proposed: the request panel's answers. The signed-in author is `currentUser`; another author sent some requests.
const otherAuthor = '8a2b0c1d-4e5f-4a6b-9c7d-0e1f2a3b4c5d'

// Proposed: the short names AI gave the preview's requests when it wrote their plans, see the review on #3263.
const titles: Record<number, string> = {
  7: 'Add a Cost centre field to purchase requests',
  6: 'Add a Supplier field to purchase requests',
  5: 'Finance manager approval over 50 million VND',
  4: 'Email the requester when a request is decided',
  3: 'Rename Category to Spending category',
  2: 'Import suppliers from the purchasing system nightly',
  1: 'A dashboard of spending by month',
}

/** The model of the preview's AI, which makes the changes asked for in Studio. */
const workingModel = 'Claude Sonnet 5.5'

function authoringRequest(
  number: number,
  state: StudioAuthoringRequest['state'],
  text: string,
  sentAt: string,
  mine: boolean,
): StudioAuthoringRequest {
  return {
    id: `a1e00000-0000-4000-8000-${number.toString(16).padStart(12, '0')}`,
    application,
    author: mine ? currentUser.id : otherAuthor,
    authorName: mine ? currentUser.displayName : 'Linh Tran',
    text,
    title: state === 'planning' ? null : (titles[number] ?? null),
    app: null,
    // Proposed: the model the author's AI used, see the review on #3263.
    coAuthors: [workingModel],
    state,
    plan: null,
    round: state === 'fixing' ? 2 : 1,
    tokens: 0,
    autoAccept: false,
    sentAt,
    approvedAt: null,
    finishedAt: null,
  }
}

// Proposed: the work of an outside AI app through the MCP server, see the review on #3263.
const appRequest: StudioAuthoringRequest = {
  ...authoringRequest(
    7,
    'ready',
    'Add a Cost centre field to the purchase request, chosen from a list, and show it in the request list.',
    '2026-11-10T09:15:00Z',
    true,
  ),
  app: 'Claude Code',
  coAuthors: ['Claude Code'],
}

const aiRequests: StudioAuthoringRequest[] = [
  authoringRequest(
    6,
    'awaitingApproval',
    'Add a Supplier field to the purchase request, chosen from the suppliers, and show it on the request list.',
    '2026-11-09T08:40:00Z',
    true,
  ),
  authoringRequest(
    5,
    'checking',
    'When a request is over 50 million VND, it also needs the finance manager to approve it.',
    '2026-11-09T07:55:00Z',
    false,
  ),
  authoringRequest(
    4,
    'ready',
    'Send the requester an email when their request is approved or rejected.',
    '2026-11-08T16:20:00Z',
    true,
  ),
  {
    ...authoringRequest(3, 'accepted', 'Rename the Category field to Spending category.', '2026-11-07T10:05:00Z', true),
    autoAccept: true,
  },
  authoringRequest(
    2,
    'failed',
    'Import the suppliers from the purchasing system every night and mark the ones that were removed there.',
    '2026-11-06T14:30:00Z',
    true,
  ),
  authoringRequest(1, 'dropped', 'Add a dashboard of spending by month.', '2026-11-05T09:10:00Z', true),
]

// Proposed: the plans AI wrote for the preview's requests, with no summary, as the request's words head its page, as a pull request's plan is written, see the review on #3263.
const supplierPlan: StudioPlan = {
  changes: [
    "A new Supplier entity keeps each supplier's name, tax code and email address, and whether it is active.",
    'Each purchase request gets a required Supplier field, chosen from the active suppliers.',
    'The request form, the request detail and the request list show the supplier.',
  ],
  decisions: [
    {
      decision: 'The Supplier field is required.',
      reason: 'A purchase needs a supplier, and the request did not say the field may be left empty.',
    },
    {
      decision: 'Only active suppliers can be chosen.',
      reason: 'A supplier that stopped trading should not be picked for a new purchase. Old requests keep theirs.',
    },
    {
      decision: 'The application gets its own Supplier entity, with no module.',
      reason: 'Nothing built into Axis keeps suppliers.',
    },
  ],
  risks: [
    'The 124 purchase requests already saved have no supplier. They stay as they are, and the field asks for one when a request is next saved.',
    'Someone must add the suppliers before people send new requests, or the form cannot be completed.',
    'The request list gets one more column, so it may scroll sideways on a narrow screen.',
  ],
  checks: [
    'A purchase request saves with its supplier.',
    'A purchase request without a supplier is refused, with a message on the Supplier field.',
    'Only active suppliers appear in the Supplier list.',
  ],
  details: [
    {
      resourceId: null,
      file: 'entities/supplier.json',
      name: 'Supplier',
      kind: 'entity',
      edits: [
        'A new entity Supplier: name (text, required), tax code (text), email address (email), active (yes or no).',
      ],
    },
    {
      resourceId: id(firstEntityId),
      file: 'entities/Request.json',
      name: null,
      kind: null,
      edits: ['A new field supplier: a reference to Supplier, required, limited to active suppliers.'],
    },
    {
      resourceId: formId,
      file: 'forms/request-form.json',
      name: null,
      kind: null,
      edits: ['The Supplier field after Title.'],
    },
    {
      resourceId: pageId,
      file: 'pages/request-list.json',
      name: null,
      kind: null,
      edits: ['A Supplier column after Title.'],
    },
    {
      resourceId: null,
      file: 'scenarios/request-supplier.json',
      name: 'Request supplier',
      kind: 'scenario',
      edits: ['Two new scenarios: a request with a supplier saves, and one without is refused.'],
    },
  ],
}

// Proposed: the plan an outside AI app proposed from its conversation with its person, see the review on #3263.
const appPlan: StudioPlan = {
  changes: [
    'Add a Cost centre entity with a name and a code.',
    'Add a Cost centre field to the purchase request, chosen from the cost centres.',
    'Show the cost centre as a column of the request list.',
  ],
  decisions: [{ decision: 'The Cost centre field is optional.', reason: 'The request did not say it is required.' }],
  risks: ['Requests already saved have no cost centre and keep none until someone sets one.'],
  checks: ['A purchase request saves with a cost centre, and without one.'],
  details: [
    {
      resourceId: null,
      file: 'entities/cost-centre.json',
      name: 'Cost centre',
      kind: 'entity',
      edits: ['A new entity CostCentre: name and code, both required.'],
    },
    {
      resourceId: id(firstEntityId),
      file: 'entities/Request.json',
      name: null,
      kind: null,
      edits: ['A new field costCentre: a reference to CostCentre.'],
    },
    {
      resourceId: pageId,
      file: 'pages/request-list.json',
      name: null,
      kind: null,
      edits: ['A Cost centre column after Title.'],
    },
  ],
}

const plans: Record<string, StudioPlan> = { [aiRequests[0].id]: supplierPlan, [appRequest.id]: appPlan }

/** A request's plan, or one that follows its words, for a request the preview writes no plan of its own for. */
function planOf(request: StudioAuthoringRequest): StudioPlan {
  return (
    plans[request.id] ?? {
      changes: ['The application changes as the request asks.'],
      decisions: [],
      risks: [],
      checks: ['One new scenario checks the change.'],
      details: [],
    }
  )
}

/**
 * Proposed: the review of a ready request: each plan step done, at the resource that does it, and no finding, unless
 * the preview gives the request a review of its own, see the review on #3263.
 */
function reviewOf(request: StudioAuthoringRequest, plan: StudioPlan | null): StudioReview {
  return (
    reviews[request.id] ?? {
      summary: 'The draft does what the request and the plan ask.',
      steps: (plan?.changes ?? []).map((step) => ({
        step,
        done: true,
        resourceId: null,
        file: null,
        name: null,
        kind: null,
        path: '',
      })),
      findings: [],
    }
  )
}

// What the preview's own sends and actions changed, so the list and each request's page follow them.
const sentRequests: { request: StudioAuthoringRequest; at: number }[] = []
const actedOn = new Map<string, { state: StudioAuthoringRequest['state']; at: number }>()
const planComments = new Map<string, string[]>()
const changeRequests = new Map<string, { comment: string; at: number }[]>()

/** The results of a step's checks: the compile, with `errors` errors, then `passed` of `total` scenarios, when they ran. */
function stepChecks(errors: number, scenarios?: [number, number]): StudioAuthoringStepChecks {
  return {
    compile: {
      outcome: errors > 0 ? 'failed' : 'passed',
      diagnostics: Array.from({ length: errors }, () => ({
        code: 'AX0412',
        severity: 'error' as const,
        message: 'The field costCentre refers to an entity that does not exist.',
        file: 'entities/Request.json',
        path: '/fields/2',
        resourceId: id(firstEntityId),
      })),
    },
    ...(scenarios === undefined
      ? {}
      : {
          scenarios: {
            outcome: scenarios[0] === scenarios[1] ? ('passed' as const) : ('failed' as const),
            items: Array.from({ length: scenarios[1] }, (_, index) => ({
              outcome: index < scenarios[0] ? 'passed' : 'failed',
            })),
          },
        }),
  }
}

/** A step of a fixture: its kind, its outcome code and the results of its checks, or the resource it writes. */
type FixtureStep = Pick<StudioAuthoringRequestStep, 'kind' | 'outcome' | 'checks' | 'resource'>

// Proposed: the steps of a request's loop, each with its kind, its outcome and its checks' results, which Studio puts in
// words. A fix round adds a fix and a second check, see docs/reference/authoring-requests.md#states.
const loop: FixtureStep[] = [
  { kind: 'plan', outcome: null },
  { kind: 'work', outcome: null },
  { kind: 'check', outcome: 'failed', checks: stepChecks(0, [7, 8]) },
  { kind: 'fix', outcome: null },
  { kind: 'check', outcome: 'passed', checks: stepChecks(0, [8, 8]) },
  { kind: 'review', outcome: 'passed', checks: { review: { outcome: 'passed' } } },
]

/**
 * Proposed: the tool calls an outside app made for its request, each with its outcome: its plan, which its author
 * approved in Studio, the writes it could make only then, each with its resource, the checks and the review.
 */
const appLoop: FixtureStep[] = [
  { kind: 'propose_plan', outcome: null },
  {
    kind: 'write_resource',
    outcome: 'saved',
    resource: { resourceId: null, file: 'entities/cost-centre.json', name: 'Cost centre', kind: 'entity' },
  },
  {
    kind: 'write_resource',
    outcome: 'saved',
    resource: { resourceId: id(firstEntityId), file: 'entities/Request.json', name: null, kind: null },
  },
  {
    kind: 'write_resource',
    outcome: 'saved',
    resource: { resourceId: pageId, file: 'pages/request-list.json', name: null, kind: null },
  },
  { kind: 'validate_draft', outcome: 'passed', checks: stepChecks(0) },
  { kind: 'run_scenarios', outcome: 'passed', checks: { scenarios: stepChecks(0, [2, 2]).scenarios } },
  { kind: 'review', outcome: 'passed', checks: { review: { outcome: 'passed' } } },
]

/** The steps of an outside app's request, a minute apart from its sending. */
function appSteps(request: StudioAuthoringRequest): StudioAuthoringRequestStep[] {
  return appLoop.map((step, index) => ({
    ...step,
    summary: null,
    tokens: 0,
    startedAt: new Date(Date.parse(request.sentAt) + index * 60_000).toISOString(),
    finishedAt: new Date(Date.parse(request.sentAt) + (index + 1) * 60_000).toISOString(),
  }))
}

/** How many steps of the loop a state has started, and whether the last one still runs. */
const progress: Record<StudioAuthoringRequest['state'], [number, boolean]> = {
  planning: [1, true],
  awaitingApproval: [1, false],
  working: [2, true],
  checking: [3, true],
  fixing: [4, true],
  reviewing: [6, true],
  ready: [6, false],
  accepted: [6, false],
  dropped: [1, false],
  failed: [3, false],
}

/** After an approval the preview's request moves on every four seconds, through one fix round, to `ready`. */
const afterApproval: [StudioAuthoringRequest['state'], number, number, boolean][] = [
  ['working', 1, 2, true],
  ['checking', 1, 3, true],
  ['fixing', 2, 4, true],
  ['checking', 2, 5, true],
  ['reviewing', 2, 6, true],
  ['ready', 2, 6, false],
]

/** The first `count` steps of the loop, `gap` milliseconds apart from `start`, the last one running when asked. */
function loopSteps(count: number, running: boolean, start: number, gap: number): StudioAuthoringRequestStep[] {
  return loop.slice(0, count).map((step, index) => {
    const last = running && index === count - 1
    return {
      ...step,
      summary: null,
      outcome: last ? null : step.outcome,
      checks: last ? null : step.checks,
      tokens: 0,
      startedAt: new Date(start + index * gap).toISOString(),
      finishedAt: last ? null : new Date(start + (index + 1) * gap).toISOString(),
    }
  })
}

/**
 * A request as it is now, with its steps. A sent request, or one whose plan was changed, writes its plan for four
 * seconds, then waits for its author with a plan made of its words. An approved one then moves on through its loop.
 */
function asNow(request: StudioAuthoringRequest): StudioAuthoringRequestDetail {
  const sent = sentRequests.find((item) => item.request.id === request.id)
  const acted = actedOn.get(request.id)
  let { state, round } = request
  let [count, running] = progress[state]
  let start = Date.parse(request.sentAt)
  let gap = 60_000
  if (sent !== undefined || acted !== undefined) {
    const since = acted?.at ?? sent!.at
    const elapsed = Date.now() - since
    const from = acted?.state ?? 'planning'
    gap = 4000
    start = from === 'working' ? since - gap : since
    if (from === 'working') {
      ;[state, round, count, running] = afterApproval[Math.min(afterApproval.length - 1, Math.floor(elapsed / gap))]
    } else {
      state = from === 'planning' ? (elapsed > gap ? 'awaitingApproval' : 'planning') : from
      ;[count, running] = progress[state]
    }
  }
  const plan = state === 'planning' ? null : planOf(request)
  // Each change of plan is a `planChange` step whose summary is the comment, then the plan is written again.
  const comments = planComments.get(request.id) ?? []
  const steps =
    comments.length > 0 && (state === 'planning' || state === 'awaitingApproval')
      ? [
          ...loopSteps(1, false, start - 2 * gap, gap),
          ...comments.map((comment) => ({
            kind: 'planChange' as const,
            summary: comment,
            outcome: null,
            tokens: 0,
            startedAt: new Date(start - gap).toISOString(),
            finishedAt: new Date(start - gap).toISOString(),
          })),
          ...loopSteps(1, state === 'planning', start, gap),
        ]
      : changeRequests.has(request.id)
        ? [
            // An ask for changes follows the ready request's steps, and the loop runs again from the work.
            ...(request.app !== null ? appSteps(request) : loopSteps(6, false, Date.parse(request.sentAt), 60_000)),
            ...changeRequests.get(request.id)!.map(({ comment, at }) => ({
              kind: 'changeRequest' as const,
              summary: comment,
              outcome: null,
              tokens: 0,
              startedAt: new Date(at).toISOString(),
              finishedAt: new Date(at).toISOString(),
            })),
            ...loopSteps(count, running, start, gap).slice(1),
          ]
        : loopSteps(count, running, start, gap)
  // An outside app's request records its tool calls as its steps, from the plan it proposed to the review.
  // The app, not Axis, works on: after an ask for changes it waits for the app, and a drop ends it.
  if (request.app !== null && sent === undefined && acted === undefined) {
    const appAccept = acceptOf(request)
    const appState = appAccept?.status === 'succeeded' ? 'accepted' : state
    const appDiff = draftChanges[request.id] ?? genericChanges(request)
    return {
      ...request,
      state: appState,
      plan: appPlan,
      aiConnected: true,
      steps: appSteps(request),
      draft: appState === 'accepted' ? null : draftOf(request.id),
      checks: checksOf(request, appState),
      // Its author has their own AI here, so a second AI reviewed it.
      review: appState === 'ready' ? reviewOf(request, appPlan) : null,
      changes: appDiff.map(({ file, summary }) => ({ file, summary })),
      failure: null,
      accept: appAccept,
    }
  }
  const accept = acceptOf(request)
  if (accept?.status === 'succeeded') {
    state = 'accepted'
  }
  const hasDraft = !['planning', 'awaitingApproval', 'dropped', 'accepted'].includes(state)
  const diff = draftChanges[request.id] ?? genericChanges(request)
  return {
    ...request,
    // Axis's agent joins an outside app's request as its co-author once the author asks for changes here.
    coAuthors:
      changeRequests.has(request.id) && !request.coAuthors.includes(workingModel)
        ? [...request.coAuthors, workingModel]
        : request.coAuthors,
    state,
    round,
    plan,
    steps,
    // The preview's author has connected their own AI.
    aiConnected: true,
    draft: hasDraft ? draftOf(request.id) : null,
    checks: checksOf(request, state),
    review: state === 'ready' ? reviewOf(request, plan) : null,
    changes: diff.map(({ file, summary }) => ({ file, summary })),
    failure: state === 'failed' ? (failures[request.id] ?? null) : null,
    accept,
  }
}

// Proposed: what a request's draft changed, its checks, its review and the accepts of the preview, see the review on
// #3263. A draft's id follows its request's.
const draftOf = (id: string) => id.replace('a1e00000', 'd1a00000')
const json = (value: unknown) => JSON.stringify(value)

/** The labels of the resources a draft changes, which name them as people read them; a scenario has none. */
const changeTitles: Record<string, string> = {
  CostCentre: 'Cost centre',
  PurchaseRequest: 'Purchase request',
  Supplier: 'Supplier',
  RequestList: 'Requests',
  PurchaseApproval: 'Purchase approval',
}

function change(
  file: string,
  kind: string,
  name: string,
  before: unknown,
  after: unknown,
  summary: string,
): StudioRevisionChange & { summary: string } {
  return {
    file,
    change: before === null ? 'added' : after === null ? 'removed' : 'changed',
    id: null,
    kind,
    name,
    titles: Object.hasOwn(changeTitles, name) ? { [locale]: changeTitles[name] } : {},
    before: before === null ? null : json(before),
    after: after === null ? null : json(after),
    summary,
  }
}

// A process long enough that its diff folds the unchanged lines. The email request changes a line, removes one and
// adds a step, so its diff shows all three.
const approvalProcess = (emails: boolean) => ({
  kind: 'process',
  name: 'PurchaseApproval',
  entity: 'PurchaseRequest',
  start: 'submitted',
  steps: [
    emails
      ? { name: 'submit', kind: 'task', assignee: '=requester', form: 'RequestForm' }
      : { name: 'submit', kind: 'task', assignee: '=requester', form: 'RequestForm', notify: false },
    { name: 'review', kind: 'task', assignee: 'role:Manager', form: 'ReviewForm', due: 'P2D' },
    {
      name: 'approve',
      kind: 'decision',
      assignee: 'role:Approver',
      outcomes: ['approved', 'rejected'],
      next: emails ? 'notifyRequester' : 'end',
    },
    ...(emails ? [{ name: 'notifyRequester', kind: 'email', to: '=requester.email', template: 'decisionMade' }] : []),
  ],
  end: ['approved', 'rejected'],
})

const requestEntity = { kind: 'entity', name: 'PurchaseRequest', fields: [{ name: 'title', type: 'text' }] }
const draftChanges: Record<string, (StudioRevisionChange & { summary: string })[]> = {
  [appRequest.id]: [
    change(
      'entities/cost-centre.json',
      'entity',
      'CostCentre',
      null,
      {
        kind: 'entity',
        name: 'CostCentre',
        fields: [
          { name: 'name', type: 'text', required: true },
          { name: 'code', type: 'text', required: true },
        ],
      },
      'A cost centre with a name and a code, for the list a request picks from.',
    ),
    change(
      'entities/purchase-request.json',
      'entity',
      'PurchaseRequest',
      requestEntity,
      {
        ...requestEntity,
        fields: [...requestEntity.fields, { name: 'costCentre', type: 'reference', entity: 'CostCentre' }],
      },
      'The purchase request gets a Cost centre field, chosen from the cost centres.',
    ),
  ],
  [aiRequests[0].id]: [
    change(
      'entities/supplier.json',
      'entity',
      'Supplier',
      null,
      { kind: 'entity', name: 'Supplier', fields: [{ name: 'name' }, { name: 'taxCode' }, { name: 'email' }] },
      'A new entity for suppliers, with a name, a tax code and an email address.',
    ),
    change(
      'entities/purchase-request.json',
      'entity',
      'PurchaseRequest',
      requestEntity,
      { ...requestEntity, fields: [...requestEntity.fields, { name: 'supplier', type: 'reference', required: true }] },
      'The purchase request gets a required Supplier field, chosen from the active suppliers.',
    ),
    change(
      'pages/request-list.json',
      'page',
      'RequestList',
      { kind: 'page', name: 'RequestList', columns: ['title', 'amount'] },
      { kind: 'page', name: 'RequestList', columns: ['title', 'supplier', 'amount'] },
      'The request list shows the supplier as a column after the title.',
    ),
    change(
      'scenarios/request-saves-with-supplier.json',
      'scenario',
      'RequestSavesWithSupplier',
      null,
      { kind: 'scenario', name: 'RequestSavesWithSupplier' },
      'A new scenario: a request saves with its supplier.',
    ),
  ],
  [aiRequests[2].id]: [
    change(
      'processes/purchase-approval.json',
      'process',
      'PurchaseApproval',
      approvalProcess(false),
      approvalProcess(true),
      'After the approval step, the process emails the requester whether the request was approved or rejected.',
    ),
    change(
      'scenarios/requester-is-told.json',
      'scenario',
      'RequesterIsTold',
      null,
      { kind: 'scenario', name: 'RequesterIsTold' },
      'A new scenario: the requester gets one email for an approval and one for a rejection.',
    ),
  ],
  [aiRequests[4].id]: [
    change(
      'connectors/purchasing-system.json',
      'connector',
      'PurchasingSystem',
      null,
      { kind: 'connector', name: 'PurchasingSystem' },
      'A connector to the purchasing system, with its address and sign-in.',
    ),
    change(
      'triggers/nightly-supplier-import.json',
      'trigger',
      'NightlySupplierImport',
      null,
      { kind: 'trigger', name: 'NightlySupplierImport', schedule: '0 2 * * *' },
      'A trigger that imports the suppliers every night at 2 AM.',
    ),
  ],
}

function genericChanges(request: StudioAuthoringRequest): (StudioRevisionChange & { summary: string })[] {
  return [
    change(
      'entities/purchase-request.json',
      'entity',
      'PurchaseRequest',
      requestEntity,
      { ...requestEntity, changed: true },
      `Changed as asked: "${request.text}"`,
    ),
  ]
}

function checksOf(request: StudioAuthoringRequest, state: StudioAuthoringRequest['state']): StudioRequest | null {
  if (state !== 'ready' && state !== 'failed') {
    return null
  }
  const scenario = (name: string, outcome: 'passed' | 'failed', message: string | null = null) => ({
    name,
    resourceId: scenarioIds[name] ?? 'a5e00000-0000-4000-8000-000000000001',
    file: `scenarios/${name}.json`,
    outcome,
    durationMs: 140,
    step: outcome === 'failed' ? 1 : null,
    path: null,
    message,
  })
  const failed = state === 'failed'
  return {
    id: request.id.replace('a1e00000', 'c4e00000'),
    application,
    kind: 'test',
    target: 'tenant',
    status: failed ? 'failed' : 'succeeded',
    label: null,
    note: null,
    releaseId: null,
    diagnostics:
      request.id === aiRequests[2].id
        ? [
            {
              code: 'AXC0102',
              severity: 'warning',
              message: 'The email has no Vietnamese text, so Vietnamese requesters get the English one.',
              file: 'processes/purchase-approval.json',
              path: '/steps/1/email',
              resourceId: null,
            },
          ]
        : [],
    scenarios: failed
      ? [scenario('NightlySupplierImport', 'failed', 'The purchasing system did not answer at its test address.')]
      : [scenario('RequestSavesWithSupplier', 'passed'), scenario('RequestIsApproved', 'passed')],
  }
}

// Proposed: a review's findings, see the review on #3263. The fix rounds of the request from an outside app left one
// blocking finding open, and each finding opens the resource it is about.
const reviews: Record<string, StudioReview> = {
  [appRequest.id]: {
    summary:
      'The draft adds the Cost centre entity and field as planned, but the request list shows no Cost centre column.',
    steps: [
      {
        step: 'Add a Cost centre entity with a name and a code.',
        done: true,
        resourceId: null,
        file: 'entities/cost-centre.json',
        name: 'Cost centre',
        kind: 'entity',
        path: '',
      },
      {
        step: 'Add a Cost centre field to the purchase request, chosen from the cost centres.',
        done: true,
        resourceId: id(firstEntityId),
        file: 'entities/Request.json',
        name: null,
        kind: null,
        path: '/fields/2',
      },
      {
        step: 'Show the cost centre as a column of the request list.',
        done: false,
        resourceId: pageId,
        file: 'pages/request-list.json',
        name: null,
        kind: null,
        path: '/widgets/0/columns',
      },
    ],
    findings: [
      {
        blocking: true,
        text: 'The request list shows no Cost centre column, while the request asked for one: the table lists no costCentre column.',
        file: 'pages/request-list.json',
        path: '/widgets/0/columns',
        resourceId: pageId,
      },
      {
        blocking: false,
        text: 'The Cost centre field could be required. The request did not say, so the draft leaves it optional.',
        file: 'entities/Request.json',
        path: '/fields/2',
        resourceId: id(firstEntityId),
      },
    ],
  },
}

const failures: Record<string, string> = {
  [aiRequests[4].id]:
    'Anthropic refused the request: the Anthropic account of your key has no credit left. Add credit in the Anthropic Console, or set another key on your account page, then send the request again.',
}

// The accepts the preview asked for, each running for two seconds. The email request meets a conflict the first time.
const accepts = new Map<string, { at: number; retried: boolean }>()
const retried = new Set<string>()

function acceptOf(request: StudioAuthoringRequest): StudioRequest | null {
  const asked = accepts.get(request.id)
  if (asked === undefined) {
    return null
  }
  const done = Date.now() - asked.at > 2000
  const conflict = request.id === aiRequests[2].id && !asked.retried
  if (done && !conflict) {
    actedOn.set(request.id, { state: 'accepted', at: asked.at })
  }
  return {
    id: request.id.replace('a1e00000', 'acc00000'),
    application,
    kind: 'accept',
    target: 'tenant',
    status: !done ? 'running' : conflict ? 'failed' : 'succeeded',
    label: null,
    note: null,
    releaseId: null,
    diagnostics:
      done && conflict
        ? [
            {
              code: 'AXC0377',
              severity: 'error',
              message:
                'PurchaseApproval (processes/purchase-approval.json): Linh Tran changed it on Nov 10 at 9:12 AM, after AI read it.',
              file: 'processes/purchase-approval.json',
              path: '',
              resourceId: null,
            },
          ]
        : [],
    scenarios: null,
  }
}

/**
 * Proposed: what the bell tells an author about their own AI request, see the review on #3263: its plan waits for
 * them, its change is ready, AI could not finish it, or its change was accepted by itself.
 */
const noticeSubjects: Partial<Record<StudioAuthoringRequest['state'], [string, NotificationKind]>> = {
  awaitingApproval: ['A plan awaits your approval', 'action'],
  ready: ['A change is ready to accept', 'action'],
  failed: ['AI could not finish your request', 'problem'],
  accepted: ['Your change was accepted', 'info'],
}

/**
 * The bell's notices, one for each request and state that told its author, kept as the server keeps them, after a
 * tenant notice of the workspace's own.
 */
const notices = new Map<
  string,
  NotificationItem & { request: string | null; state: StudioAuthoringRequest['state'] | null }
>([
  [
    'storage',
    {
      id: 'storage',
      releaseId: null,
      source: 'Workspace',
      kind: 'problem',
      subject: 'Storage is almost full',
      body: 'The workspace uses 9.2 GB of its 10 GB. Uploads stop when it is full.',
      open: null,
      read: true,
      done: false,
      createdAt: '2026-10-12T08:00:00Z',
      request: null,
      state: null,
    },
  ],
])

/** The notices the person cleared, which no list shows again. */
const cleared = new Set<string>()

/** Whether a notice is one the filter shows. */
function shows(filter: NotificationFilter, item: NotificationItem): boolean {
  return filter === 'unread' ? !item.read : filter === 'needsAction' ? item.kind === 'action' && !item.done : true
}

/**
 * Proposed: the bell's routes over the author's requests. A request of theirs that reaches a state the bell tells
 * adds a notice that opens its page, an auto-accepted one only when it was accepted by itself, and a notice is read
 * once its request has moved on, which also makes an `action` notice done, see the review on #3263. The notices of
 * requests sent before November 9 start read, so the ready request's notice is read but still needs its author. A
 * cleared notice is in no list again.
 */
function noticeRoutes(requests: () => StudioAuthoringRequestDetail[]): Record<string, FixtureRoute> {
  const page = (filter: NotificationFilter) => (): NotificationPage => {
    for (const request of requests()) {
      const told = noticeSubjects[request.state]
      const key = `${request.id}-${request.state}`
      const byItself = request.state !== 'accepted' || request.autoAccept
      // An outside app's request tells nothing, as its person is at the app, which hands them the request's page.
      if (
        request.author !== currentUser.id ||
        told === undefined ||
        !byItself ||
        notices.has(key) ||
        request.app !== null
      ) {
        continue
      }
      const [subject, kind] = told
      const sent = Date.parse(request.sentAt)
      notices.set(key, {
        id: key,
        releaseId: null,
        source: 'Purchase requests',
        kind,
        subject,
        body: requestTitle(request),
        open: { page: 'studio', parameters: { app: application, view: 'requests', request: request.id } },
        read: sent < Date.parse('2026-11-09T00:00:00Z'),
        done: false,
        createdAt: new Date(Number.isNaN(sent) ? Date.now() : sent + 20 * 60_000).toISOString(),
        request: request.id,
        state: request.state,
      })
    }
    const states = new Map(requests().map((request) => [request.id, request.state]))
    const items = [...notices.values()]
      .filter((notice) => !cleared.has(notice.id))
      .map(({ request, state, ...notice }) => {
        const movedOn = request !== null && states.get(request) !== state
        return { ...notice, read: notice.read || movedOn, done: notice.kind === 'action' && movedOn }
      })
      .sort((a, b) => b.createdAt.localeCompare(a.createdAt))
    const shown = items.filter((item) => shows(filter, item))
    return {
      items: shown,
      page: 1,
      pageSize: 10,
      totalCount: shown.length,
      unreadCount: items.filter((item) => !item.read).length,
    }
  }
  const clearAll = (filter: NotificationFilter) => () => {
    for (const item of page(filter)().items) {
      cleared.add(item.id)
    }
    return new Response(null, { status: 204 })
  }
  const markRead = (id: string) => () => {
    const notice = notices.get(id)
    if (notice !== undefined) {
      notice.read = true
    }
    return new Response(null, { status: 204 })
  }
  const ids = [
    appRequest,
    ...aiRequests,
    ...[0, 1, 2, 3].map((n) => authoringRequest(100 + n, 'planning', '', '', true)),
  ].flatMap((request) => Object.keys(noticeSubjects).map((state) => `${request.id}-${state}`))
  // The bell asks for its first page under each filter, which this answers in place of the shared empty one.
  return {
    '/api/notifications?page=1&pageSize=10': page('all'),
    '/api/notifications?page=1&pageSize=10&unread=true': page('unread'),
    '/api/notifications?page=1&pageSize=10&needsAction=true': page('needsAction'),
    '/api/notifications/read-all': () => {
      page('all')()
      notices.forEach((notice) => (notice.read = true))
      return new Response(null, { status: 204 })
    },
    '/api/notifications/clear-all': clearAll('all'),
    '/api/notifications/clear-all?unread=true': clearAll('unread'),
    '/api/notifications/clear-all?needsAction=true': clearAll('needsAction'),
    ...Object.fromEntries(['storage', ...ids].map((id) => [`/api/notifications/${id}/read`, markRead(id)])),
    ...Object.fromEntries(
      ['storage', ...ids].map((id) => [
        `/api/notifications/${id}/clear`,
        () => {
          cleared.add(id)
          return new Response(null, { status: 204 })
        },
      ]),
    ),
  }
}

/** The routes of the open application with Ask AI on, its requests, whether it allows auto-accept and whether AI is connected. */
function askAiRoutes(
  list: StudioAuthoringRequestList,
  autoAccept: boolean,
  connected = true,
): Record<string, FixtureRoute> {
  const all = () => [...sentRequests.map((item) => item.request), ...list.items]
  const one = (id: string) => (): unknown => {
    const request = all().find((item) => item.id === id)
    return request === undefined ? new Response(null, { status: 404 }) : asNow(request)
  }
  const act =
    (id: string, state: StudioAuthoringRequest['state'], asks: 'plan' | 'changes' = 'plan') =>
    (init?: RequestInit): unknown => {
      const at = Date.now()
      actedOn.set(id, { state, at })
      if (typeof init?.body === 'string') {
        const { comment } = JSON.parse(init.body) as { comment: string }
        if (asks === 'changes') {
          changeRequests.set(id, [...(changeRequests.get(id) ?? []), { comment, at }])
        } else {
          planComments.set(id, [...(planComments.get(id) ?? []), comment])
        }
      }
      return asNow(all().find((item) => item.id === id)!)
    }
  const routes: Record<string, FixtureRoute> = {
    ...workspaceRoutes(typicalFolder),
    ...noticeRoutes(() => all().map(asNow)),
    [`${folderPath}/settings`]: () => ({ autoAccept }),
    '/api/studio/ai': () => ({ connected }) satisfies StudioAiConnection,
    [`${folderPath}/authoring-requests`]: (init) => {
      if (init?.method !== 'POST') {
        return { items: all().map(asNow) } satisfies StudioAuthoringRequestList
      }
      const body = JSON.parse(String(init.body)) as { text: string; autoAccept?: boolean }
      const request = {
        ...authoringRequest(100 + sentRequests.length, 'planning', body.text, new Date().toISOString(), true),
        autoAccept: body.autoAccept === true,
      }
      sentRequests.unshift({ request, at: Date.now() })
      return request
    },
  }
  // The preview's own sends take the ids of requests 100 to 103.
  const sendable = [0, 1, 2, 3].map((n) => authoringRequest(100 + n, 'planning', '', '', true).id)
  const acceptRoute = (id: string) => (): unknown => {
    accepts.set(id, { at: Date.now(), retried: retried.has(id) })
    return acceptOf(all().find((item) => item.id === id)!)
  }
  const retry = (id: string) => (): unknown => {
    retried.add(id)
    accepts.delete(id)
    return act(id, 'working')()
  }
  const acceptStatus = (id: string) => (): unknown => acceptOf(all().find((item) => item.id === id)!)
  const requestRoutes = [...list.items.map((item) => item.id), ...sendable].flatMap((id) => [
    [`${folderPath}/authoring-requests/${id}/accept`, acceptRoute(id)],
    [`${folderPath}/authoring-requests/${id}/retry`, retry(id)],
    [`${folderPath}/requests/${id.replace('a1e00000', 'acc00000')}`, acceptStatus(id)],
    [
      `${folderPath}/drafts/${draftOf(id)}/diff`,
      () => {
        const request = all().find((item) => item.id === id)!
        const resources = (draftChanges[id] ?? genericChanges(request)).map(({ summary: _summary, ...entry }) => entry)
        return { start: 'a'.repeat(40), tip: 'b'.repeat(40), resources, cursor: null }
      },
    ],
    [`${folderPath}/authoring-requests/${id}`, one(id)],
    [`${folderPath}/authoring-requests/${id}/approve`, act(id, 'working')],
    [`${folderPath}/authoring-requests/${id}/change-plan`, act(id, 'planning')],
    [`${folderPath}/authoring-requests/${id}/request-changes`, act(id, 'working', 'changes')],
    [`${folderPath}/authoring-requests/${id}/drop`, act(id, 'dropped')],
  ])
  return { ...routes, ...(Object.fromEntries(requestRoutes) as Record<string, FixtureRoute>) }
}

/** The applications Studio lists in each state, with the routes of the open application where the state opens one. */
export const studioFixtures = {
  typical: workspaceRoutes(typicalFolder),
  empty: { [appsPath]: () => ({ items: [] }) satisfies { items: StudioApplication[] } },
  loading: { [appsPath]: () => new Promise<never>(() => {}) },
  failed: { [appsPath]: () => new Response(null, { status: 500 }) },
  manyRows: workspaceRoutes(manyFolder),
  longNames: workspaceRoutes(longFolder),
  accessDenied: { [appsPath]: () => new Response(null, { status: 403 }) },
  askAi: askAiRoutes({ items: [appRequest, ...aiRequests] }, true),
  askAiFirst: askAiRoutes({ items: [] }, false),
  // Proposed: an author who has not connected their own Claude account, see the review on #3263.
  askAiNotConnected: askAiRoutes({ items: [] }, false, false),
} satisfies Record<string, Record<string, FixtureRoute>>
