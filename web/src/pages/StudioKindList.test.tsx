import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import type { StudioKindListItem } from '../platform/studio'
import { studioResourceAddress } from '../platform/studioAddress'
import { StudioConnectionContext } from '../platform/studioConnection'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { StudioKindList } from './StudioKindList'

const application = 'PurchaseRequests'
const path = `/api/studio/apps/${application}/kinds/entity/resources`

const row = (id: string, title: string, rest: Partial<StudioKindListItem> = {}): StudioKindListItem => ({
  id,
  title,
  locale: null,
  description: null,
  module: null,
  moduleLabel: null,
  status: null,
  modifiedBy: null,
  modifiedAt: null,
  ...rest,
})

/** A page of the list: `items` is the page and `totalCount` how many match in all. */
function answer(items: StudioKindListItem[], totalCount = items.length) {
  return new Response(JSON.stringify({ items, page: 1, pageSize: 20, totalCount }), {
    headers: { 'Content-Type': 'application/json' },
  })
}

function stubList(reply: (url: string) => Response, kind = 'entity') {
  const fetchMock = vi.fn(async (input: string) =>
    input.startsWith(`/api/studio/apps/${application}/kinds/${kind}/resources`)
      ? reply(input)
      : new Response(null, { status: 404 }),
  )
  vi.stubGlobal('fetch', fetchMock)
  // The table's own choices of columns are not a request for its rows.
  return () => fetchMock.mock.calls.map(([input]) => input).filter((input) => !input.startsWith('/api/me/tables/'))
}

const Location = () => <output data-testid="location">{useLocation().search}</output>

