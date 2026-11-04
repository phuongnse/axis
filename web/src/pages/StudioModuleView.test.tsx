import { render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import type { ModuleResourceItem } from '../platform/studio'
import { ThemeModeProvider } from '../platform/theme'
import { ModuleResourceView } from './StudioModuleView'

afterEach(() => vi.unstubAllGlobals())

const scenario = { now: '2026-10-01T09:00:00Z', steps: [{ name: 'create', user: 'approver' }] }

function renderView(item: ModuleResourceItem, response: Response) {
  const fetchMock = vi.fn(async (_input: string) => response)
  vi.stubGlobal('fetch', fetchMock)
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <ModuleResourceView
          application="Checklist App"
          item={item}
          diagnostics={[]}
          onModuleWritten={vi.fn()}
          onManifestSaved={vi.fn()}
          onManifestStale={vi.fn()}
        />
      </TextProvider>
    </ThemeModeProvider>,
  )
  return fetchMock
}

describe('ModuleResourceView', () => {
  it('opens a module scenario read-only with its content as JSON and no save button', async () => {
    const fetchMock = renderView(
      { id: 'scenario-id', kind: 'scenario', name: 'checklists/ChecklistCloses', module: 'checklists' },
      new Response(JSON.stringify({ id: 'scenario-id', kind: 'scenario', name: 'ChecklistCloses', content: scenario })),
    )

    expect(await screen.findByTestId('studio-module-read-only')).toHaveAttribute('role', 'alert')
    expect(String(fetchMock.mock.calls[0]?.[0])).toBe(
      '/api/studio/apps/Checklist%20App/modules/checklists/resources/scenario-id',
    )
    expect(screen.getByTestId('studio-module-content').textContent).toBe(JSON.stringify(scenario, null, 2))
    expect(screen.queryByTestId('studio-save')).not.toBeInTheDocument()
  })

  it('shows the load error when the file does not load', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    renderView(
      { id: 'seed-id', kind: 'seed', name: 'checklists/Items', module: 'checklists' },
      new Response('', { status: 500 }),
    )

    expect(await screen.findByTestId('studio-resource-load-error')).toBeInTheDocument()
    expect(screen.queryByTestId('studio-module-content')).not.toBeInTheDocument()
  })
})
