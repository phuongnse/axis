import type {
  StudioApplication,
  StudioAuthoringRequest,
  StudioAuthoringRequestDetail,
  StudioAuthoringRequestList,
  StudioAuthoringRequestStep,
  StudioBindings,
  StudioCatalog,
  StudioDiagnostic,
  StudioNameEntity,
  StudioNames,
  StudioReleases,
  StudioResource,
  StudioResourceItem,
} from '../platform/studio'
import type { FixtureRoute } from './fixtureFetch'
import { application, currentUser, locale } from './fixtures'

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
  changes: [{ path: 'pages/request-list.json', change: 'changed', kind: null, name: null, titles: null, locale: null }],
} satisfies StudioReleases

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
    [`${folderPath}/releases`]: () => releases,
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
  authoringRequest(3, 'accepted', 'Rename the Category field to Spending category.', '2026-11-07T10:05:00Z', false),
  authoringRequest(
    2,
    'failed',
    'Import the suppliers from the purchasing system every night and mark the ones that were removed there.',
    '2026-11-06T14:30:00Z',
    true,
  ),
  authoringRequest(1, 'dropped', 'Add a dashboard of spending by month.', '2026-11-05T09:10:00Z', true),
]

// Proposed: the plan of a request that waits for its author, in plain words: what will change, the modules and layers
// it uses, and the scenarios it adds, see docs/reference/ai-authoring-in-studio.md#the-plan.
const supplierPlan = [
  'Add a Supplier entity with a name, a tax code and an email address.',
  'Add a required Supplier field to the purchase request, chosen from the active suppliers.',
  'Show the supplier on the request form, on the request detail and as a column of the request list.',
  'Use no module: nothing built in keeps suppliers.',
  'Add two scenarios: a request saves with its supplier, and a request without one is refused.',
].join('\n')

const plans: Record<string, string> = { [aiRequests[0].id]: supplierPlan }

// What the preview's own sends and actions changed, so the list and each request's page follow them.
const sentRequests: { request: StudioAuthoringRequest; at: number }[] = []
const actedOn = new Map<string, { state: StudioAuthoringRequest['state']; at: number }>()
const planComments = new Map<string, string[]>()

// Proposed: the steps of a request's loop, each with its kind, its summary and its outcome. A fix round adds a fix and a
// second check, see docs/reference/authoring-requests.md#states.
const loop: [string, string, string][] = [
  ['plan', 'Read the application and wrote a plan', 'Plan written'],
  ['work', 'Made the change in a draft', 'Draft saved, 4 resources changed'],
  [
    'check',
    "Ran the compiler and the application's 8 scenarios",
    '1 scenario failed: a request without a supplier was saved',
  ],
  ['fix', 'Worked on what the checks found', 'Draft saved, 1 resource changed'],
  ['check', "Ran the compiler and the application's 8 scenarios", 'All 8 scenarios passed'],
  ['review', 'A second AI read the draft against the request and the plan', 'No findings'],
]

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
  return loop.slice(0, count).map(([kind, summary, outcome], index) => {
    const last = running && index === count - 1
    return {
      kind,
      summary,
      outcome: last ? null : outcome,
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
  const plan =
    state === 'planning'
      ? null
      : (plans[request.id] ??
        [
          `Change the application as asked: "${request.text}"`,
          'Use no module: nothing built in does this.',
          'Add one scenario that checks the change.',
        ].join('\n'))
  // Each change of plan is a `planChange` step whose summary is the comment, then the plan is written again.
  const comments = planComments.get(request.id) ?? []
  const steps =
    comments.length > 0 && (state === 'planning' || state === 'awaitingApproval')
      ? [
          ...loopSteps(1, false, start - 2 * gap, gap),
          ...comments.map((comment) => ({
            kind: 'planChange',
            summary: comment,
            outcome: null,
            tokens: 0,
            startedAt: new Date(start - gap).toISOString(),
            finishedAt: new Date(start - gap).toISOString(),
          })),
          ...loopSteps(1, state === 'planning', start, gap),
        ]
      : loopSteps(count, running, start, gap)
  return { ...request, state, round, plan, steps }
}

/** The routes of the open application with Ask AI on, its requests and whether it allows auto-accept. */
function askAiRoutes(list: StudioAuthoringRequestList, autoAccept: boolean): Record<string, FixtureRoute> {
  const all = () => [...sentRequests.map((item) => item.request), ...list.items]
  const one = (id: string) => (): unknown => {
    const request = all().find((item) => item.id === id)
    return request === undefined ? new Response(null, { status: 404 }) : asNow(request)
  }
  const act =
    (id: string, state: StudioAuthoringRequest['state']) =>
    (init?: RequestInit): unknown => {
      actedOn.set(id, { state, at: Date.now() })
      if (typeof init?.body === 'string') {
        const { comment } = JSON.parse(init.body) as { comment: string }
        planComments.set(id, [...(planComments.get(id) ?? []), comment])
      }
      return asNow(all().find((item) => item.id === id)!)
    }
  const routes: Record<string, FixtureRoute> = {
    ...workspaceRoutes(typicalFolder),
    [`${folderPath}/settings`]: () => ({ autoAccept }),
    [`${folderPath}/authoring-requests`]: (init) => {
      if (init?.method !== 'POST') {
        return { items: all().map(asNow), left: list.left } satisfies StudioAuthoringRequestList
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
  const requestRoutes = [...list.items.map((item) => item.id), ...sendable].flatMap((id) => [
    [`${folderPath}/authoring-requests/${id}`, one(id)],
    [`${folderPath}/authoring-requests/${id}/approve`, act(id, 'working')],
    [`${folderPath}/authoring-requests/${id}/change-plan`, act(id, 'planning')],
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
  askAi: askAiRoutes({ items: aiRequests, left: 12 }, true),
  askAiFirst: askAiRoutes({ items: [], left: null }, false),
  askAiNoneLeft: askAiRoutes({ items: aiRequests.slice(0, 3), left: 0 }, false),
} satisfies Record<string, Record<string, FixtureRoute>>
