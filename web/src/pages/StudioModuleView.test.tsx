import { cleanup, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import { StudioConnectionContext } from '../platform/studioConnection'
import { moduleKinds, type ModuleKind } from '../platform/moduleKinds'
import type { ModuleResourceItem } from '../platform/studio'
import { ThemeModeProvider } from '../platform/theme'
import { ModuleResourceView } from './StudioModuleView'

afterEach(() => vi.unstubAllGlobals())

const scenario = { now: '2026-10-01T09:00:00Z', steps: [{ name: 'create', user: 'approver' }] }

function renderView(item: ModuleResourceItem, respond: Response | ((url: string) => Response)) {
  const fetchMock = vi.fn(async (input: string) => (typeof respond === 'function' ? respond(String(input)) : respond))
  vi.stubGlobal('fetch', fetchMock)
  render(
    <MemoryRouter>
      <ThemeModeProvider>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <StudioConnectionContext.Provider value={{ generation: 0, reconnecting: false, lost: () => {} }}>
            <ModuleResourceView
              application="Checklist App"
              item={item}
              diagnostics={[]}
              onModuleWritten={vi.fn()}
              onManifestSaved={vi.fn()}
              onManifestStale={vi.fn()}
            />
          </StudioConnectionContext.Provider>
        </TextProvider>
      </ThemeModeProvider>
    </MemoryRouter>,
  )
  return fetchMock
}

/** The smallest content of each kind that opens in its editor. */
const contents: Record<Exclude<ModuleKind, 'scenario'>, Record<string, unknown>> = {
  entity: { fields: [{ name: 'title', type: 'string' }] },
  dataSource: { entity: 'Checklist', fields: [{ name: 'title', path: 'title' }] },
  rule: { parameters: [], resultType: 'boolean', expression: 'true' },
  sequence: { format: 'CL-{0000}' },
  form: { sections: [] },
  page: { widgets: [] },
  process: { steps: [] },
  event: { entity: 'Checklist', fields: [] },
  text: { locale: 'en', texts: {} },
  seed: { entity: 'Checklist', records: [] },
  policy: { grants: [] },
  trigger: { type: 'dataChange', process: 'Close' },
  contract: {
    entity: 'Checklist',
    lifecycle: { field: 'status', states: [{ name: 'open', initial: true }, { name: 'onHold' }], transitions: [] },
  },
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

  it('opens every kind but a scenario in an editor with the layer applied and a Save button', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    for (const kind of moduleKinds.filter((candidate) => candidate !== 'scenario')) {
      const layered = {
        id: 'resource-id',
        kind,
        name: 'Resource',
        file: 'modules/checklists/resource.json',
        version: 'm1',
        content: { id: 'resource-id', kind, name: 'Resource', ...contents[kind] },
        layer: { id: 'layer-id', file: 'customizations/checklists/Resource.json', version: 'v1' },
        marks: kind === 'contract' ? [{ path: '/lifecycle/states/1', mark: 'added' }] : [],
        removed: [],
      }
      renderView({ id: 'resource-id', kind, name: 'checklists/Resource', module: 'checklists' }, (url) =>
        url.endsWith('/page-metadata')
          ? new Response(JSON.stringify({ page: null, diagnostics: [] }))
          : new Response(JSON.stringify(layered)),
      )

      expect(await screen.findByTestId('studio-save'), kind).toBeInTheDocument()
      expect(screen.queryByTestId('studio-module-read-only'), kind).toBeNull()
      expect(screen.queryByTestId('studio-module-content'), kind).toBeNull()
      if (kind === 'contract') {
        expect(screen.getByRole('note', { name: 'Added onHold' }), kind).toBeInTheDocument()
      } else {
        expect(screen.getByTestId('studio-module-layer'), kind).toBeInTheDocument()
      }
      cleanup()
    }
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
