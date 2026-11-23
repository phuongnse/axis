import { EditorView } from '@codemirror/view'
import { ConfigProvider } from 'antd'
import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, Link, MemoryRouter, RouterProvider, useLocation } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { designTokensFor } from '../platform/designTokens'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import purchaseRequestApproval from '../../../samples/apps/purchase-requests/processes/purchase-request-approval.json'
import { isSignInNeeded } from '../platform/signInNeeded'
import { assignLocation } from '../platform/navigation'
import { ShellAsideContext } from '../platform/shellAsideContext'
import { ShellSidebarContext } from '../platform/shellSidebarContext'
import { ThemeModeProvider } from '../platform/theme'
import { TextProvider } from '../platform/TextProvider'
import { LeaveWithoutSaving, UnsavedChangesGuard } from '../platform/UnsavedChangesGuard'
import type {
  StudioApplication,
  StudioDiagnostic,
  StudioKindList,
  StudioReference,
  StudioReleases,
  StudioResourceItem,
} from '../platform/studio'
import { releasePage } from '../designSystem/studioFixtures'
import { PlatformContext, type Platform } from './context'
import { StudioPage } from './StudioPage'
import { editJson, jsonText } from '../test/jsonView'
import { chooseOption, isRequiredLabel } from '../test/antd'
import { openEntityField } from '../test/entityFields'

vi.mock('../platform/navigation', () => ({ assignLocation: vi.fn() }))

const entityId = 'd9ccbf89-5004-4d52-8885-2ee377b7caa5'
const formId = '5b6c7d8e-9f01-4a2b-8c3d-4e5f60718293'
const departmentId = '0192f4a7-3b1c-7e2d-9a4f-1c2d3e4f5a6b'
const manifestId = 'f0e1d2c3-b4a5-4968-8776-655443322110'
const employeeId = '0c5e2f1a-7d3b-4a6e-9f8c-2b1a0d9e8f7c'
const folder = '/api/studio/apps/PurchaseRequests'

const apps: (Partial<StudioApplication> & { name: string })[] = [
  {
    name: 'PurchaseRequests',
    titles: { en: 'Purchase requests', vi: 'Yêu cầu mua hàng' },
    descriptions: { en: 'Runs the purchases.', vi: 'Quản lý việc mua hàng.' },
  },
  { name: 'LeaveRequests', titles: { en: 'Leave requests' }, descriptions: {} },
  { name: 'Inventory' },
]

const resources: StudioResourceItem[] = [
  { id: departmentId, kind: 'entity', name: 'Department', file: 'entities/department.json', version: 'a', usedBy: 1 },
  {
    id: entityId,
    kind: 'entity',
    name: 'PurchaseRequest',
    file: 'entities/purchase-request.json',
    version: 'b',
    usedBy: 3,
  },
  {
    id: formId,
    kind: 'form',
    name: 'PurchaseRequestForm',
    file: 'forms/purchase-request.json',
    version: 'c',
    usedBy: 0,
  },
  { id: manifestId, kind: 'application', name: null, file: 'application.json', version: 'd', usedBy: 0 },
]

const departmentEntry: StudioReference = {
  relation: 'field target',
  file: 'entities/purchase-request.json',
  path: '/fields/3/target',
  node: { type: 'resource', id: departmentId, kind: 'entity', name: 'Department', file: 'entities/department.json' },
}

const purchaseRequestEntry: StudioReference = {
  relation: 'field target',
  file: 'entities/purchase-request.json',
  path: '/fields/3/target',
  node: {
    type: 'resource',
    id: entityId,
    kind: 'entity',
    name: 'PurchaseRequest',
    file: 'entities/purchase-request.json',
  },
}

const labelEntry: StudioReference = {
  relation: 'label text',
  file: 'entities/purchase-request.json',
  path: '/label/textKey',
  node: { type: 'text', key: 'purchaseRequest.label' },
}

/** What each resource uses and what uses it, as its references route answers. */
const referenceAnswers: Record<string, { uses: StudioReference[]; usedBy: StudioReference[] }> = {
  [departmentId]: { uses: [], usedBy: [purchaseRequestEntry] },
  [entityId]: { uses: [departmentEntry, labelEntry], usedBy: [] },
  [formId]: { uses: [], usedBy: [] },
}

const diagnostics: StudioDiagnostic[] = [
  {
    code: 'AXC0101',
    severity: 'error',
    message: 'The field type is unknown.',
    file: 'entities/purchase-request.json',
    path: '/fields/0/type',
    resourceId: entityId,
  },
  {
    code: 'AXC0001',
    severity: 'warning',
    message: 'The application has no description.',
    file: 'application.json',
    path: '/description',
    resourceId: null,
  },
]

const entityContent = () => ({
  id: entityId,
  kind: 'entity',
  name: 'PurchaseRequest',
  formatVersion: 1,
  label: { textKey: 'entity.purchaseRequest' },
  fields: [
    { name: 'number', type: 'text', label: { textKey: 'field.number' } },
    { name: 'title', type: 'text', label: { textKey: 'field.title' }, maxLength: 200 },
  ],
})

const savedDiagnostic: StudioDiagnostic = {
  code: 'AXC0777',
  severity: 'warning',
  message: 'The title is long.',
  file: 'entities/purchase-request.json',
  path: '/fields/1/maxLength',
  resourceId: entityId,
}