function renderList(address = '/studio?app=PurchaseRequests&kind=entity', kind = 'entity') {
  const page = (folderRevision: number) => (
    <MemoryRouter initialEntries={[address]}>
      <ThemeModeProvider>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <StudioConnectionContext.Provider value={{ generation: 0, reconnecting: false, lost: vi.fn() }}>
            <StudioKindList application={application} kind={kind} folderRevision={folderRevision} />
            <Location />
          </StudioConnectionContext.Provider>
        </TextProvider>
      </ThemeModeProvider>
    </MemoryRouter>
  )
  const view = render(page(1))
  return { again: (folderRevision: number) => view.rerender(page(folderRevision)) }
}

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('StudioKindList', () => {
  it('asks the server for the search, the filter and the sort of the address and shows the rows it answers', async () => {
    const filter = JSON.stringify({ all: [{ field: 'status', op: 'eq', value: 'changed' }] })
    const requested = stubList(() => answer([row('e1', 'Supplier', { status: 'changed' })]))

    renderList(
      `/studio?app=PurchaseRequests&kind=entity&search=supp&filter=${encodeURIComponent(filter)}&sort=-modifiedAt`,
    )

    expect(await screen.findByText('Supplier')).toBeVisible()
    const [url] = requested()
    const query = new URLSearchParams(url.slice(path.length + 1))
    expect(query.get('search')).toBe('supp')
    expect(query.get('filter')).toBe(filter)
    expect(query.get('sort')).toBe('-modifiedAt')
    expect(query.get('locale')).toBe('en')
    expect(screen.getByRole('searchbox', { name: 'Search' })).toHaveValue('supp')
  })

  it('keeps a search typed in the Search box in the address and asks the server again', async () => {
    const requested = stubList(() => answer([row('e1', 'Supplier')]))
    renderList()
    await screen.findByText('Supplier')

    await userEvent.type(screen.getByRole('searchbox', { name: 'Search' }), 'supp{Enter}')

    await vi.waitFor(() => expect(requested()).toContain(`${path}?locale=en&search=supp`))
    expect(screen.getByTestId('location')).toHaveTextContent('search=supp')
  })

  it.each([
    ['Name', 'name'],
    ['Status', 'status'],
    ['Last modified by', 'modifiedBy'],
    ['Last modified at', 'modifiedAt'],
  ])('sorts from the header of %s and sends the sort to the server', async (title, field) => {
    const requested = stubList(() => answer([row('e1', 'Supplier')]))
    renderList()
    await screen.findByText('Supplier')

    await userEvent.click(screen.getByRole('columnheader', { name: title }))

    await vi.waitFor(() => expect(requested()).toContain(`${path}?locale=en&sort=${field}`))
    expect(screen.getByTestId('location')).toHaveTextContent(`sort=${field}`)
  })

  it('has no sort on the Description header', async () => {
    const requested = stubList(() => answer([row('e1', 'Supplier')]))
    renderList()
    await screen.findByText('Supplier')

    await userEvent.click(screen.getByRole('columnheader', { name: 'Description' }))
    await new Promise((resolve) => setTimeout(resolve, 50))

    expect(requested()).toEqual([`${path}?locale=en`])
    expect(screen.getByTestId('location')).not.toHaveTextContent('sort=')
  })

  it('reads the range of the page and keeps a page size in the address', async () => {
    const page = Array.from({ length: 20 }, (_, index) => row(`e${index}`, `Entity ${index + 1}`))
    const requested = stubList(() => answer(page, 45))
    renderList()

    expect(await screen.findByText('1–20 of 45')).toBeVisible()
    await userEvent.click(screen.getByRole('combobox'))
    await userEvent.click(await screen.findByTitle('50 / page'))

    await vi.waitFor(() => expect(requested()).toContain(`${path}?locale=en&pageSize=50`))
    expect(screen.getByTestId('location')).toHaveTextContent('pageSize=50')
  })

  it('shows the five columns, links each name to its resource and shows the dash in a cell with no value', async () => {
    stubList(() =>
      answer([
        row('e1', 'Supplier', {
          description: 'Who we buy from.',
          status: 'changed',
          modifiedBy: 'Linh Nguyen',
          modifiedAt: '2026-11-02T09:30:00Z',
        }),
        row('e2', 'Department', { status: 'new' }),
        row('e3', 'Budget', { status: 'published' }),
      ]),
    )
    renderList()

    const group = await screen.findByTestId('studio-group-entity')
    expect(
      within(group)
        .getAllByRole('columnheader')
        .map((header) => header.textContent),
    ).toEqual(['Name', 'Description', 'Status', 'Last modified by', 'Last modified at'])
    const link = within(group).getByRole('link', { name: 'Supplier' })
    expect(link).toHaveAttribute('href', studioResourceAddress(application, 'e1'))
    expect(link).toHaveAttribute('data-testid', 'studio-resource-e1')
    const time = new Intl.DateTimeFormat('en', { dateStyle: 'medium', timeStyle: 'medium' }).format(
      new Date('2026-11-02T09:30:00Z'),
    )
    const changed = within(link.closest('tr')!)
    expect(changed.getByText('Who we buy from.')).toBeVisible()
    expect(changed.getByText('Changed')).toBeVisible()
    expect(changed.getByText('Linh Nguyen')).toBeVisible()
    expect(changed.getByText(time)).toBeVisible()
    const cells = within(within(group).getByRole('link', { name: 'Department' }).closest('tr')!).getAllByRole('cell')
    expect(cells[1]).toHaveTextContent('—')
    expect(cells[2]).toHaveTextContent('New')
    expect(cells[3]).toHaveTextContent('—')
    expect(cells[4]).toHaveTextContent('—')
    expect(within(group).getByText('Published')).toBeVisible()
  })

  it('shows no Loading or Unavailable in the Status cell', async () => {
    stubList(() => answer([row('e1', 'Supplier')]))
    renderList()

    await screen.findByText('Supplier')
    expect(screen.queryByText('Loading…')).not.toBeInTheDocument()
    expect(screen.queryByText('Unavailable')).not.toBeInTheDocument()
  })

  it('tags a module resource with its module’s label, else with its id', async () => {
    stubList(() =>
      answer([
        row('e1', 'Staff member', { module: 'organization', moduleLabel: 'Organization' }),
        row('e2', 'Budget', { module: 'department-budgets', moduleLabel: null }),
        row('e3', 'Supplier'),
      ]),
    )
    renderList()

    const group = await screen.findByTestId('studio-group-entity')
    const tagOf = (name: string) => within(within(group).getByRole('link', { name }).closest('tr')!)
    expect(tagOf('Staff member').getByText('Organization')).toBeVisible()
    expect(tagOf('Budget').getByText('department-budgets')).toBeVisible()
    expect(tagOf('Supplier').queryByText('Organization')).not.toBeInTheDocument()
  })

  it('shows the name of the language for a text resource', async () => {
    stubList(() => answer([row('t1', 'vi', { locale: 'vi' })]), 'text')
    renderList('/studio?app=PurchaseRequests&kind=text', 'text')

    expect(await screen.findByRole('link', { name: 'Vietnamese' })).toBeVisible()
    expect(screen.queryByRole('link', { name: 'vi' })).not.toBeInTheDocument()
  })

  it('asks the server again when the folder changes, and shows a resource that was created', async () => {
    let created = false
    const requested = stubList(() =>
      answer(created ? [row('e1', 'Supplier'), row('e2', 'Budget')] : [row('e1', 'Supplier')]),
    )
    const { again } = renderList()
    await screen.findByText('Supplier')
    expect(screen.queryByText('Budget')).not.toBeInTheDocument()

    created = true
    again(2)

    expect(await screen.findByText('Budget')).toBeVisible()
    expect(requested()).toHaveLength(2)
  })

  it('asks the server again when Refresh is pressed', async () => {
    const requested = stubList(() => answer([row('e1', 'Supplier')]))
    renderList()
    await screen.findByText('Supplier')

    await userEvent.click(screen.getByRole('button', { name: 'Refresh' }))

    await vi.waitFor(() => expect(requested()).toHaveLength(2))
  })

  it('shows one error above the table when the list does not load', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubList(() => new Response(null, { status: 500 }))
    renderList()

    expect(await screen.findByTestId('studio-list-error')).toHaveTextContent('The list could not be loaded.')
    expect(warn).toHaveBeenCalledWith(expect.stringContaining('failed'), expect.anything())
  })
})
