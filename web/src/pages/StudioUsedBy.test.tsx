import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import type { StudioReference, StudioReferenceNode, StudioResourceItem } from '../platform/studio'
import { ThemeModeProvider } from '../platform/theme'
import { TextProvider } from '../platform/TextProvider'
import { StudioUsedBy } from './StudioUsedBy'

const application = 'PurchaseRequests'
const folder = `/api/studio/apps/${application}`
const targetId = '5b6c7d8e-9f01-4a2b-8c3d-4e5f60718293'

const resources: StudioResourceItem[] = [
  {
    id: targetId,
    kind: 'entity',
    name: 'Target',
    file: 'entities/target.json',
    version: 'a',
    usedBy: 4,
  },
  {
    id: 'form-request',
    kind: 'form',
    name: 'PurchaseRequestForm',
    file: 'forms/purchase-request.json',
    version: 'b',
    usedBy: 0,
  },
  {
    id: 'entity-request',
    kind: 'entity',
    name: 'PurchaseRequest',
    file: 'entities/purchase-request.json',
    version: 'c',
    usedBy: 0,
    titles: { en: 'Purchase request' },
  },
  {
    id: 'entity-department',
    kind: 'entity',
    name: 'Department',
    file: 'entities/department.json',
    version: 'd',
    usedBy: 0,
  },
  {
    id: 'page-totals',
    kind: 'page',
    name: 'RequestsByDepartment',
    file: 'pages/requests-by-department.json',
    version: 'e',
    usedBy: 0,
    titles: { en: 'Purchase request totals by department' },
  },
]

const node = (id: string): StudioReferenceNode => {
  const item = resources.find((resource) => resource.id === id)!
  return { type: 'resource', id, kind: item.kind, name: item.name, file: item.file }
}

const entry = (relation: string, path: string, id: string): StudioReference => ({
  relation,
  file: `${id}.json`,
  path,
  node: node(id),
})

const usedBy: StudioReference[] = [
  entry('task form', '/steps/1/form', 'form-request'),
  entry('widget form', '/widgets/0/form', 'page-totals'),
  entry('widget form', '/widgets/2/form', 'page-totals'),
  entry('field target', '/fields/3/target', 'entity-request'),
  entry('field target', '/fields/7/target', 'entity-request'),
  entry('field target', '/fields/1/target', 'entity-department'),
]

function stubReferences(answer: { status?: number; usedBy?: StudioReference[] } = { usedBy }) {
  const fetchMock = vi.fn(async (input: string) =>
    input === `${folder}/resources/${targetId}/references`
      ? answer.status
        ? new Response(null, { status: answer.status })
        : new Response(JSON.stringify({ uses: [], usedBy: answer.usedBy ?? [] }), {
            headers: { 'Content-Type': 'application/json' },
          })
      : new Response(null, { status: 404 }),
  )
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function renderTab() {
  render(
    <MemoryRouter>
      <ThemeModeProvider>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <StudioUsedBy application={application} resourceId={targetId} resources={resources} />
        </TextProvider>
      </ThemeModeProvider>
    </MemoryRouter>,
  )
}

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('StudioUsedBy', () => {
  it('names each resource by its label, as a link that opens it at the path of its reference', async () => {
    stubReferences()
    renderTab()

    const table = await screen.findByTestId('studio-used-by')
    const link = within(table).getByRole('link', { name: 'Purchase request' })
    expect(link).toHaveAttribute(
      'href',
      `/studio?app=${application}&resource=entity-request&path=%2Ffields%2F3%2Ftarget`,
    )
    expect(within(table).getByRole('link', { name: 'Department' })).toBeInTheDocument()
  })

  it('shows the kind and the use of each resource in words, with no file, path or relation code', async () => {
    stubReferences()
    renderTab()

    const table = await screen.findByTestId('studio-used-by')
    const row = within(table).getByRole('link', { name: 'Purchase request' }).closest('tr')!
    expect(within(row).getByText('Entity')).toBeInTheDocument()
    expect(within(row).getByText('A field refers to it')).toBeInTheDocument()
    expect(table).not.toHaveTextContent('field target')
    expect(table).not.toHaveTextContent('/fields/')
    expect(table).not.toHaveTextContent('.json')
  })

  it('sorts by sidebar kind and then label, and shows a repeated resource and relation once', async () => {
    stubReferences()
    renderTab()

    const table = await screen.findByTestId('studio-used-by')
    const links = within(table).getAllByRole('link')
    expect(links.map((link) => link.textContent)).toEqual([
      'Department',
      'Purchase request',
      'Purchase request totals by department',
      'PurchaseRequestForm',
    ])
    expect(links[1]).toHaveAttribute('href', expect.stringContaining('%2Ffields%2F3%2Ftarget'))
  })

  it('sorts the rows by the Name header, and by the Kind header in the order of the sidebar', async () => {
    stubReferences()
    renderTab()

    const table = await screen.findByTestId('studio-used-by')
    const labels = () =>
      within(table)
        .getAllByRole('link')
        .map((link) => link.textContent)
    await userEvent.click(within(table).getByRole('columnheader', { name: 'Name' }))
    await userEvent.click(within(table).getByRole('columnheader', { name: 'Name' }))
    expect(labels()).toEqual([
      'PurchaseRequestForm',
      'Purchase request totals by department',
      'Purchase request',
      'Department',
    ])

    await userEvent.click(within(table).getByRole('columnheader', { name: 'Kind' }))
    expect(labels()[0]).toBe('Department')
    await userEvent.click(within(table).getByRole('columnheader', { name: 'Kind' }))
    expect(labels()[0]).toBe('PurchaseRequestForm')
  })

  it('narrows the rows by search, which applies on Enter, and by kind', async () => {
    stubReferences()
    renderTab()

    const table = await screen.findByTestId('studio-used-by')
    await userEvent.type(screen.getByRole('searchbox', { name: 'Search' }), 'totals')
    expect(within(table).getAllByRole('link')).toHaveLength(4)
    await userEvent.type(screen.getByRole('searchbox', { name: 'Search' }), '{Enter}')
    expect(
      within(table)
        .getAllByRole('link')
        .map((link) => link.textContent),
    ).toEqual(['Purchase request totals by department'])

    await userEvent.clear(screen.getByRole('searchbox', { name: 'Search' }))
    await userEvent.type(screen.getByRole('searchbox', { name: 'Search' }), '{Enter}')
    await userEvent.click(screen.getByRole('combobox', { name: 'Kind' }))
    await userEvent.click(await screen.findByRole('option', { name: 'Form' }))
    expect(
      within(table)
        .getAllByRole('link')
        .map((link) => link.textContent),
    ).toEqual(['PurchaseRequestForm'])
  })

  it('says that nothing uses the resource when no resource uses it', async () => {
    stubReferences({ usedBy: [] })
    renderTab()

    expect(await screen.findByText('Nothing uses this resource.')).toBeInTheDocument()
  })

  it('shows a load error when the references do not load', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubReferences({ status: 500 })
    renderTab()

    expect(await screen.findByText('The references could not be loaded.')).toBeInTheDocument()
    warn.mockRestore()
  })
})