/** What the stubbed PATCH of the entity answers. */
interface PatchAnswer {
  status: number
  body: unknown
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

const namesPath = `${folder}/names`

// The first lookup waits for the schema chunk and two fetches, which is slow while other test files run.
const slow = { timeout: 5000 }

const applicationNames = {
  locales: ['en'],
  entities: [{ id: entityId, name: 'PurchaseRequest', module: null, fields: [{ name: 'title', type: 'text' }] }],
  dataSources: [],
  processes: [],
  rules: [],
  connectors: [],
  events: [],
  roles: [],
  sequences: [],
  forms: [],
  pages: [],
  policies: [],
  contracts: [],
  texts: [{ key: 'entity.purchaseRequest', texts: { en: 'Purchase request' }, usedBy: 1 }],
  seeds: [],
  triggers: [],
  scenarios: [],
  textResources: [],
}

/** While `down` is set, the entity routes, the names and `/api/me` fail: with no answer when `status` is null, else with `status`. */
interface Outage {
  down: { status: number | null } | null
}

/** What the expression stubs answer: the entity's content, and the answer of each check in order. */
interface ExpressionStub {
  content?: Record<string, unknown>
  check?: (call: number) => Response
  /** The content of `application.json`. */
  manifest?: Record<string, unknown>
}

/** The page that `GET .../kinds/{kind}/resources` answers: the folder's resources of that kind, titled by label, else name. */
function kindList(input: string): StudioKindList {
  const kind = input.slice(`${folder}/kinds/`.length).split('/')[0]
  const items = resources
    .filter((item) => item.kind === kind)
    .map((item) => ({
      id: item.id,
      title: item.titles?.en ?? item.name ?? kind,
      locale: item.locale ?? null,
      description: null,
      module: null,
      moduleLabel: null,
      status: null,
      modifiedBy: null,
      modifiedAt: null,
    }))
  return { items, totalCount: items.length }
}

/** Stubs `fetch` with the Studio list routes, the read, patch and delete of the purchase request entity, and its references. `apps` is the status or list of `/api/studio/apps`. */
function stubServer(
  apps: (Partial<StudioApplication> & { name: string })[] | number = [{ name: 'PurchaseRequests' }],
  patch: PatchAnswer = { status: 200, body: { version: 'b2', diagnostics: [savedDiagnostic] } },
  outage: Outage = { down: null },
  expressions: ExpressionStub = {},
) {
  let checkCalls = 0
  const entityPath = `${folder}/resources/${entityId}`
  const manifestPath = `${folder}/resources/${manifestId}`
  const fetchMock = vi.fn(async (input: string, init?: RequestInit): Promise<Response> => {
    if ((input === entityPath || input === namesPath || input === '/api/me') && outage.down) {
      if (outage.down.status === null) {
        throw new TypeError('Failed to fetch')
      }
      return new Response(null, { status: outage.down.status })
    }
    if (input === '/api/me') {
      return json({ id: 'author', studio: true })
    }
    if (input === entityPath && init?.method === 'PATCH') {
      return json(patch.body, patch.status)
    }
    if (input === entityPath && init?.method === 'DELETE') {
      return json({ diagnostics: [] })
    }
    if (input === entityPath) {
      return json({ ...resources[1], content: expressions.content ?? entityContent() })
    }
    if (input === manifestPath) {
      return json({
        ...resources[3],
        content: expressions.manifest ?? { id: manifestId, kind: 'application', name: 'PurchaseRequests' },
      })
    }
    if (input === namesPath) {
      return json(applicationNames)
    }
    if (input === '/api/studio/apps') {
      return typeof apps === 'number'
        ? json({ title: 'No.' }, apps)
        : json({ items: apps.map((app) => ({ titles: {}, descriptions: {}, ...app })) })
    }
    if (input === `${folder}/resources`) {
      return json({ items: resources })
    }
    if (input === `${folder}/releases`) {
      return json({ active: null, folderContentHash: null, changes: null, items: [] })
    }
    // The server builds the release list from the same history, so a test that changes the history changes the list.
    if (input === `${folder}/release-list`) {
      return json(releasePage((await (await fetchMock(`${folder}/releases`)).json()) as StudioReleases))
    }
    if (input === `${folder}/diagnostics`) {
      return json({ diagnostics })
    }
    if (input === `${folder}/revisions`) {
      return json({
        items: [
          {
            id: 'r2',
            summary: 'Added a field',
            author: 'Linh Nguyen',
            userId: 'u1',
            client: 'studio',
            request: null,
            time: '2026-11-02T09:30:00Z',
          },
          {
            id: 'r1',
            summary: 'Created the application',
            author: 'Linh Nguyen',
            userId: 'u1',
            client: 'studio',
            request: null,
            time: '2026-11-01T08:00:00Z',
          },
        ],
        page: 1,
        pageSize: 20,
        totalCount: 2,
        start: 'r1',
      })
    }
    if (input === `${folder}/names`) {
      return json(applicationNames)
    }
    if (input === `${folder}/resource-activity`) {
      return json({ items: [] })
    }
    if (input.startsWith(`${folder}/kinds/`)) {
      return json(kindList(input))
    }
    if (input === '/api/studio/catalog') {
      return json({ modules: [], packages: [], timeZones: [] })
    }
    if (input === `${folder}/settings`) {
      return json({ autoAccept: false })
    }
    if (input === `${folder}/policy-matrix`) {
      return json({ targets: [], roles: [] })
    }
    if (input === `${folder}/package-files`) {
      return json({ items: [] })
    }
    if (input === `${folder}/expressions/check` && expressions.check) {
      checkCalls++
      return expressions.check(checkCalls)
    }
    const answer = Object.entries(referenceAnswers).find(([id]) => input === `${folder}/resources/${id}/references`)
    if (answer) {
      return json(answer[1])
    }
    return new Response(null, { status: 404 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function Location() {
  const location = useLocation()
  return <div data-testid="location">{location.pathname + location.search}</div>
}

let sidebar: HTMLDivElement | undefined
let aside: HTMLDivElement | undefined

/** Adds the shell's sidebar and right column to the document, as the page's tests draw them. */
function addShellRegions() {
  sidebar = document.createElement('div')
  sidebar.dataset.testid = 'sidebar'
  aside = document.createElement('div')
  aside.dataset.testid = 'aside'
  document.body.append(sidebar, aside)
}

/** Renders the page with a shell sidebar. The sidebar's content goes into `sidebar`, and the returned spy counts its claims. */
function renderPage(entry = '/studio?app=PurchaseRequests', locale?: string) {
  const claim = vi.fn(() => () => {})
  addShellRegions()
  const page = (
    // Without motion a closed dialog leaves the document at once, as jsdom runs no animations.
    <ConfigProvider theme={{ token: { motion: false } }}>
      <ThemeModeProvider>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <ShellSidebarContext.Provider value={{ target: sidebar!, collapsed: false, claim }}>
            <ShellAsideContext.Provider value={{ target: aside!, claim: () => () => {} }}>
              <MemoryRouter initialEntries={[entry]}>
                <StudioPage />
                <Location />
              </MemoryRouter>
            </ShellAsideContext.Provider>
          </ShellSidebarContext.Provider>
        </TextProvider>
      </ThemeModeProvider>
    </ConfigProvider>
  )
  render(
    locale === undefined ? (
      page
    ) : (
      <PlatformContext.Provider value={{ current: { locale, texts: {} } } as Platform}>{page}</PlatformContext.Provider>
    ),
  )
  return claim
}

/**
 * Renders the page under the guard of docs/reference/studio.md#editor, in a data router that holds `entries` with the
 * last one open. A link to `/` stands for a link in the shell. Returns the router.
 */
function renderGuardedPage(entries: string[]) {
  addShellRegions()
  const router = createMemoryRouter(
    [
      {
        path: '*',
        element: (
          <ConfigProvider theme={{ token: { motion: false } }}>
            <ThemeModeProvider>
              <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
                <ShellSidebarContext.Provider value={{ target: sidebar!, collapsed: false, claim: () => () => {} }}>
                  <ShellAsideContext.Provider value={{ target: aside!, claim: () => () => {} }}>
                    <UnsavedChangesGuard>
                      <StudioPage />
                      <Location />
                      <Link to="/">Home</Link>
                      <LeaveWithoutSaving />
                    </UnsavedChangesGuard>
                  </ShellAsideContext.Provider>
                </ShellSidebarContext.Provider>
              </TextProvider>
            </ThemeModeProvider>
          </ConfigProvider>
        ),
      },
    ],
    { initialEntries: entries, initialIndex: entries.length - 1 },
  )
  render(<RouterProvider router={router} />)
  return router
}

/** Opens the Problems panel from the band's problems button and returns it. */
async function openProblems() {
  await userEvent.click(await screen.findByTestId('studio-problems'))
  return screen.getByTestId('studio-diagnostics')
}

afterEach(() => {
  vi.unstubAllGlobals()
  sidebar?.remove()
  sidebar = undefined
  aside?.remove()
  aside = undefined
})

describe('StudioPage', () => {
  it('the Overview shows the application heading and lists no resources', async () => {
    stubServer()
    renderPage()

    expect(await screen.findByRole('heading', { level: 1, name: 'PurchaseRequests' })).toBeInTheDocument()
    expect(screen.queryByTestId('studio-group-entity')).not.toBeInTheDocument()
    expect(screen.queryByTestId(`studio-resource-${entityId}`)).not.toBeInTheDocument()
    expect(screen.queryByTestId('studio-resource-details')).not.toBeInTheDocument()
  })

  it('&kind=entity lists only the entities under the kind name', async () => {
    stubServer()
    renderPage('/studio?app=PurchaseRequests&kind=entity')

    expect(await screen.findByRole('heading', { level: 1, name: 'Entities' })).toBeInTheDocument()
    const entities = await screen.findByTestId('studio-group-entity')
    expect((await within(entities).findAllByRole('link')).map((link) => link.textContent)).toEqual([
      'Department',
      'PurchaseRequest',
    ])
    expect(screen.queryByTestId('studio-group-form')).not.toBeInTheDocument()
    expect(screen.queryByTestId(`studio-resource-${formId}`)).not.toBeInTheDocument()
  })

  it('shows no off-standard text in the band when only application.json has a relax entry', async () => {
    const fetchMock = stubServer(undefined, undefined, undefined, {
      manifest: {
        modules: [
          {
            id: 'approvals',
            version: '^1.0.0',
            relax: [{ point: 'Request/transitions/approve', reason: 'Small team' }],
          },
        ],
      },
    })
    renderPage()

    expect(await screen.findByText('Not published yet')).toBeInTheDocument()
    await waitFor(() =>
      expect(fetchMock.mock.calls.some(([input]) => input === `${folder}/resources/${manifestId}`)).toBe(true),
    )
    expect(screen.getByTestId('studio-band')).not.toHaveTextContent(/off-standard/i)
  })

  it.each([
    ['the Releases page', '&view=releases', true],
    ['the Overview', '', false],
    ['a resource', `&resource=${entityId}`, false],
  ])('shows the release table on %s only when it is the Releases page', async (_name, view, shown) => {
    stubServer()
    renderPage(`/studio?app=PurchaseRequests${view}`)

    expect(await screen.findByText('Not published yet')).toBeInTheDocument()
    if (shown) {
      expect(screen.getByTestId('studio-releases')).toBeInTheDocument()
    } else {
      expect(screen.queryByTestId('studio-releases')).not.toBeInTheDocument()
    }
  })

  it('shows the details of a resource and keeps it in the address without its kind', async () => {
    stubServer()
    renderPage('/studio?app=PurchaseRequests&kind=entity')

    await userEvent.click(await screen.findByTestId(`studio-resource-${entityId}`))

    const details = screen.getByTestId('studio-resource-details')
    expect(details).toHaveTextContent('PurchaseRequest')
    expect(details).toHaveTextContent('Entity')
    expect(details).toHaveTextContent(entityId)
    expect(details).toHaveTextContent('entities/purchase-request.json')
    // The kind is the line of the heading, and the id and the file are the page's first part.
    expect(within(screen.getByTestId('page-heading')).getByText('Entity')).toBeInTheDocument()
    const facts = within(screen.getByTestId('studio-section-facts'))
    expect(facts.getByText('ID', { selector: 'dt' })).toBeInTheDocument()
    expect(facts.getByText(entityId, { selector: 'dd' })).toBeInTheDocument()
    expect(facts.getByText('entities/purchase-request.json', { selector: 'dd' })).toBeInTheDocument()
    expect(facts.queryByText('Kind')).not.toBeInTheDocument()
    expect(screen.getByTestId('location')).toHaveTextContent(`/studio?app=PurchaseRequests&resource=${entityId}`)
    expect(screen.queryByTestId('studio-group-entity')).not.toBeInTheDocument()
  })

  it('opens the resource named in the address', async () => {
    stubServer()
    renderPage(`/studio?app=PurchaseRequests&resource=${formId}`)

    await waitFor(() =>
      expect(screen.getByTestId('studio-resource-details')).toHaveTextContent('forms/purchase-request.json'),
    )
  })

  it('opens an entity in the entity designer', async () => {
    stubServer()
    renderPage(`/studio?app=PurchaseRequests&resource=${entityId}`)

    await openEntityField(0)
    const name = screen.getByTestId('studio-field-/fields/0/name')
    expect(within(name).getByRole('textbox')).toHaveValue('number')
    await openEntityField(1)
    expect(within(screen.getByTestId('studio-field-/fields/1/maxLength')).getByRole('spinbutton')).toHaveValue('200')
    const label = screen.getByTestId('studio-field-/label')
    expect(within(label).queryByRole('textbox', { name: 'Text key' })).toBeNull()
    expect(within(label).getByRole('textbox', { name: 'English' })).toHaveValue('Purchase request')
    expect(screen.getByTestId('studio-entity-designer')).toBeInTheDocument()
  })

  it('sends only the changed value, with the loaded version', async () => {
    const fetchMock = stubServer()
    renderPage(`/studio?app=PurchaseRequests&resource=${entityId}`)
    await openEntityField(1)
    const maxLength = within(screen.getByTestId('studio-field-/fields/1/maxLength')).getByRole('spinbutton')
    expect(screen.queryByTestId('studio-save-bar')).not.toBeInTheDocument()
    await userEvent.clear(maxLength)
    await userEvent.type(maxLength, '250')
    expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled()
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    const patches = fetchMock.mock.calls.filter(([, init]) => init?.method === 'PATCH')
    expect(patches).toHaveLength(1)
    const [, init] = patches[0]
    expect(JSON.parse(init!.body as string)).toEqual([{ op: 'replace', path: '/fields/1/maxLength', value: 250 }])
    const headers = new Headers(init!.headers)
    expect(headers.get('If-Match')).toBe('"b"')
    expect(headers.get('Content-Type')).toBe('application/json-patch+json')
    expect(screen.queryByTestId('studio-save-bar')).not.toBeInTheDocument()
  })

  it('shows the diagnostics of the save answer in place of the loaded ones', async () => {
    stubServer()
    renderPage(`/studio?app=PurchaseRequests&resource=${entityId}`)
    const panel = await openProblems()
    expect(within(panel).getByText('AXC0101')).toBeInTheDocument()

    await openEntityField(1)
    const maxLength = within(screen.getByTestId('studio-field-/fields/1/maxLength')).getByRole('spinbutton')
    await userEvent.clear(maxLength)
    await userEvent.type(maxLength, '250')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await within(panel).findByText('AXC0777')).toBeInTheDocument()
    expect(within(panel).queryByText('AXC0101')).not.toBeInTheDocument()
    expect(within(panel).queryByText('AXC0001')).not.toBeInTheDocument()
  })

  it('keeps the draft on a conflict until the author reloads', async () => {
    const fetchMock = stubServer(undefined, { status: 409, body: { title: 'Conflict.' } })
    renderPage(`/studio?app=PurchaseRequests&resource=${entityId}`)
    await openEntityField(1)
    const maxLength = within(screen.getByTestId('studio-field-/fields/1/maxLength')).getByRole('spinbutton')
    await userEvent.clear(maxLength)
    await userEvent.type(maxLength, '250')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    const conflict = await screen.findByTestId('studio-conflict')
    expect(conflict).toHaveTextContent('Someone else changed this file.')
    expect(maxLength).toHaveValue('250')
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled()
    const reads = () => fetchMock.mock.calls.filter(([input, init]) => input.endsWith(entityId) && !init?.method)

    const before = reads().length
    await userEvent.click(screen.getByRole('button', { name: 'Reload' }))

    await waitFor(() => expect(screen.queryByTestId('studio-conflict')).not.toBeInTheDocument())
    expect(reads()).toHaveLength(before + 1)
    await openEntityField(1)
    expect(within(screen.getByTestId('studio-field-/fields/1/maxLength')).getByRole('spinbutton')).toHaveValue('200')
  })

  it('shows an edit in the JSON view in the form, and keeps Save on for invalid JSON but sends nothing on a click', async () => {
    const fetchMock = stubServer()
    renderPage(`/studio?app=PurchaseRequests&resource=${entityId}`)
    await openEntityField(0)
    await userEvent.click(screen.getByRole('tab', { name: 'JSON' }))

    await editJson(JSON.stringify({ ...entityContent(), name: 'Renamed' }))
    await userEvent.click(screen.getByRole('tab', { name: 'Form' }))
    expect(within(screen.getByTestId('studio-field-/name')).getByRole('textbox')).toHaveValue('Renamed')
    expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled()

    await userEvent.click(screen.getByRole('tab', { name: 'JSON' }))
    await editJson('{ "name": ')

    expect(await screen.findByRole('button', { name: '1 problem' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled()
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    expect(fetchMock.mock.calls.filter(([, init]) => init?.method === 'PATCH')).toHaveLength(0)
  })

  it('shows each diagnostic with its resource as a link, and no path', async () => {
    stubServer()
    renderPage()

    const panel = await openProblems()
    expect(within(panel).getByText('AXC0101')).toBeInTheDocument()
    expect(within(panel).getByText('The field type is unknown.')).toBeInTheDocument()
    expect((await within(panel).findByRole('link', { name: 'PurchaseRequest' })).getAttribute('href')).toContain(
      `resource=${entityId}`,
    )
    expect(within(panel).getByText('application.json')).toBeInTheDocument()
    expect(within(panel).queryByText(/Path:/)).not.toBeInTheDocument()
  })

  it('selects the resource of a clicked diagnostic and shows its file', async () => {
    stubServer()
    renderPage()

    const panel = await openProblems()
    await userEvent.click(await within(panel).findByRole('link', { name: 'PurchaseRequest' }))

    expect(screen.getByTestId('studio-resource-details')).toHaveTextContent('entities/purchase-request.json')
    expect(screen.getByTestId('location')).toHaveTextContent(`resource=${entityId}`)
  })

  it('does not make a diagnostic without a resource clickable, and shows its file', async () => {
    stubServer()
    renderPage()

    const panel = await openProblems()
    await within(panel).findByText('The application has no description.')

    const item = within(panel).getByText('AXC0001').closest('li')!
    expect(within(item).queryByRole('button')).not.toBeInTheDocument()
    expect(within(item).getByText('application.json')).toBeInTheDocument()
    expect(within(panel).getByRole('link', { name: 'PurchaseRequest' })).toBeInTheDocument()
  })

  it('opens the resource of a clicked diagnostic at its path and scrolls to that field', async () => {
    const scrollIntoView = vi.fn()
    Element.prototype.scrollIntoView = scrollIntoView
    try {
      stubServer()
      renderPage()

      const panel = await openProblems()
      await userEvent.click(await within(panel).findByRole('link', { name: 'PurchaseRequest' }))

      // The editor scrolls the row of the field, then the designer scrolls the setting inside the field's drawer.
      const field = await screen.findByTestId('studio-field-/fields/0')
      const setting = await screen.findByTestId('studio-field-/fields/0/type')
      await waitFor(() => expect(scrollIntoView.mock.contexts).toEqual([field, setting]))
      expect(screen.getByTestId('location')).toHaveTextContent(`resource=${entityId}&path=%2Ffields%2F0%2Ftype`)
    } finally {
      delete (Element.prototype as Partial<Element>).scrollIntoView
    }
  })

  it('opens a diagnostic without a path at the top of its resource', async () => {
    const scrollIntoView = vi.fn()
    Element.prototype.scrollIntoView = scrollIntoView
    try {
      const fetchMock = stubServer()
      const base = fetchMock.getMockImplementation()!
      const noPath: StudioDiagnostic = {
        code: 'AXC0102',
        severity: 'error',
        message: 'The entity has no label.',
        file: 'entities/purchase-request.json',
        path: '',
        resourceId: entityId,
      }
      fetchMock.mockImplementation(async (input: string, init?: RequestInit) => {
        if (input === `${folder}/diagnostics`) {
          return json({ diagnostics: [noPath] })
        }
        return base(input, init)
      })
      renderPage()

      const panel = await openProblems()
      await userEvent.click(await within(panel).findByRole('link', { name: 'PurchaseRequest' }))

      await screen.findByTestId('studio-field-/fields/0')
      expect(screen.getByTestId('location')).toHaveTextContent(/resource=[^&]+$/)
      expect(screen.getByRole('tab', { name: 'Form' })).toHaveAttribute('aria-selected', 'true')
      expect(scrollIntoView).not.toHaveBeenCalled()
    } finally {
      delete (Element.prototype as Partial<Element>).scrollIntoView
    }
  })

  it('shows no path in the details of a resource opened at a path', async () => {
    stubServer()
    renderPage(`/studio?app=PurchaseRequests&resource=${entityId}&path=%2Ffields%2F0%2Ftype`)

    const details = await screen.findByTestId('studio-resource-details')
    expect(within(details).getByText('entities/purchase-request.json')).toBeInTheDocument()
    expect(within(details).queryByText('Path')).not.toBeInTheDocument()
    expect(details).not.toHaveTextContent('/fields/0/type')
  })

  it('lists the only application as a tile and stays on the list', async () => {
    const fetchMock = stubServer()
    renderPage('/studio')

    expect(await screen.findByTestId('studio-app-PurchaseRequests')).toBeInTheDocument()
    expect(screen.getByTestId('location')).toHaveTextContent(/^\/studio$/)
    expect(screen.queryByTestId('studio-group-entity')).not.toBeInTheDocument()
    expect(fetchMock).not.toHaveBeenCalledWith('/api/studio/apps/PurchaseRequests/resources', expect.anything())
  })

  it('switches between the kind list and the policy matrix from the sidebar, kept in the address', async () => {
    const fetchMock = stubServer()
    renderPage('/studio?app=PurchaseRequests&kind=entity')

    expect(await screen.findByTestId('studio-group-entity')).toBeInTheDocument()
    expect(fetchMock).not.toHaveBeenCalledWith(`${folder}/policy-matrix`, expect.anything())

    await userEvent.click(within(sidebar!).getByRole('link', { name: 'Policy matrix' }))

    expect(await screen.findByTestId('studio-matrix')).toBeInTheDocument()
    expect(screen.getByTestId('location')).toHaveTextContent('/studio?app=PurchaseRequests&view=policies')
    expect(fetchMock).toHaveBeenCalledWith(`${folder}/policy-matrix`, expect.anything())
    expect(screen.queryByTestId('studio-group-entity')).not.toBeInTheDocument()
    expect(await screen.findByTestId('studio-problems')).toBeInTheDocument()

    await userEvent.click(within(sidebar!).getByRole('link', { name: 'Overview' }))

    expect(await screen.findByRole('heading', { level: 1, name: 'PurchaseRequests' })).toBeInTheDocument()
    expect(screen.queryByTestId('studio-group-entity')).not.toBeInTheDocument()
    expect(screen.getByTestId('location')).toHaveTextContent(/^\/studio\?app=PurchaseRequests$/)
  })

  it('reopens the policy matrix from the address', async () => {
    const fetchMock = stubServer()
    renderPage('/studio?app=PurchaseRequests&view=policies')

    expect(await screen.findByTestId('studio-matrix')).toBeInTheDocument()
    expect(fetchMock).toHaveBeenCalledWith(`${folder}/policy-matrix`, expect.anything())
  })

  it('loads the names of the picked application', async () => {
    const fetchMock = stubServer()
    renderPage(`/studio?app=PurchaseRequests&resource=${entityId}`)

    expect(await screen.findByTestId('studio-editor')).toBeInTheDocument()
    expect(fetchMock).toHaveBeenCalledWith('/api/studio/apps/PurchaseRequests/names', expect.anything())
  })

  it('keeps the folder usable when the names cannot be loaded', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    const fetchMock = stubServer()
    const base = fetchMock.getMockImplementation()!
    fetchMock.mockImplementation(async (input: string, init?: RequestInit) =>
      input === `${folder}/names` ? json({ title: 'No.' }, 500) : base(input, init),
    )
    renderPage(`/studio?app=PurchaseRequests&resource=${entityId}`)

    expect(await screen.findByTestId('studio-editor')).toBeInTheDocument()
    expect(screen.getByTestId('studio-resource-details')).toHaveTextContent('entities/purchase-request.json')
    expect(warn).toHaveBeenCalled()
    warn.mockRestore()
  })

  describe('the page area', () => {
    /** Checks the table comes straight after its heading, and no element between them draws a border or a background. */
    async function expectTableStraightAfterHeading(name: string, testId: string) {
      const heading = await screen.findByRole('heading', { level: 1, name })
      const table = within(await screen.findByTestId(testId)).getByRole('table')
      let shared = table.parentElement!
      while (!shared.contains(heading)) shared = shared.parentElement!
      const children = [...shared.children]
      const at = children.findIndex((child) => child.contains(heading))
      expect(children[at + 1].contains(table)).toBe(true)
      for (let element = table.parentElement; element !== shared; element = element!.parentElement) {
        expect(element!.style.border).toBe('')
        expect(element!.style.background).toBe('')
      }
    }

    it.each([
      ['the kind list', '&kind=entity', 'Entities', 'studio-group-entity'],
      ['Releases', '&view=releases', 'Releases', 'studio-releases'],
      ['History', '&view=history', 'History', 'studio-history'],
      ['the policy matrix', '&view=policies', 'Policy matrix', 'studio-matrix'],
    ])('shows %s as a table straight after its heading, with no panel', async (_name, view, heading, testId) => {
      stubServer()
      renderPage(`/studio?app=PurchaseRequests${view}`)

      await expectTableStraightAfterHeading(heading, testId)
    })
  })

  describe('application tiles', () => {
    it('shows one tile per application with its title and description in the UI locale', async () => {
      stubServer(apps)
      renderPage('/studio', 'vi')

      const purchases = await screen.findByTestId('studio-app-PurchaseRequests')
      expect(within(purchases).getByText('Yêu cầu mua hàng')).toBeInTheDocument()
      expect(within(purchases).getByText('Quản lý việc mua hàng.')).toBeInTheDocument()
      expect(purchases).toHaveAttribute('href', '/studio?app=PurchaseRequests')
      expect(screen.getByTestId('studio-apps').children).toHaveLength(3)
      expect(screen.queryByTestId('studio-application-row')).not.toBeInTheDocument()
      expect(screen.queryByTestId('studio-diagnostics')).not.toBeInTheDocument()
    })

    it('falls back to en, then to the name, and shows no description when there is none', async () => {
      stubServer(apps)
      renderPage('/studio', 'vi')

      const leave = await screen.findByTestId('studio-app-LeaveRequests')
      expect(within(leave).getByText('Leave requests')).toBeInTheDocument()
      expect(leave).toHaveAttribute('href', '/studio?app=LeaveRequests')
      const inventory = screen.getByTestId('studio-app-Inventory')
      expect(inventory).toHaveTextContent(/^IInventory$/)
      expect(inventory).toHaveAttribute('href', '/studio?app=Inventory')
    })

    it('opens an application when its tile is clicked', async () => {
      stubServer(apps)
      renderPage('/studio')

      await userEvent.click(await screen.findByTestId('studio-app-LeaveRequests'))

      expect(screen.getByTestId('location')).toHaveTextContent('/studio?app=LeaveRequests')
      expect(await screen.findByTestId('studio-application-row')).toBeInTheDocument()
    })
  })

  it('shows the forbidden page when the server answers 403', async () => {
    stubServer(403)
    renderPage('/studio')

    const forbidden = await screen.findByTestId('studio-forbidden')
    expect(forbidden).toHaveTextContent('Studio is not available')
    expect(forbidden).toHaveTextContent('You need the author role to use Studio.')
    expect(screen.queryByRole('button', { name: 'Download' })).toBeNull()
  })

  it('says so when there is no application', async () => {
    stubServer([])
    renderPage('/studio')

    expect(await screen.findByTestId('studio-no-applications')).toBeInTheDocument()
  })

  it('shows a load error when the applications cannot be loaded', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubServer(500)
    renderPage('/studio')

    expect(await screen.findByTestId('studio-load-error')).toBeInTheDocument()
    warn.mockRestore()
  })

  it('does not claim the sidebar on the list of applications', async () => {
    stubServer()
    const claim = renderPage('/studio')

    expect(await screen.findByTestId('studio-app-PurchaseRequests')).toBeInTheDocument()
    expect(claim).not.toHaveBeenCalled()
    expect(sidebar).toBeEmptyDOMElement()
  })

  it('does not claim the sidebar on the forbidden page', async () => {
    stubServer(403)
    const claim = renderPage('/studio')

    expect(await screen.findByTestId('studio-forbidden')).toBeInTheDocument()
    expect(claim).not.toHaveBeenCalled()
    expect(sidebar).toBeEmptyDOMElement()
  })

  it('does not claim the sidebar on a load error', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubServer(500)
    const claim = renderPage('/studio')

    expect(await screen.findByTestId('studio-load-error')).toBeInTheDocument()
    expect(claim).not.toHaveBeenCalled()
    expect(sidebar).toBeEmptyDOMElement()
    warn.mockRestore()
  })

  it('draws the application row and the entries of the open application in the sidebar', async () => {
    stubServer()
    const claim = renderPage()

    expect(await within(sidebar!).findByTestId('studio-application-row')).toHaveTextContent('PurchaseRequests')
    expect(claim).toHaveBeenCalled()
    expect(within(sidebar!).getByRole('link', { name: 'Overview' })).toHaveAttribute('aria-current', 'page')
    expect(within(sidebar!).getByRole('link', { name: 'Entities' })).toHaveAttribute(
      'href',
      '/studio?app=PurchaseRequests&kind=entity',
    )
  })

  it('lists every application in the row menu and marks the open one', async () => {
    stubServer(apps)
    renderPage()

    await userEvent.click(await screen.findByTestId('studio-application-row'))

    for (const title of ['Purchase requests', 'Leave requests', 'Inventory']) {
      expect(await screen.findByRole('menuitem', { name: title })).toBeInTheDocument()
    }
    expect(screen.getByRole('menuitem', { name: 'Purchase requests' })).toHaveClass('ant-dropdown-menu-item-selected')
    expect(screen.getByRole('menuitem', { name: 'Leave requests' })).not.toHaveClass('ant-dropdown-menu-item-selected')
  })

  it('lists only the applications and New application… in the row menu, with no All applications', async () => {
    const fetchMock = stubServer(apps)
    const base = fetchMock.getMockImplementation()!
    fetchMock.mockImplementation(async (input: string, init?: RequestInit) =>
      input === '/api/me' ? json({ id: 'author', studio: true, studioCreate: true }) : base(input, init),
    )
    renderPage()

    await userEvent.click(await screen.findByTestId('studio-application-row'))
    await screen.findByRole('menuitem', { name: 'New application…' })

    const items = screen.getAllByRole('menuitem').filter((item) => item.classList.contains('ant-dropdown-menu-item'))
    expect(items.map((item) => item.textContent)).toEqual([
      'Purchase requests',
      'Leave requests',
      'Inventory',
      'New application…',
    ])
    expect(screen.queryByRole('menuitem', { name: /All applications/ })).not.toBeInTheDocument()
  })

  describe('band, Problems panel and page heading', () => {
    it('shows no Ask AI unless /api/me answers studioAi', async () => {
      stubServer()
      renderPage()

      await screen.findByTestId('studio-problems')
      expect(screen.queryByTestId('studio-ask-ai')).not.toBeInTheDocument()
    })

    it('opens the Ask AI panel from the band, one side panel at a time with Problems', async () => {
      const fetchMock = stubServer()
      const base = fetchMock.getMockImplementation()!
      fetchMock.mockImplementation(async (input: string, init?: RequestInit) =>
        input === '/api/me' ? json({ id: 'author', studio: true, studioAi: true }) : base(input, init),
      )
      renderPage()

      const askAi = await screen.findByTestId('studio-ask-ai')
      expect(askAi).toHaveTextContent('Ask AI')
      expect(askAi).toHaveAttribute('aria-expanded', 'false')

      await openProblems()
      await userEvent.click(askAi)

      expect(askAi).toHaveAttribute('aria-expanded', 'true')
      expect(askAi).toHaveAttribute('aria-controls', screen.getByTestId('studio-ask-ai-panel').id)
      expect(within(screen.getByTestId('aside')).getByTestId('studio-ask-ai-panel')).toBeInTheDocument()
      expect(screen.queryByTestId('studio-diagnostics')).not.toBeInTheDocument()

      await userEvent.click(screen.getByTestId('studio-problems'))

      expect(screen.getByTestId('studio-diagnostics')).toBeInTheDocument()
      expect(screen.queryByTestId('studio-ask-ai-panel')).not.toBeInTheDocument()
      expect(askAi).toHaveAttribute('aria-expanded', 'false')

      await userEvent.click(askAi)
      await userEvent.click(askAi)

      expect(screen.queryByTestId('studio-ask-ai-panel')).not.toBeInTheDocument()
    })

    it('opens the Problems panel from the band and keeps it open while problems are opened', async () => {
      stubServer()
      renderPage()

      const button = await screen.findByTestId('studio-problems')
      expect(button).toHaveTextContent('1 error, 1 warning')
      expect(button).toHaveAttribute('aria-expanded', 'false')
      expect(screen.queryByTestId('studio-diagnostics')).not.toBeInTheDocument()

      const panel = await openProblems()
      expect(button).toHaveAttribute('aria-expanded', 'true')
      expect(within(screen.getByTestId('aside')).getByTestId('studio-diagnostics')).toBe(panel)
      const items = within(panel).getAllByRole('listitem')
      expect(items[0]).toHaveTextContent('AXC0101')
      expect(items[1]).toHaveTextContent('AXC0001')

      await userEvent.click(within(panel).getByRole('link', { name: 'PurchaseRequest' }))

      expect(screen.getByTestId('location')).toHaveTextContent(`resource=${entityId}`)
      expect(await screen.findByTestId('studio-editor')).toBeInTheDocument()
      expect(screen.getByTestId('studio-diagnostics')).toBeInTheDocument()

      await userEvent.click(
        within(screen.getByTestId('studio-diagnostics')).getByRole('button', { name: 'Close problems' }),
      )

      expect(screen.queryByTestId('studio-diagnostics')).not.toBeInTheDocument()
      expect(button).toHaveAttribute('aria-expanded', 'false')
    })

    it('has the band and no level-2 Studio title on an open application', async () => {
      stubServer()
      renderPage()

      expect(await screen.findByTestId('studio-band')).toBeInTheDocument()
      expect(screen.queryByRole('heading', { level: 2, name: /PurchaseRequests/ })).not.toBeInTheDocument()
    })

    it.each([
      ['the list of applications', '/studio'],
      ['the Overview', '/studio?app=PurchaseRequests'],
      ['a kind’s list', '/studio?app=PurchaseRequests&kind=entity'],
      ['a resource', `/studio?app=PurchaseRequests&resource=${entityId}`],
      ['Releases', '/studio?app=PurchaseRequests&view=releases'],
      ['the policy matrix', '/studio?app=PurchaseRequests&view=policies'],
    ])('has exactly one level-one heading on %s', async (_, entry) => {
      stubServer()
      renderPage(entry)

      await screen.findByRole('heading', { level: 1 })
      if (entry.includes('view=policies')) {
        await screen.findByTestId('studio-matrix')
      } else if (entry.includes('resource=')) {
        await screen.findByTestId('studio-editor')
      }

      expect(screen.getAllByRole('heading', { level: 1 })).toHaveLength(1)
    })
  })

  it('opens another application when it is picked in the row menu', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubServer(apps)
    renderPage()

    await userEvent.click(await screen.findByTestId('studio-application-row'))
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Leave requests' }))

    expect(screen.getByTestId('location')).toHaveTextContent(/^\/studio\?app=LeaveRequests$/)
    warn.mockRestore()
  })

  it('shows only the publishing block for &view=releases', async () => {
    stubServer()
    renderPage('/studio?app=PurchaseRequests&view=releases')

    expect(await screen.findByRole('button', { name: 'Validate' })).toBeInTheDocument()
    expect(screen.queryByTestId('studio-group-entity')).not.toBeInTheDocument()
    expect(screen.queryByTestId('studio-diagnostics')).not.toBeInTheDocument()
  })

  it('shows an authoring request page in place of the content for &view=requests&request=<id>', async () => {
    const fetchMock = stubServer()
    const base = fetchMock.getMockImplementation()!
    const requestPath = '/api/studio/apps/PurchaseRequests/authoring-requests/r1'
    fetchMock.mockImplementation(async (input: string, init?: RequestInit) => {
      if (input === '/api/me') {
        return json({ id: 'author', studio: true, studioCreate: true })
      }
      if (input === requestPath) {
        return json({
          id: 'r1',
          application: 'PurchaseRequests',
          author: 'author',
          authorName: null,
          text: 'Add a supplier field.',
          title: null,
          state: 'awaitingApproval',
          plan: null,
          round: 0,
          tokens: 0,
          autoAccept: false,
          sentAt: '2026-11-18T09:30:00Z',
          approvedAt: null,
          finishedAt: null,
        })
      }
      return base(input, init)
    })
    renderPage('/studio?app=PurchaseRequests&view=requests&request=r1')

    expect(await screen.findByTestId('studio-ai-request-state')).toHaveTextContent('Awaiting approval')
    expect(screen.getByTestId('studio-ai-request')).toHaveTextContent('Add a supplier field.')
  })

  it('opens the request panel from the Ask AI of an unknown request, when studioAi is on', async () => {
    const fetchMock = stubServer()
    const base = fetchMock.getMockImplementation()!
    const requestPath = '/api/studio/apps/PurchaseRequests/authoring-requests/r9'
    fetchMock.mockImplementation(async (input: string, init?: RequestInit) => {
      if (input === '/api/me') {
        return json({ id: 'author', studio: true, studioAi: true })
      }
      if (input === requestPath) {
        return new Response(null, { status: 404 })
      }
      return base(input, init)
    })
    renderPage('/studio?app=PurchaseRequests&view=requests&request=r9')

    const notFound = await screen.findByTestId('studio-ai-request-not-found')
    await userEvent.click(await within(notFound).findByRole('button', { name: 'Ask AI' }))

    expect(within(screen.getByTestId('aside')).getByTestId('studio-ask-ai-panel')).toBeInTheDocument()
  })

  it('shows no Ask AI on an unknown request when /api/me does not answer studioAi', async () => {
    const fetchMock = stubServer()
    const base = fetchMock.getMockImplementation()!
    const requestPath = '/api/studio/apps/PurchaseRequests/authoring-requests/r9'
    fetchMock.mockImplementation(async (input: string, init?: RequestInit) => {
      if (input === '/api/me') {
        return json({ id: 'author', studio: true })
      }
      if (input === requestPath) {
        return new Response(null, { status: 404 })
      }
      return base(input, init)
    })
    renderPage('/studio?app=PurchaseRequests&view=requests&request=r9')

    const notFound = await screen.findByTestId('studio-ai-request-not-found')
    expect(within(notFound).queryByRole('button', { name: 'Ask AI' })).toBeNull()
  })

  it('shows only the kind of the address in its list, and drops the kind when a resource is picked', async () => {
    stubServer()
    renderPage('/studio?app=PurchaseRequests&kind=entity')

    expect(await screen.findByTestId('studio-group-entity')).toBeInTheDocument()
    expect(screen.queryByTestId('studio-group-form')).not.toBeInTheDocument()

    await userEvent.click(await screen.findByTestId(`studio-resource-${entityId}`))

    expect(await screen.findByTestId('studio-editor')).toBeInTheDocument()
    expect(screen.getByTestId('location')).toHaveTextContent(`app=PurchaseRequests&resource=${entityId}`)
    expect(screen.getByTestId('location')).not.toHaveTextContent('kind=')
    expect(screen.queryByTestId('studio-group-entity')).not.toBeInTheDocument()
  })

  it('shows the loading state, not the rows of the previous kind, while another kind loads', async () => {
    const fetchMock = stubServer()
    let answer: (response: Response) => void = () => {}
    const held = new Promise<Response>((resolve) => {
      answer = resolve
    })
    vi.stubGlobal('fetch', (input: string, init?: RequestInit) =>
      input.startsWith(`${folder}/kinds/form/`) ? held : fetchMock(input, init),
    )
    renderPage('/studio?app=PurchaseRequests&kind=entity')
    expect(await screen.findByTestId(`studio-resource-${entityId}`)).toBeInTheDocument()

    await userEvent.click(within(sidebar!).getByRole('link', { name: 'Forms' }))

    expect(await screen.findByRole('heading', { level: 1, name: 'Forms' })).toBeInTheDocument()
    expect(screen.queryByTestId(`studio-resource-${entityId}`)).not.toBeInTheDocument()
    expect(screen.queryByTestId(`studio-resource-${formId}`)).not.toBeInTheDocument()
    expect(within(screen.getByTestId('studio-group-form')).getByRole('table')).toBeInTheDocument()

    answer(json(kindList(`${folder}/kinds/form/resources`)))

    expect(await screen.findByTestId(`studio-resource-${formId}`)).toBeInTheDocument()
  })

  it('shows a load error when the folder cannot be loaded', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubServer()
    renderPage('/studio?app=Unknown')

    expect(await screen.findByTestId('studio-load-error')).toBeInTheDocument()
    warn.mockRestore()
  })

  it('gives the application manifest no Used by tab', async () => {
    stubServer()
    renderPage(`/studio?app=PurchaseRequests&resource=${manifestId}`)

    await screen.findByTestId('studio-editor')
    expect(screen.queryByRole('tab', { name: 'Used by' })).not.toBeInTheDocument()
  })

  it('loads what uses an entity only when its Used by tab is opened', async () => {
    const fetchMock = stubServer()
    renderPage(`/studio?app=PurchaseRequests&resource=${entityId}`)
    await screen.findByRole('tab', { name: 'Used by' })

    expect(fetchMock.mock.calls.some(([input]) => String(input).endsWith('/references'))).toBe(false)

    await userEvent.click(screen.getByRole('tab', { name: 'Used by' }))

    expect(await screen.findByTestId('studio-used-by')).toBeInTheDocument()
    expect(fetchMock).toHaveBeenCalledWith(`${folder}/resources/${entityId}/references`, expect.anything())
  })

  it('opens a resource that uses the open one at the path of its reference', async () => {
    const fetchMock = stubServer()
    const base = fetchMock.getMockImplementation()!
    fetchMock.mockImplementation(async (input: string, init?: RequestInit) =>
      input === `${folder}/resources/${departmentId}`
        ? json({
            ...resources[0],
            content: { id: departmentId, kind: 'entity', name: 'Department', formatVersion: 1, fields: [] },
          })
        : base(input, init),
    )
    renderPage(`/studio?app=PurchaseRequests&resource=${departmentId}`)

    await userEvent.click(await screen.findByRole('tab', { name: 'Used by' }))
    const usedBy = await screen.findByTestId('studio-used-by')
    await userEvent.click(await within(usedBy).findByRole('link', { name: 'PurchaseRequest' }))

    expect(screen.getByTestId('location')).toHaveTextContent(`resource=${entityId}&path=%2Ffields%2F3%2Ftarget`)
    expect(within(await screen.findByTestId('studio-resource-details')).queryByText('Path')).not.toBeInTheDocument()
  })

  it.each([
    ["its label in the author's language", { en: 'Employee', vi: 'Nhân viên' }, 'Nhân viên'],
    ['its name without the module, when it has no label', {}, 'Employee'],
  ])('shows a module entity by %s in its heading, never with the module', async (_, titles, heading) => {
    const fetchMock = stubServer()
    const base = fetchMock.getMockImplementation()!
    fetchMock.mockImplementation(async (input: string, init?: RequestInit) => {
      if (input === namesPath) {
        const employee = { id: employeeId, name: 'organization/Employee', module: 'organization', fields: [] }
        return json({ ...applicationNames, entities: [...applicationNames.entities, employee] })
      }
      if (input === `${folder}/resource-activity`) {
        const item = { id: employeeId, module: 'organization', status: null, titles, descriptions: {} }
        return json({ items: [{ ...item, modifiedBy: null, modifiedAt: null }] })
      }
      return base(input, init)
    })
    renderPage(`/studio?app=PurchaseRequests&resource=${employeeId}`, 'vi')

    expect(await screen.findByRole('heading', { level: 1, name: heading })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { level: 1, name: 'organization/Employee' })).toBeNull()
    // The module is a fact of the page's first part, not part of the heading.
    expect(screen.getByTestId('page-heading')).not.toHaveTextContent('organization')
    expect(
      within(screen.getByTestId('studio-section-facts')).getByText('organization', { selector: 'dd' }),
    ).toBeVisible()
  })

  it('scrolls the form field of the path in the address into view', async () => {
    const scrollIntoView = vi.fn()
    Element.prototype.scrollIntoView = scrollIntoView
    try {
      stubServer()
      renderPage(`/studio?app=PurchaseRequests&resource=${entityId}&path=%2Ffields%2F1%2FmaxLength`)

      // The editor scrolls the row of the field, then the designer scrolls the setting inside the field's drawer.
      const field = await screen.findByTestId('studio-field-/fields/1')
      const setting = await screen.findByTestId('studio-field-/fields/1/maxLength')
      await waitFor(() => expect(scrollIntoView.mock.contexts).toEqual([field, setting]))
    } finally {
      delete (Element.prototype as Partial<Element>).scrollIntoView
    }
  })

  it('opens a path inside a map in the JSON view', async () => {
    const fetchMock = stubServer()
    const base = fetchMock.getMockImplementation()!
    const textId = '3f9c2a10-7b5e-4d8c-9e1f-0a1b2c3d4e5f'
    const textItem: StudioResourceItem = {
      id: textId,
      kind: 'text',
      name: null,
      file: 'texts/en.json',
      version: 'e',
      usedBy: 0,
    }
    const textContent = { id: textId, kind: 'text', name: null, formatVersion: 1, locale: 'en', texts: { 'a.b': 'x' } }
    fetchMock.mockImplementation(async (input: string, init?: RequestInit) => {
      if (input === `${folder}/resources`) {
        return json({ items: [...resources, textItem] })
      }
      if (input === `${folder}/resources/${textId}`) {
        return json({ ...textItem, content: textContent })
      }
      if (input === `${folder}/resources/${textId}/references`) {
        return json({ uses: [], usedBy: [] })
      }
      return base(input, init)
    })
    renderPage(`/studio?app=PurchaseRequests&resource=${textId}&path=%2Ftexts%2Fa.b`)

    await waitFor(
      () => expect(screen.getByRole('tab', { name: 'JSON' })).toHaveAttribute('aria-selected', 'true'),
      slow,
    )
    expect(await jsonText()).toBe(JSON.stringify(textContent, null, 2))
  })

  it('keeps the last expression result and Save enabled when a check gets no answer', async () => {
    const fetchMock = stubServer(undefined, undefined, undefined, {
      content: {
        ...entityContent(),
        validations: [
          {
            name: 'totalPositive',
            expression: 'totalAmount > 0',
            message: { textKey: 'validation.total' },
            field: 'title',
          },
        ],
      },
      check: (call) => {
        if (call === 1) {
          return json({ type: 'boolean', diagnostics: [] })
        }
        throw new TypeError('Failed to fetch')
      },
    })
    renderPage(`/studio?app=PurchaseRequests&resource=${entityId}`)

    const field = await screen.findByTestId('studio-field-/validations/0/expression')
    await waitFor(() =>
      expect(within(field).getByTestId('studio-expression-type')).toHaveTextContent('Gives yes or no'),
    )

    const view = EditorView.findFromDOM(within(field).getByRole('textbox'))!
    view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: 'totalAmount > 1' } })
    await waitFor(() =>
      expect(fetchMock.mock.calls.filter(([input]) => input === `${folder}/expressions/check`)).toHaveLength(2),
    )

    expect(within(field).getByTestId('studio-expression-type')).toHaveTextContent('Gives yes or no')
    expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled()
    expect(screen.queryByTestId('studio-reconnecting')).not.toBeInTheDocument()
  })

