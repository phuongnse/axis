import { EditorView } from '@codemirror/view'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import purchaseRequestApproval from '../../../samples/apps/purchase-requests/processes/purchase-request-approval.json'
import { assignLocation } from '../platform/navigation'
import { ThemeModeProvider } from '../platform/theme'
import { TextProvider } from '../platform/TextProvider'
import type { StudioDiagnostic, StudioReference, StudioResourceItem } from '../platform/studio'
import { segmentLabel } from '../test/antd'
import { StudioPage } from './StudioPage'

vi.mock('../platform/navigation', () => ({ assignLocation: vi.fn() }))

const entityId = 'd9ccbf89-5004-4d52-8885-2ee377b7caa5'
const formId = '5b6c7d8e-9f01-4a2b-8c3d-4e5f60718293'
const departmentId = '0192f4a7-3b1c-7e2d-9a4f-1c2d3e4f5a6b'
const manifestId = 'f0e1d2c3-b4a5-4968-8776-655443322110'
const folder = '/api/studio/apps/PurchaseRequests'

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

/** What the text key `purchaseRequest.label` is used by, as its text key route answers. */
const labelUsers: { usedBy: StudioReference[] } = {
  usedBy: [
    {
      relation: 'label text',
      file: 'entities/purchase-request.json',
      path: '/label/textKey',
      node: {
        type: 'resource',
        id: entityId,
        kind: 'entity',
        name: 'PurchaseRequest',
        file: 'entities/purchase-request.json',
      },
    },
  ],
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
  texts: [{ key: 'entity.purchaseRequest', texts: { en: 'Purchase request' }, usedBy: 1 }],
}

/** While `down` is set, the entity routes, the names and `/api/me` fail: with no answer when `status` is null, else with `status`. */
interface Outage {
  down: { status: number | null } | null
}

/** What the expression stubs answer: the entity's content, and the answer of each check in order. */
interface ExpressionStub {
  content?: Record<string, unknown>
  check?: (call: number) => Response
}

