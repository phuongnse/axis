import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Outlet, Route, Routes } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import type { Cell } from '../platform/cell'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { CellPage } from './CellPage'
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

function stubCell(status: number, cell?: Cell) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => new Response(JSON.stringify(cell ?? { status }), { status })),
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
                <Route index element={<CellPage />} />
              </Route>
            </Routes>
          </TextProvider>
        </PlatformContext.Provider>
      </ThemeModeProvider>
    </MemoryRouter>,
  )
  return onRefused
}

describe('CellPage', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('shows the id, the region and the tenants against the cap', async () => {
    stubCell(200, { id: 'vn-1', region: 'vn', tenants: 12, cap: 50 })
    renderPage()

    const overview = await screen.findByTestId('cell-overview')
    expect(overview).toHaveTextContent('vn-1')
    expect(overview).toHaveTextContent('vn')
    expect(overview).toHaveTextContent('12 of 50')
  })

  it('shows the count alone when the cell has no cap', async () => {
    stubCell(200, { id: 'vn-1', region: 'vn', tenants: 12, cap: null })
    renderPage()

    const overview = await screen.findByTestId('cell-overview')
    expect(overview).toHaveTextContent('12')
    expect(overview).not.toHaveTextContent('of')
  })

  it('reports a refusal to the console', async () => {
    stubCell(403)
    const onRefused = renderPage()

    await waitFor(() => expect(onRefused).toHaveBeenCalledWith('forbidden'))
    expect(screen.queryByTestId('cell-overview')).not.toBeInTheDocument()
  })

  it('shows an error when the cell fails to load', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubCell(500)
    renderPage()

    expect(await screen.findByTestId('cell-error')).toHaveTextContent('The cell could not be loaded.')
  })
})