  it('shows the form again for a path it can show after a path it cannot show', async () => {
    const scrollIntoView = vi.fn()
    Element.prototype.scrollIntoView = scrollIntoView
    try {
      stubServer()
      renderPage(`/studio?app=PurchaseRequests&resource=${entityId}&path=%2Funknown`)
      await waitFor(
        () => expect(screen.getByRole('tab', { name: 'JSON' })).toHaveAttribute('aria-selected', 'true'),
        slow,
      )

      const panel = await openProblems()
      await userEvent.click(await within(panel).findByRole('link', { name: 'PurchaseRequest' }, slow))

      // The editor scrolls the row of the field, then the designer scrolls the setting inside the field's drawer.
      const field = await screen.findByTestId('studio-field-/fields/0', {}, slow)
      const setting = await screen.findByTestId('studio-field-/fields/0/type', {}, slow)
      await waitFor(() => expect(scrollIntoView.mock.contexts).toEqual([field, setting]), slow)
      await waitFor(
        () => expect(screen.getByRole('tab', { name: 'Form' })).toHaveAttribute('aria-selected', 'true'),
        slow,
      )
    } finally {
      delete (Element.prototype as Partial<Element>).scrollIntoView
    }
  })

  async function openDeleteDialog() {
    await userEvent.click(await screen.findByRole('button', { name: 'Delete…' }))
  }