/** Stubs `fetch` with the Studio list routes, the read, patch and delete of the purchase request entity, and its references. `apps` is the status or list of `/api/studio/apps`. */
function stubServer(
  apps: { name: string }[] | number = [{ name: 'PurchaseRequests' }],
  patch: PatchAnswer = { status: 200, body: { version: 'b2', diagnostics: [savedDiagnostic] } },
  outage: Outage = { down: null },
  expressions: ExpressionStub = {},
) {
  let checkCalls = 0
  const entityPath = `${folder}/resources/${entityId}`
  const manifestPath = `${folder}/resources/${manifestId}`
  const fetchMock = vi.fn(async (input: string, init?: RequestInit) => {
    if ((input === entityPath || input === namesPath || input === '/api/me') && outage.down) {
      if (outage.down.status === null) {
        throw new TypeError('Failed to fetch')
      }
      return new Response(null, { status: outage.down.status })
    }
    if (input === '/api/me') {
      return json({ id: 'hoa', studio: true })
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
      return json({ ...resources[3], content: { id: manifestId, kind: 'application', name: 'PurchaseRequests' } })
    }
    if (input === namesPath) {
      return json(applicationNames)
    }
    if (input === '/api/studio/apps') {
      return typeof apps === 'number' ? json({ title: 'No.' }, apps) : json({ items: apps })
    }
    if (input === `${folder}/resources`) {
      return json({ items: resources })
    }
    if (input === `${folder}/releases`) {
      return json({ active: null, folderContentHash: null, items: [] })
    }
    if (input === `${folder}/diagnostics`) {
      return json({ diagnostics })
    }
    if (input === `${folder}/names`) {
      return json(applicationNames)
    }
    if (input === `${folder}/policy-matrix`) {
      return json({ targets: [], roles: [] })
    }
    if (input === `${folder}/expressions/check` && expressions.check) {
      checkCalls++
      return expressions.check(checkCalls)
    }
    if (input === `${folder}/texts/purchaseRequest.label/references`) {
      return json(labelUsers)
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

function renderPage(entry = '/studio?app=PurchaseRequests') {
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <MemoryRouter initialEntries={[entry]}>
          <StudioPage />
          <Location />
        </MemoryRouter>
      </TextProvider>
    </ThemeModeProvider>,
  )
}

afterEach(() => vi.unstubAllGlobals())

describe('StudioPage', () => {
  it('lists the resources in one group per kind', async () => {
    stubServer()
    renderPage()

    const entities = await screen.findByTestId('studio-group-entity')
    expect(within(entities).getByText('entity')).toBeInTheDocument()
    expect(
      within(entities)
        .getAllByRole('button')
        .map((button) => button.textContent),
    ).toEqual(['Department', 'PurchaseRequest'])
    const forms = screen.getByTestId('studio-group-form')
    expect(
      within(forms)
        .getAllByRole('button')
        .map((button) => button.textContent),
    ).toEqual(['PurchaseRequestForm'])
    expect(screen.getByText('Select a resource.')).toBeInTheDocument()
  })

  it('filters the resources by part of a name in any letter case', async () => {
    stubServer()
    renderPage()
    await screen.findByTestId('studio-group-entity')

    await userEvent.type(screen.getByRole('searchbox', { name: 'Search by name or id' }), 'purchaserEQ')

    expect(screen.getByTestId(`studio-resource-${entityId}`)).toBeInTheDocument()
    expect(screen.getByTestId(`studio-resource-${formId}`)).toBeInTheDocument()
    expect(screen.queryByTestId(`studio-resource-${departmentId}`)).not.toBeInTheDocument()
  })

  it('filters the resources by part of an id in any letter case', async () => {
    stubServer()
    renderPage()
    await screen.findByTestId('studio-group-entity')

    await userEvent.type(screen.getByRole('searchbox', { name: 'Search by name or id' }), '5B6C7D8E')

    expect(screen.getByTestId(`studio-resource-${formId}`)).toBeInTheDocument()
    expect(screen.queryByTestId(`studio-resource-${entityId}`)).not.toBeInTheDocument()
    expect(screen.queryByTestId('studio-group-entity')).not.toBeInTheDocument()
  })

  it('shows the details of a resource and keeps it in the address', async () => {
    stubServer()
    renderPage()

    await userEvent.click(await screen.findByTestId(`studio-resource-${entityId}`))

    const details = screen.getByTestId('studio-resource-details')
    expect(details).toHaveTextContent('PurchaseRequest')
    expect(details).toHaveTextContent('entity')
    expect(details).toHaveTextContent(entityId)
    expect(details).toHaveTextContent('entities/purchase-request.json')
    expect(screen.getByTestId('location')).toHaveTextContent(`/studio?app=PurchaseRequests&resource=${entityId}`)
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

    const name = await screen.findByTestId('studio-field-/fields/0/name')
    expect(within(name).getByRole('textbox')).toHaveValue('number')
    expect(within(screen.getByTestId('studio-field-/fields/1/maxLength')).getByRole('spinbutton')).toHaveValue('200')
    const label = screen.getByTestId('studio-field-/label')
    expect(within(label).getByRole('textbox', { name: 'Text key' })).toHaveValue('entity.purchaseRequest')
    expect(within(label).getByRole('textbox', { name: 'en' })).toHaveValue('Purchase request')
    expect(screen.getByTestId('studio-entity-designer')).toBeInTheDocument()
  })

  it('sends only the changed value, with the loaded version', async () => {
    const fetchMock = stubServer()
    renderPage(`/studio?app=PurchaseRequests&resource=${entityId}`)
    const save = await screen.findByRole('button', { name: 'Save' })
    expect(save).toBeDisabled()

    const maxLength = within(screen.getByTestId('studio-field-/fields/1/maxLength')).getByRole('spinbutton')
    await userEvent.clear(maxLength)
    await userEvent.type(maxLength, '250')
    expect(save).toBeEnabled()
    await userEvent.click(save)

    await screen.findByTestId('studio-saved')
    const patches = fetchMock.mock.calls.filter(([, init]) => init?.method === 'PATCH')
    expect(patches).toHaveLength(1)
    const [, init] = patches[0]
    expect(JSON.parse(init!.body as string)).toEqual([{ op: 'replace', path: '/fields/1/maxLength', value: 250 }])
    const headers = new Headers(init!.headers)
    expect(headers.get('If-Match')).toBe('"b"')
    expect(headers.get('Content-Type')).toBe('application/json-patch+json')
    expect(save).toBeDisabled()
  })

  it('shows the diagnostics of the save answer in place of the loaded ones', async () => {
    stubServer()
    renderPage(`/studio?app=PurchaseRequests&resource=${entityId}`)
    const panel = await screen.findByTestId('studio-diagnostics')
    expect(within(panel).getByText('AXC0101')).toBeInTheDocument()

    const maxLength = within(await screen.findByTestId('studio-field-/fields/1/maxLength')).getByRole('spinbutton')
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
    const maxLength = within(await screen.findByTestId('studio-field-/fields/1/maxLength')).getByRole('spinbutton')
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
    expect(within(screen.getByTestId('studio-field-/fields/1/maxLength')).getByRole('spinbutton')).toHaveValue('200')
  })

  it('shows an edit in the JSON view in the form, and turns Save off for invalid JSON', async () => {
    stubServer()
    renderPage(`/studio?app=PurchaseRequests&resource=${entityId}`)
    await screen.findByTestId('studio-field-/fields/0/name')
    await userEvent.click(screen.getByRole('tab', { name: 'JSON' }))
    const editor = screen.getByRole('textbox', { name: 'JSON' })

    fireEvent.change(editor, { target: { value: JSON.stringify({ ...entityContent(), name: 'Renamed' }) } })
    await userEvent.click(screen.getByRole('tab', { name: 'Form' }))
    expect(within(screen.getByTestId('studio-field-/name')).getByRole('textbox')).toHaveValue('Renamed')
    expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled()

    await userEvent.click(screen.getByRole('tab', { name: 'JSON' }))
    fireEvent.change(screen.getByRole('textbox', { name: 'JSON' }), { target: { value: '{ "name": ' } })

    expect(screen.getByTestId('studio-json-error')).toHaveTextContent('The JSON is not valid.')
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled()
  })

  it('shows each diagnostic with its file and path', async () => {
    stubServer()
    renderPage()

    const panel = await screen.findByTestId('studio-diagnostics')
    expect(within(panel).getByText('AXC0101')).toBeInTheDocument()
    expect(within(panel).getByText('The field type is unknown.')).toBeInTheDocument()
    expect(within(panel).getByText(/entities\/purchase-request\.json · Path: \/fields\/0\/type/)).toBeInTheDocument()
    expect(within(panel).getByText(/application\.json · Path: \/description/)).toBeInTheDocument()
  })

  it('selects the resource of a clicked diagnostic, clears the search and shows its file', async () => {
    stubServer()
    renderPage()
    await screen.findByTestId('studio-group-entity')
    await userEvent.type(screen.getByRole('searchbox', { name: 'Search by name or id' }), 'Department')
    expect(screen.queryByTestId(`studio-resource-${entityId}`)).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: /AXC0101/ }))

    expect(screen.getByTestId('studio-resource-details')).toHaveTextContent('entities/purchase-request.json')
    expect(screen.getByRole('searchbox', { name: 'Search by name or id' })).toHaveValue('')
    expect(screen.getByTestId(`studio-resource-${entityId}`)).toBeInTheDocument()
    expect(screen.getByTestId('location')).toHaveTextContent(`resource=${entityId}`)
  })

  it('does not make a diagnostic without a resource clickable', async () => {
    stubServer()
    renderPage()

    await screen.findByText('The application has no description.')

    expect(screen.queryByRole('button', { name: /AXC0001/ })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: /AXC0101/ })).toBeInTheDocument()
  })

  it('opens the resource of a clicked diagnostic at its path and scrolls to that field', async () => {
    const scrollIntoView = vi.fn()
    Element.prototype.scrollIntoView = scrollIntoView
    try {
      stubServer()
      renderPage()

      await userEvent.click(await screen.findByRole('button', { name: /AXC0101/ }))

      const field = await screen.findByTestId('studio-field-/fields/0/type')
      await waitFor(() => expect(scrollIntoView).toHaveBeenCalledWith({ block: 'center' }))
      expect(scrollIntoView.mock.contexts).toEqual([field])
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

      await userEvent.click(await screen.findByRole('button', { name: /AXC0102/ }))

      await screen.findByTestId('studio-field-/fields/0/name')
      expect(screen.getByTestId('location')).toHaveTextContent(/resource=[^&]+$/)
      expect(screen.getByRole('tab', { name: 'Form' })).toHaveAttribute('aria-selected', 'true')
      expect(scrollIntoView).not.toHaveBeenCalled()
    } finally {
      delete (Element.prototype as Partial<Element>).scrollIntoView
    }
  })

  it('picks the only application by itself', async () => {
    const fetchMock = stubServer()
    renderPage('/studio')

    expect(await screen.findByTestId('studio-group-entity')).toBeInTheDocument()
    expect(screen.getByTestId('location')).toHaveTextContent('/studio?app=PurchaseRequests')
    expect(fetchMock).toHaveBeenCalledWith('/api/studio/apps/PurchaseRequests/resources', expect.anything())
  })

  it('switches between the resources and the policy matrix, kept in the address', async () => {
    const fetchMock = stubServer()
    renderPage()

    expect(await screen.findByTestId('studio-group-entity')).toBeInTheDocument()
    expect(fetchMock).not.toHaveBeenCalledWith(`${folder}/policy-matrix`, expect.anything())

    await userEvent.click(screen.getByText('Policy matrix'))

    expect(await screen.findByTestId('studio-matrix')).toBeInTheDocument()
    expect(screen.getByTestId('location')).toHaveTextContent('/studio?app=PurchaseRequests&view=policies')
    expect(fetchMock).toHaveBeenCalledWith(`${folder}/policy-matrix`, expect.anything())
    expect(screen.queryByTestId('studio-group-entity')).not.toBeInTheDocument()
    expect(screen.getByTestId('studio-diagnostics')).toBeInTheDocument()

    await userEvent.click(segmentLabel('Resources'))

    expect(await screen.findByTestId('studio-group-entity')).toBeInTheDocument()
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
    expect(screen.getByTestId('studio-group-entity')).toBeInTheDocument()
    expect(warn).toHaveBeenCalled()
    warn.mockRestore()
  })

  it('waits for the person to pick when there are several applications', async () => {
    stubServer([{ name: 'PurchaseRequests' }, { name: 'LeaveRequests' }])
    renderPage('/studio')

    expect(await screen.findByRole('combobox', { name: 'Application' })).toBeInTheDocument()
    expect(screen.getByTestId('location')).toHaveTextContent(/^\/studio$/)
    expect(screen.queryByTestId('studio-diagnostics')).not.toBeInTheDocument()
  })

  it('shows the forbidden page when the server answers 403', async () => {
    stubServer(403)
    renderPage('/studio')

    const forbidden = await screen.findByTestId('studio-forbidden')
    expect(forbidden).toHaveTextContent('Studio is not available')
    expect(forbidden).toHaveTextContent('You need the author role to use Studio.')
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

  it('shows a load error when the folder cannot be loaded', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubServer()
    renderPage('/studio?app=Unknown')

    expect(await screen.findByTestId('studio-load-error')).toBeInTheDocument()
    warn.mockRestore()
  })

  it('lists what a resource uses and what uses it, with the relation, resource, file and path of each entry', async () => {
    stubServer()
    renderPage(`/studio?app=PurchaseRequests&resource=${entityId}`)

    const uses = await screen.findByTestId('studio-uses')
    const [department, label] = within(uses).getAllByTestId('studio-reference')
    expect(department).toHaveTextContent('field target')
    expect(department).toHaveTextContent('Department')
    expect(department).toHaveTextContent('entities/purchase-request.json · Path: /fields/3/target')
    expect(label).toHaveTextContent('label text')
    expect(label).toHaveTextContent('purchaseRequest.label')
    expect(within(screen.getByTestId('studio-used-by')).getByText('None.')).toBeInTheDocument()
  })

  it('opens a Uses entry at its top and a Used by entry at the path of its reference', async () => {
    stubServer()
    renderPage(`/studio?app=PurchaseRequests&resource=${entityId}`)
    await userEvent.click(within(await screen.findByTestId('studio-uses')).getByRole('button', { name: 'Department' }))

    expect(screen.getByTestId('studio-resource-details')).toHaveTextContent('entities/department.json')
    expect(screen.getByTestId('location')).toHaveTextContent(/^\/studio\?app=PurchaseRequests&resource=[^&]+$/)

    const usedBy = await screen.findByTestId('studio-used-by')
    await userEvent.click(await within(usedBy).findByRole('button', { name: 'PurchaseRequest' }))

    expect(screen.getByTestId('studio-resource-details')).toHaveTextContent('entities/purchase-request.json')
    expect(screen.getByTestId('studio-resource-details')).toHaveTextContent('/fields/3/target')
    expect(screen.getByTestId('location')).toHaveTextContent(`resource=${entityId}&path=%2Ffields%2F3%2Ftarget`)
  })

  it('scrolls the form field of the path in the address into view', async () => {
    const scrollIntoView = vi.fn()
    Element.prototype.scrollIntoView = scrollIntoView
    try {
      stubServer()
      renderPage(`/studio?app=PurchaseRequests&resource=${entityId}&path=%2Ffields%2F1%2FmaxLength`)

      const field = await screen.findByTestId('studio-field-/fields/1/maxLength')
      await waitFor(() => expect(scrollIntoView).toHaveBeenCalledWith({ block: 'center' }))
      expect(scrollIntoView.mock.contexts).toEqual([field])
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

    await waitFor(() => expect(screen.getByRole('tab', { name: 'JSON' })).toHaveAttribute('aria-selected', 'true'))
    expect(screen.getByRole('textbox', { name: 'JSON' })).toHaveValue(JSON.stringify(textContent, null, 2))
  })

  it('keeps the last expression result and Save enabled when a check gets no answer', async () => {
    const fetchMock = stubServer(undefined, undefined, undefined, {
      content: {
        ...entityContent(),
        validations: [{ expression: 'totalAmount > 0', message: { textKey: 'validation.total' }, field: 'title' }],
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
    await waitFor(() => expect(within(field).getByTestId('studio-expression-type')).toHaveTextContent('boolean'))

    const view = EditorView.findFromDOM(within(field).getByRole('textbox'))!
    view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: 'totalAmount > 1' } })
    await waitFor(() =>
      expect(fetchMock.mock.calls.filter(([input]) => input === `${folder}/expressions/check`)).toHaveLength(2),
    )

    expect(within(field).getByTestId('studio-expression-type')).toHaveTextContent('boolean')
    expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled()
    expect(screen.queryByTestId('studio-reconnecting')).not.toBeInTheDocument()
  })

  it('shows the form again for a path it can show after a path it cannot show', async () => {
    const scrollIntoView = vi.fn()
    Element.prototype.scrollIntoView = scrollIntoView
    try {
      stubServer()
      renderPage(`/studio?app=PurchaseRequests&resource=${entityId}&path=%2Funknown`)
      await waitFor(() => expect(screen.getByRole('tab', { name: 'JSON' })).toHaveAttribute('aria-selected', 'true'))

      await userEvent.click(await screen.findByRole('button', { name: /AXC0101/ }))

      const field = await screen.findByTestId('studio-field-/fields/0/type')
      await waitFor(() => expect(scrollIntoView).toHaveBeenCalledWith({ block: 'center' }))
      expect(scrollIntoView.mock.contexts).toEqual([field])
      await waitFor(() => expect(screen.getByRole('tab', { name: 'Form' })).toHaveAttribute('aria-selected', 'true'))
    } finally {
      delete (Element.prototype as Partial<Element>).scrollIntoView
    }
  })

  it('loads an entry once when it is first expanded, and a text key entry loads its text key route', async () => {
    const fetchMock = stubServer()
    renderPage(`/studio?app=PurchaseRequests&resource=${entityId}`)
    const uses = await screen.findByTestId('studio-uses')
    const departmentRequests = () =>
      fetchMock.mock.calls.filter(
        ([input]) => input === `/api/studio/apps/PurchaseRequests/resources/${departmentId}/references`,
      )
    const department = within(uses).getByRole('button', { name: 'Department' }).closest('li')!

    await userEvent.click(within(department).getByRole('button', { name: 'Expand' }))
    expect(await within(department).findByRole('button', { name: 'PurchaseRequest' })).toBeInTheDocument()
    await userEvent.click(within(department).getByRole('button', { name: 'Collapse' }))
    await userEvent.click(within(department).getByRole('button', { name: 'Expand' }))

    expect(within(department).getByRole('button', { name: 'PurchaseRequest' })).toBeInTheDocument()
    expect(departmentRequests()).toHaveLength(1)

    const label = within(uses).getByText('purchaseRequest.label').closest('li')!
    await userEvent.click(within(label).getByRole('button', { name: 'Expand' }))

    expect(await within(label).findByRole('button', { name: 'PurchaseRequest' })).toBeInTheDocument()
    expect(within(label).queryByText('Uses')).not.toBeInTheDocument()
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/studio/apps/PurchaseRequests/texts/purchaseRequest.label/references',
      expect.anything(),
    )
  })

  it('warns with the number of references before a delete, and deletes after confirming', async () => {
    const fetchMock = stubServer()
    renderPage(`/studio?app=PurchaseRequests&resource=${entityId}`)
    await userEvent.click(await screen.findByRole('button', { name: 'Delete' }))

    const dialog = await screen.findByRole('dialog')
    expect(within(within(dialog).getByTestId('studio-delete-warning')).getByText('3')).toBeInTheDocument()
    await userEvent.click(within(dialog).getByRole('button', { name: 'Delete' }))

    await waitFor(() => expect(screen.queryByTestId(`studio-resource-${entityId}`)).not.toBeInTheDocument())
    const deletes = fetchMock.mock.calls.filter(([, init]) => init?.method === 'DELETE')
    expect(deletes).toHaveLength(1)
    const [, init] = deletes[0]
    expect(new Headers(init!.headers).get('If-Match')).toBe('"b"')
    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent(/^\/studio\?app=PurchaseRequests$/))
  })

  it('loads the names again after a save and after a delete', async () => {
    const fetchMock = stubServer()
    renderPage(`/studio?app=PurchaseRequests&resource=${entityId}`)
    const maxLength = within(await screen.findByTestId('studio-field-/fields/1/maxLength')).getByRole('spinbutton')
    const loads = () => fetchMock.mock.calls.filter(([input]) => input === namesPath).length
    await waitFor(() => expect(loads()).toBe(1))

    await userEvent.clear(maxLength)
    await userEvent.type(maxLength, '250')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await screen.findByTestId('studio-saved')
    await waitFor(() => expect(loads()).toBe(2))

    await userEvent.click(screen.getByRole('button', { name: 'Delete' }))
    await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Delete' }))
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

    await screen.findByRole('button', { name: 'Save' })
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

    expect(await screen.findByTestId('studio-field-/fields/0/name', {}, { timeout: 3000 })).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByTestId('studio-reconnecting')).not.toBeInTheDocument())
    const reads = (path: string) => fetchMock.mock.calls.filter(([input, init]) => input === path && !init?.method)
    expect(reads(`/api/studio/apps/PurchaseRequests/resources/${entityId}`)).toHaveLength(2)
    expect(reads('/api/studio/apps/PurchaseRequests/diagnostics')).toHaveLength(2)
  })

  it('keeps an unsaved draft through a reconnect, and saves it with the same patch', async () => {
    const outage: Outage = { down: null }
    const fetchMock = stubServer(undefined, undefined, outage)
    renderPage(`/studio?app=PurchaseRequests&resource=${entityId}`)
    const maxLength = within(await screen.findByTestId('studio-field-/fields/1/maxLength')).getByRole('spinbutton')
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

  it('starts sign-in when the reload after a reconnect answers 401', async () => {
    vi.mocked(assignLocation).mockClear()
    const outage: Outage = { down: null }
    stubServer(undefined, undefined, outage)
    renderPage(`/studio?app=PurchaseRequests&resource=${entityId}`)
    const maxLength = within(await screen.findByTestId('studio-field-/fields/1/maxLength')).getByRole('spinbutton')
    await userEvent.clear(maxLength)
    await userEvent.type(maxLength, '250')

    outage.down = { status: null }
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await screen.findByTestId('studio-reconnecting', {}, { timeout: 3000 })
    outage.down = { status: 401 }

    await waitFor(
      () => expect(assignLocation).toHaveBeenCalledWith(expect.stringMatching(/^\/api\/auth\/sign-in\?returnUrl=/)),
      { timeout: 3000 },
    )
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

  /** `stubServer` where `/api/me` answers `me`, and the request routes answer in turn with `statuses`. */
  function stubPublishing(me: () => Response, statuses: unknown[] = []) {
    const fetchMock = stubServer()
    const base = fetchMock.getMockImplementation()!
    const answers = [...statuses]
    fetchMock.mockImplementation(async (input: string, init?: RequestInit) => {
      if (input === '/api/me') {
        return me()
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
        const row = (id: string, number: number, activatedAt: string) => ({
          releaseId: id,
          number,
          contentHash: `hash-${number}`,
          activatedAt,
          actor: 'hoa',
          actorName: null,
          label: null,
          note: null,
          requestId: null,
        })
        return json({
          active: { releaseId, number: 3, contentHash: 'hash-3' },
          folderContentHash: 'hash-3',
          items: [row(releaseId, 3, '2026-05-02T10:00:00Z'), row('older', 2, '2026-05-01T10:00:00Z')],
        })
      }
      return base(input, init)
    })
    return fetchMock
  }

  const publisher = () => json({ id: 'hoa', studio: true, studioPublisher: true, studioPreviewHost: null })
  const previewer =
    (studioPublisher = true) =>
    () =>
      json({ id: 'hoa', studio: true, studioPublisher, studioPreviewHost: 'preview.example.test' })

  afterEach(() => vi.useRealTimers())

  it.each([
    ['studioPublisher is true', publisher, true],
    ['studioPublisher is false', () => json({ id: 'hoa', studio: true, studioPublisher: false }), false],
    ['GET /api/me fails', () => new Response(null, { status: 500 }), false],
  ])('shows Publish only when it may: %s', async (_name, me, shown) => {
    stubPublishing(me)
    renderPage()

    expect(await screen.findByRole('button', { name: 'Validate' })).toBeEnabled()
    await waitFor(() => expect(screen.queryAllByRole('button', { name: 'Publish' })).toHaveLength(shown ? 1 : 0))
  })

  it('shows the status while a publish runs and the release number when it succeeds', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const fetchMock = stubPublishing(publisher, [
      request('running'),
      request('succeeded', { releaseId, diagnostics: [published] }),
    ])
    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: 'Publish' }))
    await userEvent.type(screen.getByRole('textbox', { name: 'Label (optional)' }), 'May')
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Publish' }))

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
    expect(screen.getByTestId('studio-request-status')).toHaveAttribute('data-status', 'running')

    await vi.advanceTimersByTimeAsync(1000)
    await waitFor(() => expect(screen.getByTestId('studio-request-status')).toHaveAttribute('data-status', 'succeeded'))
    expect(screen.getByTestId('studio-release-number')).toHaveTextContent('3')
    const panel = screen.getByTestId('studio-diagnostics')
    expect(within(panel).getByText('AXC0900')).toBeInTheDocument()
    expect(within(panel).queryByText('AXC0101')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Validate' })).toBeEnabled()
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
    expect(within(screen.getByTestId('studio-diagnostics')).getByText('AXC0777')).toBeInTheDocument()
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
    expect(within(screen.getByTestId('studio-diagnostics')).getByText('AXC0777')).toBeInTheDocument()
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

  describe('New application', () => {
    function stubCreateAccess(studioCreate: boolean, apps: { name: string }[] = [{ name: 'PurchaseRequests' }]) {
      const fetchMock = stubServer(apps)
      const base = fetchMock.getMockImplementation()!
      fetchMock.mockImplementation(async (input: string, init?: RequestInit) =>
        input === '/api/me' ? json({ id: 'hoa', studio: true, studioCreate }) : base(input, init),
      )
      return fetchMock
    }

    it.each([
      ['studioCreate is true', true, true],
      ['studioCreate is false', false, false],
    ])('shows New application only when it may create: %s', async (_name, studioCreate, shown) => {
      stubCreateAccess(studioCreate)
      renderPage()

      expect(await screen.findByTestId(`studio-resource-${entityId}`)).toBeInTheDocument()
      await waitFor(() =>
        expect(screen.queryAllByRole('button', { name: 'New application' })).toHaveLength(shown ? 1 : 0),
      )
    })

    it('shows New application when Studio lists no application', async () => {
      stubCreateAccess(true, [])
      renderPage()

      expect(await screen.findByTestId('studio-no-applications')).toBeInTheDocument()
      expect(await screen.findByRole('button', { name: 'New application' })).toBeInTheDocument()
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
      await userEvent.click(await screen.findByRole('button', { name: 'New resource' }))
      await userEvent.click(screen.getByRole('combobox', { name: 'Kind' }))
      await userEvent.click(await screen.findByTitle(kind))
      await userEvent.type(screen.getByRole('textbox', { name: valueLabel }), value)
      await userEvent.click(screen.getByRole('button', { name: 'Create' }))
    }

    it('narrows the kinds to what is typed', async () => {
      stubCreate(201)
      renderPage()

      await userEvent.click(await screen.findByRole('button', { name: 'New resource' }))
      await userEvent.type(screen.getByRole('combobox', { name: 'Kind' }), 'proc')

      expect(screen.getAllByRole('option').map((option) => option.textContent)).toEqual(['process'])
      await userEvent.click(screen.getByRole('option', { name: 'process' }))
      const create = screen.getByRole('button', { name: 'Create' })
      expect(create).toBeDisabled()
      await userEvent.type(screen.getByRole('textbox', { name: 'Name' }), 'Approval')
      expect(create).toBeEnabled()
    })

    it('creates a text resource that copies the texts of the default locale of the first site', async () => {
      const fetchMock = stubCreate(201)
      renderPage()

      await fillDialog('text', 'de', 'Locale')

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
      expect(screen.getByTestId(`studio-resource-${newId}`)).toBeInTheDocument()
      expect(within(screen.getByTestId('studio-diagnostics')).getByText('AXC0777')).toBeInTheDocument()
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

      await fillDialog('text', 'vi', 'Locale')

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

      await fillDialog('entity', 'CostCentre', 'Name')

      expect(await screen.findByText('A resource with this name or locale already exists.')).toBeInTheDocument()
      expect(screen.getByRole('textbox', { name: 'Name' })).toBeInTheDocument()
      expect(screen.getByTestId('location')).not.toHaveTextContent('resource=')
    })

    it('shows a refused name under the field', async () => {
      stubCreate(400)
      renderPage()

      await fillDialog('entity', 'Cost Centre', 'Name')

      expect(await screen.findByText(/This name or locale is not allowed/)).toBeInTheDocument()
    })
  })
})

describe('StudioPage process graph marks', () => {
  const slow = { timeout: 5000 }
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
    expect(marked).toHaveAccessibleName(/has diagnostics$/)
    expect(marked).toHaveAttribute('data-marked', 'true')
    const unmarked = screen.getByTestId('studio-graph-node-0')
    expect(unmarked).not.toHaveAccessibleName(/has diagnostics/)
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
