import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Outlet, Route, Routes } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import type { CellTenant } from '../platform/cell'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { CellTenantsPage } from './CellTenantsPage'
import { PlatformContext, type Platform } from './context'

const platform: Platform = {
  site: {
    name: 'platform',
    titleKey: 'shell.title',
    locales: { default: 'en', fallback: 'en', available: ['en'] },
    navigation: [],
  },
  fallbackTexts: platformEnglish,
  current: { locale: 'en', texts: platformEnglish },
  setCurrent: () => {},
  development: false,
}

const hoursAgo = (hours: number) => new Date(Date.now() - hours * 60 * 60 * 1000).toISOString()
const noUse = { limit: null, used: null }
const noUsage = { storage: noUse, users: noUse, applications: noUse, emailsPerDay: noUse }

const acme: CellTenant = {
  id: 'acme',
  status: 'active',
  application: 'crm',
  hosts: ['acme.example.com', 'portal.acme.test'],
  failedWork: { instances: 2, unknownCalls: 0, deadLetters: 1 },
  usage: {
    storage: { limit: 1024 ** 3 * 10, used: 1024 ** 2 * 512 },
    users: { limit: 50, used: null },
    applications: { limit: null, used: 3 },
    emailsPerDay: { limit: 1000, used: 1200 },
  },
  newestArchiveAt: hoursAgo(30),
}
const beta: CellTenant = {
  id: 'beta',
  status: 'suspended',
  application: null,
  hosts: ['beta.example.com'],
  failedWork: null,
  usage: noUsage,
  newestArchiveAt: null,
}
const gamma: CellTenant = {
  id: 'gamma',
  status: 'readOnly',
  application: 'hr',
  hosts: ['gamma.example.com'],
  failedWork: { instances: 0, unknownCalls: 0, deadLetters: 0 },
  usage: noUsage,
  newestArchiveAt: hoursAgo(2),
}

function stubTenants(status: number, items: CellTenant[] = []) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => new Response(JSON.stringify(status === 200 ? { items } : { status }), { status })),
  )
}

function renderPage(onRefused = vi.fn()) {
  render(
    <MemoryRouter>
      <ThemeModeProvider>
        <PlatformContext.Provider value={platform}>
          <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
            <Routes>
              <Route element={<Outlet context={{ onRefused }} />}>
                <Route index element={<CellTenantsPage />} />
              </Route>
            </Routes>
          </TextProvider>
        </PlatformContext.Provider>
      </ThemeModeProvider>
    </MemoryRouter>,
  )
  return onRefused
}

function dataRows() {
  return within(screen.getByTestId('cell-tenants')).getAllByRole('row').slice(1)
}

function rowIds() {
  return dataRows().map((row) => within(row).getAllByRole('cell')[0].textContent)
}

describe('CellTenantsPage', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('lists one row per tenant, with dashes for the failed work not yet counted', async () => {
    stubTenants(200, [acme, beta, gamma])
    renderPage()

    await screen.findByTestId('cell-tenants')
    const rows = dataRows()
    expect(rows).toHaveLength(3)
    expect(rows[0]).toHaveTextContent('Active')
    expect(rows[0]).toHaveTextContent('crm')
    expect(rows[0]).toHaveTextContent('acme.example.com, portal.acme.test')
    expect(
      within(rows[0])
        .getAllByRole('cell')
        .slice(4, 7)
        .map((cell) => cell.textContent),
    ).toEqual(['2', '0', '1'])
    expect(
      within(rows[1])
        .getAllByRole('cell')
        .slice(4, 7)
        .map((cell) => cell.textContent),
    ).toEqual(['—', '—', '—'])
    expect(within(rows[0]).getByRole('link', { name: 'acme' })).toHaveAttribute('href', '/tenants/acme')
  })

  it('shows the usage against the limits, with a dash for a count not yet filled', async () => {
    stubTenants(200, [acme, beta])
    renderPage()

    await screen.findByTestId('cell-tenants')
    const rows = dataRows()
    expect(
      within(rows[0])
        .getAllByRole('cell')
        .slice(7, 11)
        .map((cell) => cell.textContent),
    ).toEqual(['512 MB of 10 GB', '—', '3, no limit', '1,200 of 1,000'])
    expect(
      within(rows[1])
        .getAllByRole('cell')
        .slice(7, 11)
        .map((cell) => cell.textContent),
    ).toEqual(['—', '—', '—', '—'])
  })

  it('marks an archive more than 26 hours old and no other, and shows a dash for none', async () => {
    stubTenants(200, [acme, beta, gamma])
    renderPage()

    await screen.findByTestId('cell-tenants')
    const rows = dataRows()
    expect(within(rows[0]).getByTestId('cell-tenants-archive-stale')).toHaveTextContent('Overdue')
    expect(within(rows[1]).queryByTestId('cell-tenants-archive-stale')).toBeNull()
    expect(within(rows[1]).getAllByRole('cell')[11]).toHaveTextContent('—')
    expect(within(rows[2]).queryByTestId('cell-tenants-archive-stale')).toBeNull()
    expect(within(rows[2]).getAllByRole('cell')[11]).not.toHaveTextContent('—')
  })

  it('narrows the rows by id, ignoring case', async () => {
    stubTenants(200, [acme, beta, gamma])
    renderPage()

    await screen.findByTestId('cell-tenants')
    await userEvent.type(screen.getByLabelText('Search by ID or host'), 'BETA')

    expect(rowIds()).toEqual(['beta'])
  })

  it('narrows the rows by any host', async () => {
    stubTenants(200, [acme, beta, gamma])
    renderPage()

    await screen.findByTestId('cell-tenants')
    await userEvent.type(screen.getByLabelText('Search by ID or host'), 'portal')

    expect(rowIds()).toEqual(['acme'])
  })

  it('keeps only the tenants with the chosen status', async () => {
    stubTenants(200, [acme, beta, gamma])
    renderPage()

    await screen.findByTestId('cell-tenants')
    await userEvent.click(screen.getByRole('combobox', { name: 'Status' }))
    await userEvent.click(await screen.findByRole('option', { name: 'Read only' }))

    expect(rowIds()).toEqual(['gamma'])
  })

  it('reports a refusal to the console', async () => {
    stubTenants(401)
    const onRefused = renderPage()

    await waitFor(() => expect(onRefused).toHaveBeenCalledWith('unauthorized'))
  })

  it('shows an error when the tenants fail to load', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubTenants(500)
    renderPage()

    expect(await screen.findByTestId('cell-tenants-error')).toHaveTextContent('The tenants could not be loaded.')
  })
})