  it('draws Delete… on the heading in the destructive tone, with no "…" menu beside it', async () => {
    stubServer()
    renderPage(`/studio?app=PurchaseRequests&resource=${entityId}`)

    const heading = await screen.findByTestId('studio-resource-details')
    expect(within(heading).queryByRole('button', { name: 'More actions' })).not.toBeInTheDocument()
    expect(within(heading).getByRole('button', { name: 'Delete…' })).toHaveStyle({
      color: designTokensFor('light').colour.dangerText,
      borderColor: designTokensFor('light').colour.dangerText,
    })
  })

  it('turns Delete… off while Studio reconnects, and on again once it is back', async () => {
    const outage: Outage = { down: null }
    stubServer(undefined, undefined, outage)
    renderPage(`/studio?app=PurchaseRequests&resource=${entityId}`)
    await openEntityField(1)
    const maxLength = within(screen.getByTestId('studio-field-/fields/1/maxLength')).getByRole('spinbutton')
    await userEvent.clear(maxLength)
    await userEvent.type(maxLength, '250')

    outage.down = { status: null }
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await screen.findByTestId('studio-reconnecting', {}, { timeout: 3000 })
    expect(screen.getByRole('button', { name: 'Delete…' })).toBeDisabled()

    outage.down = null
    await waitFor(() => expect(screen.queryByTestId('studio-reconnecting')).not.toBeInTheDocument(), { timeout: 3000 })
    expect(screen.getByRole('button', { name: 'Delete…' })).toBeEnabled()
  })

  it('closes the delete dialog and opens the Used by tab from See what uses it', async () => {
    stubServer()
    renderPage(`/studio?app=PurchaseRequests&resource=${entityId}`)
    await openDeleteDialog()

    const dialog = await screen.findByRole('dialog')
    await userEvent.click(within(dialog).getByRole('button', { name: 'See what uses it' }))

    await waitFor(
      () => expect(screen.getByRole('tab', { name: 'Used by' })).toHaveAttribute('aria-selected', 'true'),
      slow,
    )
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument(), slow)
    expect(screen.getByRole('tab', { name: 'Used by' })).toHaveAttribute('aria-selected', 'true')
  })

  it('finishes the delete dialog in a solid red Delete, with Cancel beside it', async () => {
    stubServer()
    renderPage(`/studio?app=PurchaseRequests&resource=${entityId}`)
    await openDeleteDialog()

    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByRole('button', { name: 'Delete' })).toHaveStyle({
      background: designTokensFor('light').colour.danger,
    })
    expect(within(dialog).getByRole('button', { name: 'Cancel' })).toBeVisible()
  })

  it('warns with the number of references before a delete, and deletes after confirming', async () => {
    const fetchMock = stubServer()
    renderPage(`/studio?app=PurchaseRequests&resource=${entityId}`)
    await screen.findByTestId('studio-editor')
    expect(screen.queryByRole('button', { name: 'Delete' })).not.toBeInTheDocument()
    await openDeleteDialog()

    const dialog = await screen.findByRole('dialog')
    expect(within(within(dialog).getByTestId('studio-delete-warning')).getByText('3')).toBeInTheDocument()
    await userEvent.click(within(dialog).getByRole('button', { name: 'Delete' }))

    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent(/^\/studio\?app=PurchaseRequests$/))
    const deletes = fetchMock.mock.calls.filter(([, init]) => init?.method === 'DELETE')
    expect(deletes).toHaveLength(1)
    const [, init] = deletes[0]
    expect(new Headers(init!.headers).get('If-Match')).toBe('"b"')
  })

  it('loads the names again after a save and after a delete', async () => {
    const fetchMock = stubServer()
    renderPage(`/studio?app=PurchaseRequests&resource=${entityId}`)
    await openEntityField(1)
    const maxLength = within(screen.getByTestId('studio-field-/fields/1/maxLength')).getByRole('spinbutton')
    const loads = () => fetchMock.mock.calls.filter(([input]) => input === namesPath).length
    await waitFor(() => expect(loads()).toBe(1))

    await userEvent.clear(maxLength)
    await userEvent.type(maxLength, '250')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await screen.findByTestId('studio-saved')
    await waitFor(() => expect(loads()).toBe(2))

    await openDeleteDialog()
    // The field drawer is open too, so the dialog is the one that holds Delete.
    await userEvent.click(await screen.findByRole('button', { name: 'Delete' }))
    await waitFor(() => expect(loads()).toBe(3))
  })

  it('loads the names again once the server answers after an outage', async () => {
    const outage: Outage = { down: { status: 503 } }
    const fetchMock = stubServer(undefined, undefined, outage)
    renderPage(`/studio?app=PurchaseRequests&resource=${entityId}`)
    await screen.findByTestId('studio-reconnecting', {}, { timeout: 3000 })
    const loads = () => fetchMock.mock.calls.filter(([input]) => input === namesPath).length
    expect(loads()).toBe(1)

    outage.down = null

    await waitFor(() => expect(screen.queryByTestId('studio-reconnecting')).not.toBeInTheDocument(), { timeout: 3000 })
    await waitFor(() => expect(loads()).toBe(2))
  })

  it('does not offer a delete for the application manifest', async () => {
    stubServer()
    renderPage(`/studio?app=PurchaseRequests&resource=${manifestId}`)

    await screen.findByTestId('studio-editor')
    expect(screen.queryByRole('button', { name: 'More actions' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Delete…' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Delete' })).not.toBeInTheDocument()
  })

  it.each([
    ['does not answer', { status: null }],
    ['answers 503', { status: 503 }],
  ])('shows the reconnect message, and no load error, when the server %s', async (_, down) => {
    stubServer(undefined, undefined, { down })
    renderPage(`/studio?app=PurchaseRequests&resource=${entityId}`)

    expect(await screen.findByTestId('studio-reconnecting', {}, { timeout: 3000 })).toHaveTextContent(
      'The server is restarting. Reconnecting…',
    )
    expect(screen.queryByTestId('studio-resource-load-error')).not.toBeInTheDocument()
    expect(screen.queryByTestId('studio-load-error')).not.toBeInTheDocument()
  })

  it('still shows the load error for a 500', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubServer(undefined, undefined, { down: { status: 500 } })
    renderPage(`/studio?app=PurchaseRequests&resource=${entityId}`)

    expect(await screen.findByTestId('studio-resource-load-error', {}, { timeout: 3000 })).toBeInTheDocument()
    expect(screen.queryByTestId('studio-reconnecting')).not.toBeInTheDocument()
    warn.mockRestore()
  })

  it('loads the resource and the diagnostics again once the server answers', async () => {
    const outage: Outage = { down: { status: 503 } }
    const fetchMock = stubServer(undefined, undefined, outage)
    renderPage(`/studio?app=PurchaseRequests&resource=${entityId}`)
    await screen.findByTestId('studio-reconnecting', {}, { timeout: 3000 })

    outage.down = null

    expect(await openEntityField(0)).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByTestId('studio-reconnecting')).not.toBeInTheDocument())
    const reads = (path: string) => fetchMock.mock.calls.filter(([input, init]) => input === path && !init?.method)
    expect(reads(`/api/studio/apps/PurchaseRequests/resources/${entityId}`)).toHaveLength(2)
    expect(reads('/api/studio/apps/PurchaseRequests/diagnostics')).toHaveLength(2)
  })

  it('keeps an unsaved draft through a reconnect, and saves it with the same patch', async () => {
    const outage: Outage = { down: null }
    const fetchMock = stubServer(undefined, undefined, outage)
    renderPage(`/studio?app=PurchaseRequests&resource=${entityId}`)
    await openEntityField(1)
    const maxLength = within(screen.getByTestId('studio-field-/fields/1/maxLength')).getByRole('spinbutton')
    await userEvent.clear(maxLength)
    await userEvent.type(maxLength, '250')

    outage.down = { status: null }
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await screen.findByTestId('studio-reconnecting', {}, { timeout: 3000 })
    expect(maxLength).toHaveValue('250')

    outage.down = null
    await waitFor(() => expect(screen.queryByTestId('studio-reconnecting')).not.toBeInTheDocument(), { timeout: 3000 })
    expect(maxLength).toHaveValue('250')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    const patches = fetchMock.mock.calls.filter(([, init]) => init?.method === 'PATCH')
    expect(patches).toHaveLength(2)
    const [, init] = patches[1]
    expect(JSON.parse(init!.body as string)).toEqual([{ op: 'replace', path: '/fields/1/maxLength', value: 250 }])
    expect(new Headers(init!.headers).get('If-Match')).toBe('"b"')
  })

  it('asks to sign in when the reload after a reconnect answers 401', async () => {
    vi.mocked(assignLocation).mockClear()
    const outage: Outage = { down: null }
    stubServer(undefined, undefined, outage)
    renderPage(`/studio?app=PurchaseRequests&resource=${entityId}`)
    await openEntityField(1)
    const maxLength = within(screen.getByTestId('studio-field-/fields/1/maxLength')).getByRole('spinbutton')
    await userEvent.clear(maxLength)
    await userEvent.type(maxLength, '250')

    outage.down = { status: null }
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await screen.findByTestId('studio-reconnecting', {}, { timeout: 3000 })
    outage.down = { status: 401 }

    await waitFor(() => expect(isSignInNeeded()).toBe(true), { timeout: 3000 })
    expect(assignLocation).not.toHaveBeenCalled()
  })
})

