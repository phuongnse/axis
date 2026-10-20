import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import type { StudioResourceItem } from '../platform/studio'
import { ThemeModeProvider } from '../platform/theme'
import { loadKindSchema } from '../platform/studioSchemas'
import { StudioEditor } from './StudioEditor'

const folder = '/api/studio/apps/Shop'
const processId = '3c1f4a2e-8b7d-4e6f-9a0b-1c2d3e4f5a6b'
const seedId = '46e28f6b-ce3b-4a17-829e-92fb3a5930ad'

const processItem: StudioResourceItem = {
  id: processId,
  kind: 'process',
  name: 'Approve',
  file: 'processes/approve.json',
  version: 'p1',
  usedBy: 0,
}
const seedItem: StudioResourceItem = {
  id: seedId,
  kind: 'seed',
  name: 'Orders',
  file: 'seeds/orders.json',
  version: 's1',
  usedBy: 0,
}

const processContent = {
  id: processId,
  kind: 'process',
  name: 'Approve',
  formatVersion: 1,
  start: 'Start',
  steps: [
    { name: 'Start', type: 'operation', next: 'Review' },
    { name: 'Review', type: 'operation', next: 'End' },
    { name: 'End', type: 'end' },
  ],
}

const seedContent = {
  id: seedId,
  kind: 'seed',
  name: 'Orders',
  formatVersion: 1,
  entity: 'Order',
  sync: true,
  records: [],
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

function stub(item: StudioResourceItem, content: unknown) {
  const fetchMock = vi.fn(async (input: string, init?: RequestInit) => {
    if (input === `${folder}/resources/${item.id}` && init?.method === 'PATCH') {
      return json({ version: 'p2', diagnostics: [] })
    }
    if (input === `${folder}/resources/${item.id}`) {
      return json({ ...item, content })
    }
    return new Response(null, { status: 404 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function renderEditor(item: StudioResourceItem, path: string | null = null) {
  const onOpenPath = vi.fn()
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <StudioEditor
          application="Shop"
          item={item}
          resources={[item]}
          path={path}
          diagnosticPaths={[]}
          onOpenPath={onOpenPath}
          onSaved={vi.fn()}
          onDeleted={vi.fn()}
        />
      </TextProvider>
    </ThemeModeProvider>,
  )
  return { onOpenPath }
}

// The first lookup waits for the schema chunk and the fetch, which is slow while other test files run.
const slow = { timeout: 5000 }

beforeAll(() => loadKindSchema('process'))

describe('StudioEditor process graph', () => {
  it('draws a step added in the JSON view at once, without saving', async () => {
    const fetchMock = stub(processItem, processContent)
    renderEditor(processItem)
    await screen.findByRole('button', { name: 'End (end)' }, slow)

    await userEvent.click(screen.getByRole('tab', { name: 'JSON' }))
    const withNewStep = { ...processContent, steps: [...processContent.steps, { name: 'NewStep', type: 'end' }] }
    fireEvent.change(screen.getByRole('textbox', { name: 'JSON' }), { target: { value: JSON.stringify(withNewStep) } })

    expect(await screen.findByRole('button', { name: /NewStep \(end\)/ })).toBeInTheDocument()
    expect(fetchMock.mock.calls.some(([, init]) => init?.method === 'PATCH')).toBe(false)
  })

  describe.each(['light', 'dark'] as const)('in %s mode', (mode) => {
    it('tabs through the steps in order and opens the step Enter is on', async () => {
      localStorage.setItem('axis.themeMode', mode)
      stub(processItem, processContent)
      const { onOpenPath } = renderEditor(processItem)
      await screen.findByRole('button', { name: 'End (end)' }, slow)

      expect(document.activeElement).toBe(document.body)
      const names = ['Start (operation)', 'Review (operation)', 'End (end)']
      for (const [index, name] of names.entries()) {
        await userEvent.tab()
        const node = screen.getByTestId(`studio-graph-node-${index}`)
        expect(document.activeElement).toBe(node)
        expect(node).toHaveAccessibleName(name)
      }

      await userEvent.keyboard('{Enter}')
      expect(onOpenPath).toHaveBeenCalledWith('/steps/2')
    })
  })

  it('marks the node of the open path as current', async () => {
    stub(processItem, processContent)
    renderEditor(processItem, '/steps/1/next')

    expect(await screen.findByTestId('studio-graph-node-1', undefined, slow)).toHaveAttribute('aria-current', 'true')
    expect(screen.getByTestId('studio-graph-node-0')).not.toHaveAttribute('aria-current')
  })

  it('shows no process graph for a seed', async () => {
    stub(seedItem, seedContent)
    renderEditor(seedItem)
    await screen.findByRole('tab', { name: 'Form' }, slow)

    expect(screen.queryByTestId('studio-process-graph')).not.toBeInTheDocument()
  })
})
