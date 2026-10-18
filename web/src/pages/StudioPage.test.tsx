import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import type { StudioDiagnostic, StudioResourceItem } from '../platform/studio'
import { StudioPage } from './StudioPage'

const entityId = 'd9ccbf89-5004-4d52-8885-2ee377b7caa5'
const formId = '5b6c7d8e-9f01-4a2b-8c3d-4e5f60718293'
const departmentId = '0192f4a7-3b1c-7e2d-9a4f-1c2d3e4f5a6b'

const resources: StudioResourceItem[] = [
  { id: departmentId, kind: 'entity', name: 'Department', file: 'entities/department.json', version: 'a' },
  { id: entityId, kind: 'entity', name: 'PurchaseRequest', file: 'entities/purchase-request.json', version: 'b' },
  { id: formId, kind: 'form', name: 'PurchaseRequestForm', file: 'forms/purchase-request.json', version: 'c' },
]

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

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

/** Stubs `fetch` with the three Studio list routes. `apps` is the status or list of `/api/studio/apps`. */
function stubServer(apps: { name: string }[] | number = [{ name: 'PurchaseRequests' }]) {
  const fetchMock = vi.fn(async (input: string, _init?: RequestInit) => {
    if (input === '/api/studio/apps') {
      return typeof apps === 'number' ? json({ title: 'No.' }, apps) : json({ items: apps })
    }
    if (input === '/api/studio/apps/PurchaseRequests/resources') {
      return json({ items: resources })
    }
    if (input === '/api/studio/apps/PurchaseRequests/diagnostics') {
      return json({ diagnostics })
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
    <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
      <MemoryRouter initialEntries={[entry]}>
        <StudioPage />
        <Location />
      </MemoryRouter>
    </TextProvider>,
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

  it('picks the only application by itself', async () => {
    const fetchMock = stubServer()
    renderPage('/studio')

    expect(await screen.findByTestId('studio-group-entity')).toBeInTheDocument()
    expect(screen.getByTestId('location')).toHaveTextContent('/studio?app=PurchaseRequests')
    expect(fetchMock).toHaveBeenCalledWith('/api/studio/apps/PurchaseRequests/resources', expect.anything())
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
})