describe('StudioPage leaving unsaved changes', () => {
  const overview = '/studio?app=PurchaseRequests'
  const entityAddress = `${overview}&resource=${entityId}`
  // Field 1 is `title`. Its drawer is open while the draft is edited, so the address holds its `field`.
  const titleAddress = `${entityAddress}&field=title`

  async function editMaxLength() {
    await openEntityField(1)
    const maxLength = within(screen.getByTestId('studio-field-/fields/1/maxLength')).getByRole('spinbutton')
    await userEvent.clear(maxLength)
    await userEvent.type(maxLength, '250')
    return maxLength
  }

  async function stay() {
    expect(await screen.findByText('Leave without saving?')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Stay' }))
    await waitFor(() => expect(screen.getByText('Leave without saving?')).not.toBeVisible())
  }

  it('asks before a shell link, and Stay keeps the address and the draft', async () => {
    stubServer()
    const router = renderGuardedPage([entityAddress])
    const maxLength = await editMaxLength()

    await userEvent.click(screen.getByRole('link', { name: 'Home' }))
    await stay()

    expect(router.state.location.pathname + router.state.location.search).toBe(titleAddress)
    expect(maxLength).toHaveValue('250')
    expect(screen.getByTestId('studio-save-bar')).toHaveTextContent('Unsaved changes')
  })

  it('moves on when the author picks Leave', async () => {
    stubServer()
    const router = renderGuardedPage([entityAddress])
    await editMaxLength()

    await userEvent.click(screen.getByRole('link', { name: 'Home' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Leave' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/'))
  })

  it('asks before opening a problem, and Stay keeps the address and the draft', async () => {
    stubServer()
    const router = renderGuardedPage([entityAddress])
    const maxLength = await editMaxLength()

    const panel = await openProblems()
    await userEvent.click(await within(panel).findByRole('link', { name: 'PurchaseRequest' }))
    await stay()

    expect(router.state.location.pathname + router.state.location.search).toBe(titleAddress)
    expect(maxLength).toHaveValue('250')
  })

  it('asks before Back, and Stay keeps the address and the draft', async () => {
    stubServer()
    const router = renderGuardedPage([overview, entityAddress])
    await editMaxLength()

    // The first Back only closes the field's drawer, which the editor's own view does not ask about.
    act(() => {
      void router.navigate(-1)
    })
    await waitFor(() => expect(router.state.location.pathname + router.state.location.search).toBe(entityAddress))
    expect(screen.queryByText('Leave without saving?')).not.toBeInTheDocument()

    // The second Back leaves the resource, so it is asked.
    act(() => {
      void router.navigate(-1)
    })
    await stay()

    expect(router.state.location.pathname + router.state.location.search).toBe(entityAddress)
    expect(screen.getByTestId('studio-save-bar')).toHaveTextContent('Unsaved changes')
  })

  it('does not ask before a link while nothing is unsaved', async () => {
    stubServer()
    const router = renderGuardedPage([entityAddress])
    await openEntityField(1)

    await userEvent.click(screen.getByRole('link', { name: 'Home' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/'))
    expect(screen.queryByText('Leave without saving?')).not.toBeInTheDocument()
  })

  it('opens the Overview after a delete without asking, though the draft is unsaved', async () => {
    stubServer()
    const router = renderGuardedPage([entityAddress])
    await editMaxLength()

    await userEvent.click(await screen.findByRole('button', { name: 'Delete…' }))
    // The field drawer is open too, so the dialog is the one that holds Delete.
    await userEvent.click(await screen.findByRole('button', { name: 'Delete' }))

    await waitFor(() => expect(router.state.location.pathname + router.state.location.search).toBe(overview))
    expect(screen.queryByText('Leave without saving?')).not.toBeInTheDocument()
  })
})

describe('StudioPage publishing', () => {
  const requestId = '7a8b9c0d-1e2f-4a3b-9c4d-5e6f70819203'
  const siteId = '3c4d5e6f-7081-4920-a3b4-c5d6e7f80910'
  const releaseId = '0192f4a7-3b1c-7e2d-9a4f-aaaaaaaaaaaa'
  const published: StudioDiagnostic = {
    code: 'AXC0900',
    severity: 'warning',
    message: 'The release has a warning.',
    file: 'application.json',
    path: '',
    resourceId: null,
  }

  function request(status: string, extra: Record<string, unknown> = {}) {
    return {
      id: requestId,
      application: 'PurchaseRequests',
      kind: 'publish',
      target: 'tenant',
      status,
      label: 'May',
      note: null,
      releaseId: null,
      diagnostics: [],
      ...extra,
    }
  }

  function row(id: string, number: number, activatedAt: string) {
    return {
      releaseId: id,
      number,
      contentHash: `hash-${number}`,
      activatedAt,
      actor: 'author',
      actorName: null,
      label: null,
      note: null,
      requestId: null,
      offStandard: false,
      relaxed: [],
    }
  }

  /**
   * `stubServer` where `/api/me` answers `me`, and the request routes answer in turn with `statuses`. The live diff answers
   * `live` and an AI request answers `requestAnswers[id]`, both 404 unless given.
   */
  function stubPublishing(
    me: () => Response,
    statuses: unknown[] = [],
    history: { folderContentHash: string; active: boolean } = { folderContentHash: 'hash-4', active: true },
    live: () => Response | Promise<Response> = () => new Response(null, { status: 404 }),
    requestAnswers: Record<string, () => Response> = {},
  ) {
    const fetchMock = stubServer()
    const base = fetchMock.getMockImplementation()!
    const answers = [...statuses]
    fetchMock.mockImplementation(async (input: string, init?: RequestInit) => {
      if (input === '/api/me') {
        return me()
      }
      if (input.startsWith(`${folder}/releases/live/diff`)) {
        return live()
      }
      if (input.startsWith(`${folder}/authoring-requests/`)) {
        const answer = requestAnswers[input.slice(`${folder}/authoring-requests/`.length)]
        return answer ? answer() : new Response(null, { status: 404 })
      }
      if (input === `${folder}/publish` && init?.method === 'POST') {
        return json(request('queued'), 202)
      }
      if (input === `${folder}/validate` && init?.method === 'POST') {
        return json(request('queued', { kind: 'validate', label: null }), 202)
      }
      if (input === `${folder}/preview` && init?.method === 'POST') {
        return json(request('queued', { target: 'preview', label: null }), 202)
      }
      if (input === `${folder}/resources`) {
        const site = {
          id: siteId,
          kind: 'site',
          name: 'Purchasing',
          file: 'sites/purchasing.json',
          version: 'e',
          usedBy: 0,
        }
        return json({ items: [...resources, site] })
      }
      if (input === `${folder}/resources/${siteId}`) {
        return json({
          id: siteId,
          kind: 'site',
          name: 'Purchasing',
          file: 'sites/purchasing.json',
          version: 'e',
          usedBy: 0,
          content: { id: siteId, kind: 'site', name: 'Purchasing', path: 'purchasing' },
        })
      }
      if (input === `${folder}/requests/${requestId}`) {
        return json(answers.length > 1 ? answers.shift() : answers[0])
      }
      if (input === `${folder}/releases`) {
        return json({
          active: history.active ? { releaseId, number: 3, contentHash: 'hash-3' } : null,
          folderContentHash: history.folderContentHash,
          changes: [],
          items: history.active
            ? [row(releaseId, 3, '2026-05-02T10:00:00Z'), row('older', 2, '2026-05-01T10:00:00Z')]
            : [],
        })
      }
      return base(input, init)
    })
    return fetchMock
  }

  const publisher = () => json({ id: 'author', studio: true, studioPublisher: true, studioPreviewHost: null })
  const previewer =
    (studioPublisher = true) =>
    () =>
      json({ id: 'author', studio: true, studioPublisher, studioPreviewHost: 'preview.example.test' })

  afterEach(() => vi.useRealTimers())

  it.each([
    ['studioPublisher is true', publisher, true],
    ['studioPublisher is false', () => json({ id: 'author', studio: true, studioPublisher: false }), false],
    ['GET /api/me fails', () => new Response(null, { status: 500 }), false],
  ])('shows Publish only when it may: %s', async (_name, me, shown) => {
    stubPublishing(me)
    renderPage()

    expect(await screen.findByRole('button', { name: 'Validate' })).toBeEnabled()
    await waitFor(() => expect(screen.queryAllByRole('button', { name: 'Publish…' })).toHaveLength(shown ? 1 : 0))
  })

  it('opens the publish page from Publish… with its heading, line, fields and action bar', async () => {
    stubPublishing(publisher)
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'Publish…' }))

    expect(screen.getByTestId('location')).toHaveTextContent('/studio?app=PurchaseRequests&view=publish')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Publish this application' })).toBeInTheDocument()
    expect(
      await screen.findByText('Everything below goes live as a new release, in place of release 3.'),
    ).toBeInTheDocument()
    const group = screen.getByRole('group', { name: 'The release' })
    expect(within(group).getByRole('textbox', { name: 'Label' })).toHaveAttribute('maxlength', '100')
    expect(within(group).getByRole('textbox', { name: 'Note' })).toHaveAttribute('maxlength', '2000')
    expect(within(group).getByText('A short name for the release, such as Spring update.')).toBeInTheDocument()
    expect(within(group).getByText('What changed and why, for whoever reads the release list.')).toBeInTheDocument()
    expect(await screen.findByText('Goes live as release 4')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeEnabled()
    await waitFor(() => expect(screen.getByTestId('studio-publish-confirm')).toBeEnabled())
    expect(screen.queryByTestId('studio-publish')).not.toBeInTheDocument()
    // The page marks no sidebar entry.
    expect(document.querySelector('[aria-current="page"]')).toBeNull()
  })

  it('opens the publish page from a link and shows the first-release line when nothing is live', async () => {
    stubPublishing(publisher, [], { folderContentHash: 'hash-1', active: false })
    renderPage('/studio?app=PurchaseRequests&view=publish')

    expect(
      await screen.findByText('Nothing is live yet. Everything below goes live as the first release.'),
    ).toBeInTheDocument()
    expect(screen.getByText('Goes live as release 1')).toBeInTheDocument()
    expect(screen.getByTestId('studio-publish-confirm')).toBeEnabled()
  })

  describe('the files changed since the live release', () => {
    /** A file of the live diff, with the revisions made since the live release that changed it, newest first. */
    function liveFile(file: string, change: 'added' | 'changed', name: string, by: unknown[]) {
      return {
        file,
        change,
        id: null,
        kind: 'role',
        name,
        titles: null,
        before: change === 'added' ? null : '{"label":"first"}',
        after: '{"label":"second"}',
        target: null,
        by,
      }
    }

    function revision(id: string, userId: string | null, author: string, authoringRequest: string | null = null) {
      return {
        id,
        summary: 'Edit',
        author,
        userId,
        client: userId === null ? null : 'studio',
        request: null,
        time: '2026-11-18T10:00:00Z',
        authoringRequest,
      }
    }

    const sectionOf = () => screen.getByTestId('studio-section-publish-changes')
    const liveDiff = (resources: unknown[]) => () => json({ release: 3, resources, cursor: null })

    it('lists the files that differ from the live release with who changed each, under a count', async () => {
      const fetchMock = stubPublishing(
        publisher,
        [],
        { folderContentHash: 'hash-4', active: true },
        liveDiff([
          liveFile('roles/buyer.json', 'changed', 'Buyer', [revision('b', 'author', 'Ana')]),
          liveFile('roles/approver.json', 'added', 'Approver', [
            revision('a2', 'author', 'Ana', 'r1'),
            revision('a1', 'u2', 'Sam Lee'),
          ]),
        ]),
        { r1: () => json({ id: 'r1', text: 'Add a field. Then a test.' }) },
      )
      renderPage('/studio?app=PurchaseRequests&view=publish')

      expect(await screen.findByRole('heading', { name: '2 files changed since the live release' })).toBeVisible()
      const section = sectionOf()
      expect(within(section).getAllByTestId('studio-changes-file')).toHaveLength(2)
      const buyer = within(section)
        .getAllByTestId('studio-changes-file')
        .find((file) => file.dataset.file === 'roles/buyer.json')!
      expect(within(buyer).getByTestId('studio-changes-by')).toHaveTextContent(/^Changed by you$/)
      const approver = within(section)
        .getAllByTestId('studio-changes-file')
        .find((file) => file.dataset.file === 'roles/approver.json')!
      expect(within(approver).getByTestId('studio-changes-by')).toHaveTextContent(
        'Changed by Sam Lee and the AI request “Add a field.”',
      )
      expect(within(approver).getByRole('link', { name: 'the AI request “Add a field.”' })).toHaveAttribute(
        'href',
        '/studio?app=PurchaseRequests&view=requests&request=r1',
      )
      expect(fetchMock.mock.calls.map(([input]) => input)).toContain(`${folder}/releases/live/diff`)
    })

    it('names the person who accepted an AI request that cannot be read', async () => {
      stubPublishing(
        publisher,
        [],
        { folderContentHash: 'hash-4', active: true },
        liveDiff([liveFile('roles/approver.json', 'added', 'Approver', [revision('a1', 'u2', 'Sam Lee', 'r1')])]),
        { r1: () => new Response(null, { status: 500 }) },
      )
      renderPage('/studio?app=PurchaseRequests&view=publish')

      expect(await screen.findByRole('heading', { name: '1 file changed since the live release' })).toBeVisible()
      const change = within(sectionOf()).getByTestId('studio-changes-by')
      expect(change).toHaveTextContent(/^Changed by Sam Lee$/)
      expect(within(change).queryByRole('link')).toBeNull()
    })

    it('asks for no live diff and shows no list when nothing is live yet', async () => {
      const fetchMock = stubPublishing(publisher, [], { folderContentHash: 'hash-1', active: false }, liveDiff([]))
      renderPage('/studio?app=PurchaseRequests&view=publish')

      expect(
        await screen.findByText('Nothing is live yet. Everything below goes live as the first release.'),
      ).toBeVisible()
      expect(screen.queryByTestId('studio-section-publish-changes')).toBeNull()
      expect(fetchMock.mock.calls.some(([input]) => String(input).includes('/releases/live/diff'))).toBe(false)
    })

    it('shows no list when the server has no revision history', async () => {
      const fetchMock = stubPublishing(publisher)
      renderPage('/studio?app=PurchaseRequests&view=publish')

      expect(await screen.findByText('Goes live as release 4')).toBeVisible()
      await waitFor(() =>
        expect(fetchMock.mock.calls.map(([input]) => input)).toContain(`${folder}/releases/live/diff`),
      )
      expect(screen.queryByTestId('studio-section-publish-changes')).toBeNull()
      await waitFor(() => expect(screen.getByTestId('studio-publish-confirm')).toBeEnabled())
    })

    it('keeps Publish off while the changed files load, and turns it on when the list shows', async () => {
      let answer: (response: Response) => void = () => undefined
      let requested: () => void = () => undefined
      const diffRequested = new Promise<void>((resolve) => (requested = resolve))
      stubPublishing(publisher, [], { folderContentHash: 'hash-4', active: true }, () => {
        requested()
        return new Promise<Response>((resolve) => (answer = resolve))
      })
      renderPage('/studio?app=PurchaseRequests&view=publish')

      expect(
        await screen.findByText('Everything below goes live as a new release, in place of release 3.'),
      ).toBeVisible()
      expect(screen.getByTestId('studio-publish-confirm')).toBeDisabled()

      // The diff request starts after the intro text shows, so wait for it: an answer sent before it is lost.
      await diffRequested
      answer(
        json({
          release: 3,
          resources: [liveFile('roles/employee.json', 'changed', 'Employee', [])],
          cursor: null,
        }),
      )

      expect(await screen.findByRole('heading', { name: '1 file changed since the live release' })).toBeVisible()
      expect(screen.getByTestId('studio-publish-confirm')).toBeEnabled()
    })
  })

  it('shows the Overview at &view=publish without studioPublisher', async () => {
    stubPublishing(() => json({ id: 'author', studio: true, studioPublisher: false }))
    renderPage('/studio?app=PurchaseRequests&view=publish')

    expect(await screen.findByRole('button', { name: 'Validate' })).toBeEnabled()
    expect(screen.queryByRole('heading', { name: 'Publish this application' })).not.toBeInTheDocument()
    expect(screen.queryByTestId('studio-publish-confirm')).not.toBeInTheDocument()
  })

  it('says everything is live and turns Publish off when the folder is the live release', async () => {
    stubPublishing(publisher, [], { folderContentHash: 'hash-3', active: true })
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'Publish…' }))

    expect(await screen.findByText('Everything is live, so there is nothing to publish.')).toBeInTheDocument()
    expect(screen.getByTestId('studio-publish-confirm')).toBeDisabled()
    expect(screen.queryByText(/Goes live as release/)).not.toBeInTheDocument()
  })

  it('reloads the release history when the publish page opens', async () => {
    const fetchMock = stubPublishing(publisher, [], { folderContentHash: 'hash-3', active: true })
    renderPage('/studio?app=PurchaseRequests&view=releases')
    const publish = await screen.findByRole('button', { name: 'Publish…' })
    await waitFor(() => expect(fetchMock.mock.calls.some(([input]) => input === `${folder}/releases`)).toBe(true))

    const loaded = fetchMock.getMockImplementation()!
    fetchMock.mockImplementation(async (input: string, init?: RequestInit) => {
      if (input === `${folder}/releases`) {
        const answer = await (await loaded(input, init)).json()
        return json({ ...answer, folderContentHash: 'hash-4' })
      }
      return loaded(input, init)
    })
    await userEvent.click(publish)

    expect(await screen.findByText('Goes live as release 4')).toBeInTheDocument()
    await waitFor(() => expect(screen.getByTestId('studio-publish-confirm')).toBeEnabled())
  })

  it('goes back to where Publish… was opened when Cancel is chosen, and sends nothing', async () => {
    const fetchMock = stubPublishing(publisher)
    renderPage('/studio?app=PurchaseRequests&view=releases')

    await userEvent.click(await screen.findByRole('button', { name: 'Publish…' }))
    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }))

    expect(screen.getByTestId('location')).toHaveTextContent(/^\/studio\?app=PurchaseRequests&view=releases$/)
    expect(fetchMock.mock.calls.some(([input, init]) => input === `${folder}/publish` && init?.method === 'POST')).toBe(
      false,
    )
  })

  it('goes to the Overview from Cancel on a publish page opened from a link', async () => {
    stubPublishing(publisher)
    renderPage('/studio?app=PurchaseRequests&view=publish')

    await userEvent.click(await screen.findByRole('button', { name: 'Cancel' }))

    expect(screen.getByTestId('location')).toHaveTextContent(/^\/studio\?app=PurchaseRequests$/)
  })

  it('sends only the filled values, goes back and shows the status there', async () => {
    const fetchMock = stubPublishing(publisher)
    renderPage('/studio?app=PurchaseRequests&view=releases')
    await userEvent.click(await screen.findByRole('button', { name: 'Publish…' }))
    await userEvent.type(screen.getByRole('textbox', { name: 'Note' }), 'Added the approval step')
    await userEvent.click(await screen.findByText('Goes live as release 4'))
    await waitFor(() => expect(screen.getByTestId('studio-publish-confirm')).toBeEnabled())
    await userEvent.click(screen.getByTestId('studio-publish-confirm'))

    expect(await screen.findByTestId('studio-request-status')).toHaveAttribute('data-status', 'queued')
    expect(screen.getByTestId('location')).toHaveTextContent(/^\/studio\?app=PurchaseRequests&view=releases$/)
    expect(screen.queryByRole('heading', { name: 'Publish this application' })).not.toBeInTheDocument()
    const posts = fetchMock.mock.calls.filter(
      ([input, init]) => input === `${folder}/publish` && init?.method === 'POST',
    )
    expect(posts).toHaveLength(1)
    expect(JSON.parse(posts[0][1]!.body as string)).toEqual({ note: 'Added the approval step' })
  })

  it("heads the band's actions with New resource… and ends them with Publish…", async () => {
    stubPublishing(publisher)
    renderPage()

    await screen.findByRole('button', { name: 'Publish…' })
    const names = within(screen.getByTestId('studio-band'))
      .getAllByRole('button')
      .filter((button) => button.dataset.testid !== 'studio-problems')
      .map((button) => button.textContent)
    expect(names[0]).toBe('New resource…')
    expect(names[1]).toBe('Validate')
    expect(names.at(-1)).toBe('Publish…')
  })

  it('shows the status while a publish runs and the release number when it succeeds', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const fetchMock = stubPublishing(publisher, [
      request('running'),
      request('succeeded', { releaseId, diagnostics: [published] }),
    ])
    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: 'Publish…' }))
    await userEvent.type(screen.getByRole('textbox', { name: 'Label' }), 'May')
    await userEvent.click(await screen.findByText('Goes live as release 4'))
    await waitFor(() => expect(screen.getByTestId('studio-publish-confirm')).toBeEnabled())
    await userEvent.click(screen.getByTestId('studio-publish-confirm'))
    expect(screen.getByTestId('location')).toHaveTextContent(/^\/studio\?app=PurchaseRequests$/)

    const status = await screen.findByTestId('studio-request-status')
    expect(status).toHaveAttribute('data-status', 'queued')
    expect(screen.getByRole('button', { name: 'Validate' })).toBeDisabled()
    expect(screen.getByTestId('studio-publish')).toBeDisabled()
    const posts = fetchMock.mock.calls.filter(
      ([input, init]) => input === `${folder}/publish` && init?.method === 'POST',
    )
    expect(JSON.parse(posts[0][1]!.body as string)).toEqual({ label: 'May' })
    expect(new Headers(posts[0][1]!.headers).get('Content-Type')).toBe('application/json')

    await vi.advanceTimersByTimeAsync(1000)
    await waitFor(() => expect(screen.getByTestId('studio-request-status')).toHaveAttribute('data-status', 'running'))

    await vi.advanceTimersByTimeAsync(1000)
    await waitFor(() => expect(screen.getByTestId('studio-request-status')).toHaveAttribute('data-status', 'succeeded'))
    expect(screen.getByTestId('studio-release-number')).toHaveTextContent('3')
    const panel = await openProblems()
    expect(within(panel).getByText('AXC0900')).toBeInTheDocument()
    expect(within(panel).queryByText('AXC0101')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Validate' })).toBeEnabled()
  })

  it('shows the new release in the table after a publish from the band on the Releases page', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const fetchMock = stubPublishing(publisher, [
      request('running'),
      request('succeeded', { releaseId, diagnostics: [published] }),
    ])
    const base = fetchMock.getMockImplementation()!
    let sent = false
    fetchMock.mockImplementation(async (input: string, init?: RequestInit) => {
      if (input === `${folder}/publish` && init?.method === 'POST') {
        sent = true
      }
      if (input === `${folder}/releases` && !sent) {
        return json({
          active: { releaseId: 'older', number: 2, contentHash: 'hash-2' },
          folderContentHash: 'hash-3',
          changes: [],
          items: [row('older', 2, '2026-05-01T10:00:00Z')],
        })
      }
      return base(input, init)
    })
    renderPage('/studio?app=PurchaseRequests&view=releases')
    expect(
      await within(await screen.findByTestId('studio-releases')).findAllByTestId('studio-release-row'),
    ).toHaveLength(1)

    await userEvent.click(await screen.findByRole('button', { name: 'Publish…' }))
    await userEvent.click(await screen.findByText('Goes live as release 3'))
    await waitFor(() => expect(screen.getByTestId('studio-publish-confirm')).toBeEnabled())
    await userEvent.click(screen.getByTestId('studio-publish-confirm'))

    await vi.advanceTimersByTimeAsync(1000)
    await vi.advanceTimersByTimeAsync(1000)
    await waitFor(() => expect(screen.getByTestId('studio-request-status')).toHaveAttribute('data-status', 'succeeded'))
    await waitFor(() =>
      expect(
        within(screen.getByTestId('studio-releases'))
          .getAllByTestId('studio-release-row')
          .map((item) => item.dataset.number),
      ).toEqual(['3', '2']),
    )
  })

  it('activates an older release from the table on the Releases page and marks it live', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const fetchMock = stubPublishing(publisher, [
      request('running', { kind: 'activate', releaseId: 'older' }),
      request('succeeded', { kind: 'activate', releaseId: 'older' }),
    ])
    const base = fetchMock.getMockImplementation()!
    let activated = false
    fetchMock.mockImplementation(async (input: string, init?: RequestInit) => {
      if (input === `${folder}/releases/older/activate` && init?.method === 'POST') {
        activated = true
        return json(request('queued', { kind: 'activate', releaseId: 'older' }), 202)
      }
      if (input === `${folder}/releases/2`) {
        return json({
          number: 2,
          previous: 3,
          previousIsLive: true,
          live: false,
          changes: [],
          partChanges: [],
          modules: [],
          packages: [],
          blockers: [],
        })
      }
      if (input === `${folder}/releases` && activated) {
        return json({
          active: { releaseId: 'older', number: 2, contentHash: 'hash-2' },
          folderContentHash: 'hash-2',
          changes: [],
          items: [row(releaseId, 3, '2026-05-02T10:00:00Z'), row('older', 2, '2026-05-01T10:00:00Z')],
        })
      }
      return base(input, init)
    })
    renderPage('/studio?app=PurchaseRequests&view=releases')
    const olderRow = async () =>
      (await within(await screen.findByTestId('studio-releases')).findAllByTestId('studio-release-row')).find(
        (item) => item.dataset.number === '2',
      )!
    expect(await olderRow()).not.toHaveAttribute('data-active')

    await userEvent.click(
      await within(await screen.findByTestId('studio-releases')).findByRole('button', { name: 'Activate release 2' }),
    )
    await userEvent.click(await within(screen.getByRole('dialog')).findByRole('button', { name: 'Activate' }))

    await vi.advanceTimersByTimeAsync(1000)
    await vi.advanceTimersByTimeAsync(1000)
    await waitFor(() => expect(screen.getByTestId('studio-request-status')).toHaveAttribute('data-status', 'succeeded'))
    await waitFor(async () => expect(await olderRow()).toHaveAttribute('data-active', 'true'))
    expect(
      fetchMock.mock.calls.some(
        ([input, init]) => input === `${folder}/releases/older/activate` && init?.method === 'POST',
      ),
    ).toBe(true)
  })

  it('shows the quota title when a publish is refused past the storage quota', async () => {
    const fetchMock = stubPublishing(publisher)
    const base = fetchMock.getMockImplementation()!
    fetchMock.mockImplementation(async (input: string, init?: RequestInit) =>
      input === `${folder}/publish` && init?.method === 'POST'
        ? new Response(
            JSON.stringify({
              title: 'This tenant has reached its storage quota.',
              status: 403,
              reason: 'quota-exceeded',
              quota: 'storage',
            }),
            { status: 403 },
          )
        : base(input, init),
    )
    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: 'Publish…' }))
    await userEvent.click(await screen.findByText('Goes live as release 4'))
    await waitFor(() => expect(screen.getByTestId('studio-publish-confirm')).toBeEnabled())
    await userEvent.click(screen.getByTestId('studio-publish-confirm'))

    expect(await screen.findByTestId('studio-request-error')).toHaveTextContent(
      'This tenant has reached its storage quota.',
    )
  })

  // jsdom has no layout, so the band's row is faked 300 px wide for 400 px of content, and it folds.
  describe('with too little room', () => {
    beforeEach(() => {
      const onRow = (element: HTMLElement, width: number) => (element.hasAttribute('data-fold-row') ? width : 0)
      vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockImplementation(function (this: HTMLElement) {
        return onRow(this, 300)
      })
      vi.spyOn(HTMLElement.prototype, 'scrollWidth', 'get').mockImplementation(function (this: HTMLElement) {
        return onRow(this, 400)
      })
    })
    afterEach(() => vi.restoreAllMocks())

    it.each([
      ['an author who may publish', true, ['New resource…', 'Publish…']],
      ['an author who may not publish', false, ['New resource…']],
    ])('folds Validate, Preview and Run tests into the More actions menu for %s', async (_name, mayPublish, kept) => {
      stubPublishing(() =>
        json({
          id: 'author',
          studio: true,
          studioPublisher: mayPublish,
          studioTest: true,
          studioPreviewHost: 'preview.example.test',
        }),
      )
      renderPage()

      const more = await screen.findByRole('button', { name: 'More actions' })
      const band = within(screen.getByTestId('studio-band'))
      await waitFor(() =>
        expect(
          band
            .getAllByRole('button')
            .filter((button) => button.dataset.testid !== 'studio-problems')
            .map((button) => button.textContent || button.ariaLabel),
        ).toEqual([kept[0], 'More actions', ...kept.slice(1)]),
      )
      await userEvent.click(more)

      expect(
        within(await screen.findByRole('menu', { name: 'More actions' }))
          .getAllByRole('menuitem')
          .map((entry) => entry.textContent),
      ).toEqual(['Validate', 'Preview', 'Run tests'])
    })
  })

  it.each([
    ['studioPreviewHost is null', publisher, false],
    ['studioPreviewHost is set', previewer(), true],
    ['studioPreviewHost is set and studioPublisher is false', previewer(false), true],
    ['GET /api/me fails', () => new Response(null, { status: 500 }), false],
  ])('shows Preview only when there is a preview host: %s', async (_name, me, shown) => {
    stubPublishing(me)
    renderPage()

    expect(await screen.findByRole('button', { name: 'Validate' })).toBeEnabled()
    await waitFor(() => expect(screen.queryAllByTestId('studio-preview')).toHaveLength(shown ? 1 : 0))
  })

  it('links a succeeded preview to each site on the preview host and shows no release number', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const fetchMock = stubPublishing(previewer(false), [
      request('running', { target: 'preview' }),
      request('succeeded', { target: 'preview', releaseId }),
    ])
    renderPage()
    await userEvent.click(await screen.findByTestId('studio-preview'))

    expect(await screen.findByTestId('studio-request-status')).toHaveAttribute('data-status', 'queued')
    expect(screen.getByTestId('studio-preview')).toBeDisabled()
    expect(screen.queryByTestId('studio-preview-link')).not.toBeInTheDocument()
    await vi.advanceTimersByTimeAsync(1000)
    await waitFor(() => expect(screen.getByTestId('studio-request-status')).toHaveAttribute('data-status', 'running'))

    await vi.advanceTimersByTimeAsync(1000)
    await waitFor(() => expect(screen.getByTestId('studio-request-status')).toHaveAttribute('data-status', 'succeeded'))
    const link = await screen.findByTestId('studio-preview-link')
    expect(link.getAttribute('href')).toContain('//preview.example.test')
    expect(link.getAttribute('href')).toMatch(/\/purchasing$/)
    expect(screen.queryByTestId('studio-release-number')).not.toBeInTheDocument()
    expect(screen.getByTestId('studio-preview')).toBeEnabled()
    const posts = fetchMock.mock.calls.filter(
      ([input, init]) => input === `${folder}/preview` && init?.method === 'POST',
    )
    expect(posts).toHaveLength(1)
  })

  it('shows no preview link when the preview fails, and puts its diagnostics in the panel', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    stubPublishing(previewer(), [request('failed', { target: 'preview', diagnostics: [savedDiagnostic] })])
    renderPage()
    await userEvent.click(await screen.findByTestId('studio-preview'))
    await screen.findByTestId('studio-request-status')

    await vi.advanceTimersByTimeAsync(1000)

    await waitFor(() => expect(screen.getByTestId('studio-request-status')).toHaveAttribute('data-status', 'failed'))
    expect(screen.getByTestId('studio-request-status')).toHaveTextContent('Preview failed.')
    expect(screen.queryByTestId('studio-preview-link')).not.toBeInTheDocument()
    expect(within(await openProblems()).getByText('AXC0777')).toBeInTheDocument()
  })

  it('puts the diagnostics of a failed validate in the panel and shows no release number', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    stubPublishing(publisher, [request('failed', { kind: 'validate', releaseId, diagnostics: [savedDiagnostic] })])
    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: 'Validate' }))
    await screen.findByTestId('studio-request-status')

    await vi.advanceTimersByTimeAsync(1000)

    await waitFor(() => expect(screen.getByTestId('studio-request-status')).toHaveAttribute('data-status', 'failed'))
    expect(screen.queryByTestId('studio-release-number')).not.toBeInTheDocument()
    expect(within(await openProblems()).getByText('AXC0777')).toBeInTheDocument()
  })

  it('keeps checking the request after a reconnect', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    let down = false
    const fetchMock = stubPublishing(publisher, [request('succeeded', { releaseId })])
    const base = fetchMock.getMockImplementation()!
    fetchMock.mockImplementation(async (input: string, init?: RequestInit) => {
      if (down && (input === `${folder}/requests/${requestId}` || input === '/api/me')) {
        return new Response(null, { status: 503 })
      }
      return base(input, init)
    })
    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: 'Validate' }))
    await screen.findByTestId('studio-request-status')
    down = true

    await vi.advanceTimersByTimeAsync(1000)
    expect(await screen.findByTestId('studio-reconnecting')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Validate' })).toBeDisabled()
    down = false
    await vi.advanceTimersByTimeAsync(1000)
    await waitFor(() => expect(screen.queryByTestId('studio-reconnecting')).not.toBeInTheDocument())
    await vi.advanceTimersByTimeAsync(1000)

    await waitFor(() => expect(screen.getByTestId('studio-request-status')).toHaveAttribute('data-status', 'succeeded'))
  })

  describe('New application…', () => {
    function stubCreateAccess(studioCreate: boolean, apps: { name: string }[] = [{ name: 'PurchaseRequests' }]) {
      const fetchMock = stubServer(apps)
      const base = fetchMock.getMockImplementation()!
      fetchMock.mockImplementation(async (input: string, init?: RequestInit) =>
        input === '/api/me' ? json({ id: 'author', studio: true, studioCreate }) : base(input, init),
      )
      return fetchMock
    }

    it.each([
      ['studioCreate is true', true, true],
      ['studioCreate is false', false, false],
    ])(
      'shows New application… in the application row only when it may create: %s',
      async (_name, studioCreate, shown) => {
        stubCreateAccess(studioCreate)
        renderPage()

        await userEvent.click(await screen.findByTestId('studio-application-row'))
        await waitFor(() =>
          expect(screen.queryAllByRole('menuitem', { name: 'New application…' })).toHaveLength(shown ? 1 : 0),
        )
      },
    )

    it('opens the New application dialog from the application row menu', async () => {
      stubCreateAccess(true)
      renderPage()

      await userEvent.click(await screen.findByTestId('studio-application-row'))
      await userEvent.click(await screen.findByRole('menuitem', { name: 'New application…' }))

      expect(await screen.findByRole('dialog', { name: 'New application' })).toBeInTheDocument()
    })

    it('lays the New application form out with each label above its input', async () => {
      stubCreateAccess(true)
      renderPage()

      await userEvent.click(await screen.findByTestId('studio-application-row'))
      await userEvent.click(await screen.findByRole('menuitem', { name: 'New application…' }))

      const dialog = await screen.findByRole('dialog', { name: 'New application' })
      expect(dialog.querySelector('form')).toHaveClass('ant-form-vertical')
    })

    it.each([
      ['studioCreate is true', true, true],
      ['studioCreate is false', false, false],
    ])('shows New application… on the list only when it may create: %s', async (_name, studioCreate, shown) => {
      stubCreateAccess(studioCreate)
      renderPage('/studio')

      expect(await screen.findByTestId('studio-app-PurchaseRequests')).toBeInTheDocument()
      await waitFor(() =>
        expect(screen.queryAllByRole('button', { name: 'New application…' })).toHaveLength(shown ? 1 : 0),
      )
    })

    it('shows New application… when Studio lists no application', async () => {
      stubCreateAccess(true, [])
      renderPage()

      expect(await screen.findByTestId('studio-no-applications')).toBeInTheDocument()
      expect(await screen.findByRole('button', { name: 'New application…' })).toBeInTheDocument()
    })
  })

  describe('New resource', () => {
    const siteId = '11111111-2222-4333-8444-555555555555'
    const enId = '66666666-7777-4888-8999-aaaaaaaaaaaa'
    const viId = 'bbbbbbbb-cccc-4ddd-8eee-ffffffffffff'
    const newId = '01234567-89ab-4cde-8f01-23456789abcd'
    const withTexts: StudioResourceItem[] = [
      ...resources,
      { id: siteId, kind: 'site', name: 'Purchasing', file: 'sites/purchasing.json', version: 'e', usedBy: 0 },
      { id: enId, kind: 'text', name: 'TextsEn', file: 'texts/en.json', version: 'f', usedBy: 0 },
      { id: viId, kind: 'text', name: 'TextsVi', file: 'texts/vi.json', version: 'g', usedBy: 0 },
    ]

    /** The base stub, with a site that defaults to `vi`, two text resources, and a create that answers `status`. */
    function stubCreate(status: number) {
      const fetchMock = stubServer()
      const base = fetchMock.getMockImplementation()!
      fetchMock.mockImplementation(async (input: string, init?: RequestInit) => {
        if (input === `${folder}/resources` && init?.method === 'POST') {
          return status === 201
            ? json(
                {
                  id: newId,
                  kind: 'text',
                  name: 'TextsDe',
                  file: 'texts/de.json',
                  version: 'h',
                  diagnostics: [savedDiagnostic],
                },
                201,
              )
            : json({ title: 'No.' }, status)
        }
        if (input === `${folder}/resources`) {
          return json({ items: withTexts })
        }
        if (input === `${folder}/resources/${siteId}`) {
          return json({
            ...withTexts[4],
            content: { locales: { default: 'vi', fallback: 'en', available: ['en', 'vi'] } },
          })
        }
        if (input === `${folder}/resources/${enId}`) {
          return json({
            ...withTexts[5],
            content: { locale: 'en', texts: { 'app.title': 'Title', 'app.save': 'Save' } },
          })
        }
        if (input === `${folder}/resources/${viId}`) {
          return json({ ...withTexts[6], content: { locale: 'vi', texts: { 'app.title': 'Tiêu đề' } } })
        }
        return base(input, init)
      })
      return fetchMock
    }

    async function fillDialog(kind: string, value: string, valueLabel: string) {
      await userEvent.click(await screen.findByRole('button', { name: 'New resource…' }))
      await userEvent.click(screen.getByRole('combobox', { name: 'Kind' }))
      await userEvent.click(await screen.findByTitle(kind))
      await userEvent.type(screen.getByRole('textbox', { name: valueLabel }), value)
      await userEvent.click(screen.getByRole('button', { name: 'Create' }))
    }

    it('asks for a kind in the sidebar groups, migrations included, then for a name', async () => {
      stubCreate(201)
      renderPage()

      await userEvent.click(await screen.findByRole('button', { name: 'New resource…' }))
      await userEvent.click(screen.getByRole('combobox', { name: 'Kind' }))

      const headings = [...document.querySelectorAll('.ant-select-item-group')].map((group) => group.textContent)
      expect(headings).toEqual(['Data', 'Pages', 'Logic', 'Access', 'Integration', 'Texts', 'Tests'])
      const data = [...document.querySelectorAll('.ant-select-item-option')].map((option) =>
        option.getAttribute('title'),
      )
      expect(data.indexOf('Migration')).toBeGreaterThan(data.indexOf('Entity'))
      expect(data.indexOf('Migration')).toBeLessThan(data.indexOf('Site'))
      await userEvent.click(await screen.findByTitle('Migration'))
      expect(screen.getByRole('textbox', { name: 'Name' })).toBeInTheDocument()
    })

    it('keeps Create on, marks Kind and Name required and checks them on a click', async () => {
      const fetchMock = stubCreate(201)
      renderPage()

      await userEvent.click(await screen.findByRole('button', { name: 'New resource…' }))

      expect(screen.getByRole('button', { name: 'Create' })).toBeEnabled()
      expect(isRequiredLabel(screen.getByText('Kind'))).toBe(true)
      expect(isRequiredLabel(screen.getByText('Name'))).toBe(true)
      expect(screen.getByRole('textbox', { name: 'Name' })).toHaveAccessibleDescription(
        expect.stringContaining('Letters and digits'),
      )

      await userEvent.click(screen.getByRole('button', { name: 'Create' }))

      expect(await screen.findByText('Choose a kind.')).toBeInTheDocument()
      expect(screen.getByRole('combobox', { name: 'Kind' })).toHaveFocus()
      expect(fetchMock.mock.calls.some(([, call]) => call?.method === 'POST')).toBe(false)

      await userEvent.click(screen.getByRole('combobox', { name: 'Kind' }))
      await userEvent.click(await screen.findByTitle('Entity'))
      await waitFor(() => expect(screen.queryByText('Choose a kind.')).not.toBeInTheDocument())
      await userEvent.click(screen.getByRole('button', { name: 'Create' }))

      expect(await screen.findByText('Enter a name.')).toBeInTheDocument()
      expect(screen.getByRole('textbox', { name: 'Name' })).toHaveFocus()
      expect(fetchMock.mock.calls.some(([, call]) => call?.method === 'POST')).toBe(false)

      await userEvent.type(screen.getByRole('textbox', { name: 'Name' }), 'CostCentre')

      await waitFor(() => expect(screen.queryByText('Enter a name.')).not.toBeInTheDocument())
    })

    it('checks the locale of a kind that is text, and starts empty on each opening', async () => {
      stubCreate(201)
      renderPage('/studio?app=PurchaseRequests&kind=text')

      await userEvent.click(await screen.findByRole('button', { name: 'Add language…' }))
      expect(isRequiredLabel(screen.getByText('Locale'))).toBe(true)
      await userEvent.click(screen.getByRole('button', { name: 'Create' }))

      expect(await screen.findByText('Enter a locale.')).toBeInTheDocument()
      expect(screen.getByRole('textbox', { name: 'Locale' })).toHaveFocus()

      await userEvent.type(screen.getByRole('textbox', { name: 'Locale' }), 'de')
      await userEvent.click(screen.getByRole('button', { name: 'Cancel' }))
      await userEvent.click(screen.getByRole('button', { name: 'Add language…' }))

      expect(screen.getByRole('textbox', { name: 'Locale' })).toHaveValue('')
      expect(screen.queryByText('Enter a locale.')).not.toBeInTheDocument()
    })

    it("asks a kind's New entity… for the name alone", async () => {
      const fetchMock = stubCreate(201)
      renderPage('/studio?app=PurchaseRequests&kind=entity')

      await userEvent.click(await screen.findByRole('button', { name: 'New entity…' }))

      // The table's page size picker takes the id that antd gives every id in tests, so the dialog is found by its title.
      const dialog = await screen.findByRole('dialog')
      expect(within(dialog).getByText('New entity')).toBeVisible()
      expect(within(dialog).queryByRole('combobox', { name: 'Kind' })).not.toBeInTheDocument()
      const create = within(dialog).getByRole('button', { name: 'Create' })
      expect(create).toBeEnabled()
      await userEvent.type(within(dialog).getByRole('textbox', { name: 'Name' }), 'CostCentre')
      expect(create).toBeEnabled()
      await userEvent.click(create)

      await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('resource='))
      const [, init] = fetchMock.mock.calls.find(([, call]) => call?.method === 'POST')!
      expect(JSON.parse(init!.body as string)).toMatchObject({ kind: 'entity', name: 'CostCentre' })
    })

    it("creates once when Enter is pressed in a kind's New entity…", async () => {
      const fetchMock = stubCreate(201)
      renderPage('/studio?app=PurchaseRequests&kind=entity')

      await userEvent.click(await screen.findByRole('button', { name: 'New entity…' }))

      // The table's page size picker takes the id that antd gives every id in tests, so the dialog is found by its title.
      const dialog = await screen.findByRole('dialog')
      expect(within(dialog).getByText('New entity')).toBeVisible()
      await userEvent.type(within(dialog).getByRole('textbox', { name: 'Name' }), 'CostCentre{Enter}')

      await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('resource='))
      expect(fetchMock.mock.calls.filter(([, call]) => call?.method === 'POST')).toHaveLength(1)
    })

    it("asks a kind's Add language… for a locale alone", async () => {
      const fetchMock = stubCreate(201)
      renderPage('/studio?app=PurchaseRequests&kind=text')

      await userEvent.click(await screen.findByRole('button', { name: 'Add language…' }))

      const dialog = await screen.findByRole('dialog', { name: 'Add language' })
      expect(within(dialog).queryByRole('combobox', { name: 'Kind' })).not.toBeInTheDocument()
      expect(within(dialog).queryByRole('textbox', { name: 'Name' })).not.toBeInTheDocument()
      await userEvent.type(within(dialog).getByRole('textbox', { name: 'Locale' }), 'de')
      await userEvent.click(within(dialog).getByRole('button', { name: 'Create' }))

      await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent(`resource=${newId}`))
      const [, init] = fetchMock.mock.calls.find(([, call]) => call?.method === 'POST')!
      expect(JSON.parse(init!.body as string)).toMatchObject({ kind: 'text', name: 'TextsDe', locale: 'de' })
    })

    it("shows a kind's conflict under the field and keeps its dialog open", async () => {
      stubCreate(409)
      renderPage('/studio?app=PurchaseRequests&kind=entity')

      await userEvent.click(await screen.findByRole('button', { name: 'New entity…' }))
      await userEvent.type(screen.getByRole('textbox', { name: 'Name' }), 'CostCentre')
      await userEvent.click(screen.getByRole('button', { name: 'Create' }))

      expect(await screen.findByText('A resource with this name or locale already exists.')).toBeInTheDocument()
      expect(within(screen.getByRole('dialog')).getByText('New entity')).toBeVisible()
    })

    it('offers no create button on the list of a kind that has no starter', async () => {
      stubCreate(201)
      renderPage('/studio?app=PurchaseRequests&kind=layer')

      expect(await screen.findByRole('heading', { name: 'Layers' })).toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'New resource…' })).toBeInTheDocument()
      expect(screen.queryByRole('button', { name: /^New (?!resource…)/ })).not.toBeInTheDocument()
    })

    it('narrows the kinds to what is typed', async () => {
      stubCreate(201)
      renderPage()

      await userEvent.click(await screen.findByRole('button', { name: 'New resource…' }))
      await userEvent.type(screen.getByRole('combobox', { name: 'Kind' }), 'proc')

      expect(screen.getAllByRole('option').map((option) => option.textContent)).toEqual(['Process'])
      await userEvent.click(screen.getByRole('option', { name: 'Process' }))
      expect(screen.getByRole('button', { name: 'Create' })).toBeEnabled()
      await userEvent.type(screen.getByRole('textbox', { name: 'Name' }), 'Approval')
      expect(screen.getByRole('button', { name: 'Create' })).toBeEnabled()
    })

    it('creates a text resource that copies the texts of the default locale of the first site', async () => {
      const fetchMock = stubCreate(201)
      renderPage()

      await fillDialog('Language', 'de', 'Locale')

      await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent(`resource=${newId}`))
      const [, init] = fetchMock.mock.calls.find(([, call]) => call?.method === 'POST')!
      const body = JSON.parse(init!.body as string) as Record<string, unknown>
      expect(body).toMatchObject({
        kind: 'text',
        name: 'TextsDe',
        formatVersion: 1,
        locale: 'de',
        texts: { 'app.title': 'Tiêu đề' },
      })
      expect(body.id).toMatch(/^[0-9a-f-]{36}$/)
      expect(screen.getByTestId('studio-resource-details')).toBeInTheDocument()
      expect(within(await openProblems()).getByText('AXC0777')).toBeInTheDocument()
    })

    it('creates a webhook that names no event and no binding yet', async () => {
      const fetchMock = stubCreate(201)
      renderPage()

      await fillDialog('Webhook', 'NoteClosedChat', 'Name')

      await waitFor(() => expect(fetchMock.mock.calls.some(([, call]) => call?.method === 'POST')).toBe(true))
      const [, init] = fetchMock.mock.calls.find(([, call]) => call?.method === 'POST')!
      const body = JSON.parse(init!.body as string) as Record<string, unknown>
      expect(body).toMatchObject({
        kind: 'webhook',
        name: 'NoteClosedChat',
        formatVersion: 1,
        event: '',
        url: { binding: '' },
        secret: { binding: '' },
      })
      expect(body.id).toMatch(/^[0-9a-f-]{36}$/)
    })

    it('copies the en texts when the application has no site', async () => {
      const fetchMock = stubCreate(201)
      const base = fetchMock.getMockImplementation()!
      fetchMock.mockImplementation(async (input: string, init?: RequestInit) =>
        input === `${folder}/resources` && init?.method !== 'POST'
          ? json({ items: withTexts.filter((item) => item.kind !== 'site') })
          : base(input, init),
      )
      renderPage()

      await fillDialog('Language', 'vi', 'Locale')

      await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent(`resource=${newId}`))
      const [, init] = fetchMock.mock.calls.find(([, call]) => call?.method === 'POST')!
      expect((JSON.parse(init!.body as string) as { texts: unknown }).texts).toEqual({
        'app.title': 'Title',
        'app.save': 'Save',
      })
    })

    it('shows a conflict under the field and keeps the dialog open', async () => {
      stubCreate(409)
      renderPage()

      await fillDialog('Entity', 'CostCentre', 'Name')

      expect(await screen.findByText('A resource with this name or locale already exists.')).toBeInTheDocument()
      expect(screen.getByRole('textbox', { name: 'Name' })).toBeInTheDocument()
      expect(screen.getByTestId('location')).not.toHaveTextContent('resource=')
    })

    it('shows a refused name under the field', async () => {
      stubCreate(400)
      renderPage()

      await fillDialog('Entity', 'Cost Centre', 'Name')

      expect(await screen.findByText(/This name or locale is not allowed/)).toBeInTheDocument()
    })
  })
})

describe('StudioPage process graph marks', () => {
  const processItem: StudioResourceItem = {
    id: purchaseRequestApproval.id,
    kind: 'process',
    name: 'PurchaseRequestApproval',
    file: 'processes/purchase-request-approval.json',
    version: 'p1',
    usedBy: 0,
  }
  const processPath = `${folder}/resources/${processItem.id}`

  /** Stubs the base server, and adds the process with the given folder diagnostics and its own content and references. */
  function stubProcess(processDiagnostics: StudioDiagnostic[]) {
    const base = stubServer()
    const fetchMock = vi.fn(async (input: string, init?: RequestInit) => {
      if (input === `${folder}/resources`) {
        return json({ items: [...resources, processItem] })
      }
      if (input === `${folder}/diagnostics`) {
        return json({ diagnostics: processDiagnostics })
      }
      if (input === processPath) {
        return json({ ...processItem, content: purchaseRequestApproval })
      }
      if (input === `${processPath}/references`) {
        return json({ uses: [], usedBy: [] })
      }
      return base(input, init)
    })
    vi.stubGlobal('fetch', fetchMock)
    return fetchMock
  }

  it('marks the step that a diagnostic of the open process names', async () => {
    stubProcess([
      {
        code: 'AXC0202',
        severity: 'error',
        message: 'The next step is unknown.',
        file: processItem.file,
        path: '/steps/1/next',
        resourceId: processItem.id,
      },
    ])
    renderPage(`/studio?app=PurchaseRequests&resource=${processItem.id}`)

    const marked = await screen.findByTestId('studio-graph-node-1', {}, slow)
    expect(marked).toHaveAccessibleName(/has problems$/)
    expect(marked).toHaveAttribute('data-marked', 'true')
    const unmarked = screen.getByTestId('studio-graph-node-0')
    expect(unmarked).not.toHaveAccessibleName(/has problems/)
    expect(unmarked).not.toHaveAttribute('data-marked')
  })

  it('marks no step for a diagnostic of another resource', async () => {
    stubProcess([
      {
        code: 'AXC0101',
        severity: 'error',
        message: 'The field type is unknown.',
        file: 'entities/purchase-request.json',
        path: '/steps/0',
        resourceId: entityId,
      },
    ])
    renderPage(`/studio?app=PurchaseRequests&resource=${processItem.id}`)

    await screen.findByTestId('studio-graph-node-0', {}, slow)
    expect(screen.getByTestId('studio-process-graph').querySelector('[data-marked]')).toBeNull()
  })
})

describe('StudioPage Packages', () => {
  const packageRoute = (id: string, path: string) => `${folder}/packages/${id}/files/${path}`
  const toolsManifest = JSON.stringify({ id: 'tools', language: 'node', label: 'Purchasing tools', version: '0.1.0' })

  /** Stubs `/api/me` with `me` and one package, `tools`, which `written` files add to once a write arrives. */
  function stubPackages(me: Record<string, unknown>) {
    const written: { path: string; ifNoneMatch: string | null }[] = []
    const fetchMock = stubServer()
    const base = fetchMock.getMockImplementation()!
    fetchMock.mockImplementation(async (input: string, init?: RequestInit) => {
      if (input === '/api/me') {
        return json({ id: 'author', studio: true, ...me })
      }
      if (input === `${folder}/package-files`) {
        const created = written.filter((write) => write.path.startsWith('new-tools/'))
        return json({
          items: [
            { package: 'tools', path: 'axis-package.json', version: 'v1' },
            { package: 'tools', path: 'index.js', version: 'v2' },
            ...created.map((write) => ({
              package: 'new-tools',
              path: write.path.slice('new-tools/'.length),
              version: 'v3',
            })),
          ],
        })
      }
      if (input === packageRoute('tools', 'axis-package.json') && init?.method === undefined) {
        return json({ package: 'tools', path: 'axis-package.json', version: 'v1', content: toolsManifest })
      }
      if (input.startsWith(`${folder}/packages/`) && init?.method === 'PUT') {
        const path = input.slice(`${folder}/packages/`.length).replace('/files/', '/')
        written.push({ path, ifNoneMatch: new Headers(init.headers).get('If-None-Match') })
        return json({ version: 'v3', diagnostics: [] })
      }
      if (input.startsWith(`${folder}/packages/new-tools/`)) {
        return json({ package: 'new-tools', path: 'axis-package.json', version: 'v3', content: '{}' })
      }
      return base(input, init)
    })
    return written
  }

  it('ends the Integration group of the sidebar with Packages, which opens the list', async () => {
    stubPackages({})
    renderPage('/studio?app=PurchaseRequests')

    const link = await within(sidebar!).findByRole('link', { name: 'Packages' })
    const links = within(sidebar!).getAllByRole('link')
    // The Integration group has no resource of its own in this application, so Packages is all of it.
    expect(links[links.indexOf(link) - 1]).toHaveTextContent('Policy matrix')
    expect(within(sidebar!).getByText('Integration')).toBeInTheDocument()

    await userEvent.click(link)

    expect(await screen.findByRole('heading', { level: 1, name: 'Packages' })).toBeInTheDocument()
    expect(screen.getByTestId('location')).toHaveTextContent('/studio?app=PurchaseRequests&view=packages')
    expect(await screen.findByTestId('studio-package-tools')).toHaveTextContent('Purchasing tools')
  })

  it('opens a package from its row, under the Packages entry, as a read-only workspace without studioDeveloper', async () => {
    stubPackages({})
    renderPage('/studio?app=PurchaseRequests&view=packages')

    await userEvent.click(await screen.findByTestId('studio-package-tools'))

    expect(await screen.findByRole('heading', { level: 1, name: 'Purchasing tools' })).toBeInTheDocument()
    expect(screen.getByTestId('location')).toHaveTextContent('/studio?app=PurchaseRequests&view=packages&package=tools')
    const tree = screen.getByTestId('studio-package-tree')
    expect(within(tree).getByText('axis-package.json', { exact: true })).toBeInTheDocument()
    expect(within(tree).getByText('index.js', { exact: true })).toBeInTheDocument()
    expect(screen.getByTestId('studio-package-read-only')).toBeInTheDocument()
    expect(within(sidebar!).getByRole('link', { name: 'Packages' }).closest('li')).toHaveClass('ant-menu-item-selected')
  })

  it.each([
    ['without studioDeveloper', {}],
    ['with studioDeveloper false', { studioDeveloper: false }],
  ])('shows no New package… %s', async (_name, me) => {
    stubPackages(me)
    renderPage('/studio?app=PurchaseRequests&view=packages')

    await screen.findByTestId('studio-package-tools')
    expect(screen.queryByRole('button', { name: 'New package…' })).not.toBeInTheDocument()
  })

  it('shows New package… when /api/me answers studioDeveloper, and a created package joins the list', async () => {
    const written = stubPackages({ studioDeveloper: true })
    renderPage('/studio?app=PurchaseRequests&view=packages')

    await screen.findByTestId('studio-package-tools')
    expect(screen.queryByTestId('studio-package-new-tools')).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'New package…' }))
    await userEvent.type(screen.getByRole('textbox', { name: 'ID' }), 'new-tools')
    await chooseOption(
      'TypeScript',
      screen.getByRole('combobox', { name: 'Language' }).closest('.ant-form-item') as HTMLElement,
    )
    await userEvent.click(screen.getByRole('button', { name: 'Create' }))

    expect(await screen.findByTestId('studio-package-new-tools')).toBeInTheDocument()
    expect(written[0].path).toBe('new-tools/axis-package.json')
    expect(written.every((write) => write.ifNoneMatch === '*')).toBe(true)
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })
})
