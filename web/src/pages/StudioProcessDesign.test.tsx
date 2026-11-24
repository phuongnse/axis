import type { DndContextProps } from '@dnd-kit/core'
import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { colourTokens } from '../platform/designTokens'
import { saveFile } from '../platform/exports'
import { setAt } from '../platform/jsonPatch'
import type { StudioNames } from '../platform/studio'
import { StudioNamesContext } from '../platform/studioNames'
import { loadKindSchema, type JsonSchema } from '../platform/studioSchemas'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { chooseRowAction } from '../test/antd'
import { stubResizeObserver } from '../test/resizeObserver'
import { ShellAsideHost } from '../test/shellAsideHost'
import type { DesignerLayer } from './LayerMarkViews'
import { paletteEntries } from './processPalette'
import StudioProcessDesign from './StudioProcessDesign'

// A download is saved through `saveFile`, which the test reads instead of clicking a link jsdom cannot follow.
vi.mock('../platform/exports', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../platform/exports')>()),
  saveFile: vi.fn(),
}))

// The drag library draws as it is, and the test calls the drop handler that the designer gives it, because jsdom has no
// layout to hit-test.
const dnd = vi.hoisted(() => ({ props: undefined as DndContextProps | undefined }))
vi.mock('@dnd-kit/core', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@dnd-kit/core')>()
  return {
    ...actual,
    DndContext: (props: DndContextProps) => {
      dnd.props = props
      return <actual.DndContext {...props} />
    },
  }
})

const initial = {
  start: 'submit',
  steps: [
    { name: 'submit', type: 'operation', operation: 'updateRecord', set: {}, next: 'resolve' },
    { name: 'resolve', type: 'operation', operation: 'updateRecord', set: {}, next: 'done' },
    { name: 'check', type: 'decision', branches: [], otherwise: 'done' },
    { name: 'done', type: 'end' },
  ],
}

const names = {
  locales: ['en'],
  entities: [],
  dataSources: [],
  processes: [],
  rules: [],
  connectors: [],
  events: [],
  roles: [],
  sequences: [],
  forms: [],
  pages: [],
  texts: [],
} as unknown as StudioNames

type Steps = Record<string, unknown>[]

let schema: JsonSchema

beforeAll(async () => {
  schema = (await loadKindSchema('process'))!
})

/** Applies each change to the draft, as the Studio editor does, and records the changes and the selections. */
function renderDesign(
  start: number | null,
  layer?: DesignerLayer,
  options: { draft?: Record<string, unknown>; diagnosticPaths?: string[]; initialMode?: 'light' | 'dark' } = {},
) {
  const calls = { changes: [] as [string, unknown][], selected: [] as (number | null)[] }
  function Harness() {
    const [draft, setDraft] = useState<Record<string, unknown>>(options.draft ?? initial)
    const [selected, setSelected] = useState(start)
    return (
      <ThemeModeProvider initialMode={options.initialMode}>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <StudioNamesContext.Provider value={names}>
            <ShellAsideHost>
              <StudioProcessDesign
                schema={schema}
                draft={draft}
                diagnosticPaths={options.diagnosticPaths ?? []}
                layer={layer}
                selected={selected}
                onSelect={(index) => {
                  calls.selected.push(index)
                  setSelected(index)
                }}
                onChange={(pointer, next) => {
                  calls.changes.push([pointer, next])
                  setDraft((current) => setAt(current, pointer, next) as Record<string, unknown>)
                }}
              />
            </ShellAsideHost>
          </StudioNamesContext.Provider>
        </TextProvider>
      </ThemeModeProvider>
    )
  }
  render(<Harness />)
  return calls
}

beforeEach(() =>
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => new Response(JSON.stringify({ type: 'boolean', diagnostics: [] }), { status: 200 })),
  ),
)

afterEach(() => vi.unstubAllGlobals())

function entry(key: string) {
  return paletteEntries.find((candidate) => candidate.key === key)!
}

function drop(key: string, pointer: string) {
  act(() =>
    dnd.props!.onDragEnd!({
      active: { data: { current: { entry: entry(key) } } },
      over: { data: { current: { pointer } } },
    } as never),
  )
}

const announcement = () => screen.getByTestId('studio-process-announcement')

describe('the palette of the process designer', () => {
  it('has a button for each entry', () => {
    renderDesign(null)
    for (const { key } of paletteEntries) {
      expect(screen.getByTestId(`studio-process-palette-${key}`)).toBeInTheDocument()
    }
    expect(screen.getByTestId('studio-process-palette-package')).toHaveTextContent('Package operation')
  })

  it('inserts after the selected step with one change, selects the new step and says so', async () => {
    const calls = renderDesign(1)
    await userEvent.click(screen.getByTestId('studio-process-palette-raiseEvent'))

    expect(calls.changes).toHaveLength(1)
    expect(calls.changes[0][0]).toBe('/steps')
    const steps = calls.changes[0][1] as Steps
    expect(steps[1].next).toBe('raiseEvent1')
    expect(steps[4]).toMatchObject({ name: 'raiseEvent1', operation: 'raiseEvent', next: 'done' })
    expect(calls.selected.at(-1)).toBe(4)
    expect(announcement()).toHaveTextContent(/^Added step raiseEvent1 after resolve\.$/)
  })

  it('adds an unconnected step, with no source named, when the selected step has no next', async () => {
    const calls = renderDesign(2)
    await userEvent.click(screen.getByTestId('studio-process-palette-task'))

    expect(calls.changes).toHaveLength(1)
    const steps = calls.changes[0][1] as Steps
    expect(steps[2]).toEqual(initial.steps[2])
    expect(steps[4]).toMatchObject({ name: 'task1', type: 'task', outcomes: [] })
    expect(announcement()).toHaveTextContent(/^Added step task1\.$/)
  })

  it('adds a step with no source when nothing is selected', async () => {
    const calls = renderDesign(null)
    await userEvent.click(screen.getByTestId('studio-process-palette-end'))

    expect(calls.changes).toEqual([['/steps', [...initial.steps, { name: 'end1', type: 'end' }]]])
    expect(calls.selected.at(-1)).toBe(4)
    expect(announcement()).toHaveTextContent(/^Added step end1\.$/)
  })

  it('inserts into the selected edge before the selected step', async () => {
    const calls = renderDesign(0)
    await userEvent.click(await screen.findByTestId('rf__edge-/steps/2/otherwise'))
    await userEvent.click(screen.getByTestId('studio-process-palette-decision'))

    // The edge leaves check, so it is selected together with that step.
    const steps = calls.changes.at(-1)![1] as Steps
    expect(steps[2].otherwise).toBe('decision1')
    expect(steps[4]).toMatchObject({ name: 'decision1', otherwise: 'done' })
    expect(announcement()).toHaveTextContent(/^Added step decision1 after check\.$/)
  })

  it('inserts into the edge that a drop names with one change, and selects the new step', () => {
    const calls = renderDesign(null)
    drop('updateRecord', '/steps/1/next')

    expect(calls.changes).toHaveLength(1)
    const steps = calls.changes[0][1] as Steps
    expect(steps[1].next).toBe('updateRecord1')
    expect(steps[4]).toMatchObject({ name: 'updateRecord1', operation: 'updateRecord', next: 'done' })
    expect(calls.selected.at(-1)).toBe(4)
    expect(announcement()).toHaveTextContent(/^Added step updateRecord1 after resolve\.$/)
  })

  it('adds an unconnected step when the drop names the canvas', () => {
    const calls = renderDesign(null)
    drop('callConnector', '/steps')

    const steps = calls.changes[0][1] as Steps
    expect(steps[4]).toMatchObject({ name: 'callConnector1', next: '' })
    expect(announcement()).toHaveTextContent(/^Added step callConnector1\.$/)
  })

  it('refuses an end step on an edge, changes nothing and says why', async () => {
    const calls = renderDesign(1)
    drop('end', '/steps/1/next')
    expect(calls.changes).toEqual([])
    expect(announcement()).toHaveTextContent(/^An end step cannot go on a line\.$/)

    await userEvent.click(screen.getByTestId('studio-process-palette-end'))
    expect(calls.changes).toEqual([])
  })

  it('ignores a drop that names no transition', () => {
    const calls = renderDesign(null)
    drop('task', '/steps/1/branches/0')
    expect(calls.changes).toEqual([])
    expect(announcement()).toBeEmptyDOMElement()
  })
})

describe('moving a step', () => {
  const [submit, resolve, check, done] = initial.steps

  it('moves a step to the start with one change, keeps it selected and says so', async () => {
    const calls = renderDesign(1)
    await chooseRowAction(screen.getByTestId('studio-step-actions-1'), 'Move up')

    expect(calls.changes).toEqual([['/steps', [resolve, submit, check, done]]])
    expect(calls.selected.at(-1)).toBe(0)
    expect(announcement()).toHaveTextContent(/^Moved step resolve to the start\.$/)
  })

  it('moves a step down and names the step that is now before it', async () => {
    const calls = renderDesign(1)
    await chooseRowAction(screen.getByTestId('studio-step-actions-1'), 'Move down')

    expect(calls.changes).toEqual([['/steps', [submit, check, resolve, done]]])
    expect(calls.selected.at(-1)).toBe(2)
    expect(announcement()).toHaveTextContent(/^Moved step resolve after check\.$/)
  })

  it('moves a step up past another and names the step that is now before it', async () => {
    const calls = renderDesign(2)
    await chooseRowAction(screen.getByTestId('studio-step-actions-2'), 'Move up')

    expect(calls.changes).toEqual([['/steps', [submit, check, resolve, done]]])
    expect(calls.selected.at(-1)).toBe(1)
    expect(announcement()).toHaveTextContent(/^Moved step check after submit\.$/)
  })
})

describe('removing a line', () => {
  it('draws Remove line as a destructive button, and clicking it clears that line and says so', async () => {
    const calls = renderDesign(0)
    await userEvent.click(await screen.findByTestId('rf__edge-/steps/2/otherwise'))
    const remove = within(screen.getByTestId('studio-process-transition')).getByRole('button', { name: 'Remove line' })

    expect(remove).toHaveClass('ant-btn-variant-outlined')
    expect(remove).toHaveStyle({ color: colourTokens.dangerText.light, borderColor: colourTokens.dangerText.light })
    await userEvent.click(remove)

    expect(calls.changes.at(-1)).toEqual(['/steps/2/otherwise', ''])
    expect(screen.queryByTestId('rf__edge-/steps/2/otherwise')).not.toBeInTheDocument()
    expect(announcement()).toHaveTextContent(/^Removed the line check to done, otherwise\.$/)
  })
})

describe('a module process with a customization layer', () => {
  // The first lookup waits for the graph to lay out, which is slow while other test files run.
  const slow = { timeout: 5000 }

  function renderLayered(disabled = false) {
    const onReset = vi.fn()
    renderDesign(2, {
      elements: [
        { list: '/steps', key: 'check', mark: 'changed' },
        { list: '/steps', key: 'review', mark: 'removed' },
      ],
      disabled,
      onReset,
    })
    return onReset
  }

  it('marks a changed step on its graph node and on its block, with Reset to module', async () => {
    const onReset = renderLayered()

    expect(await screen.findByRole('button', { name: 'check, decision, Changed' }, slow)).toBe(
      screen.getByTestId('studio-graph-node-2'),
    )
    expect(screen.getByRole('button', { name: 'submit, operation' })).toBeInTheDocument()
    expect(
      within(screen.getByTestId('studio-step-2')).getByRole('note', { name: 'check, changed by this application' }),
    ).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Reset to module check' }))

    expect(onReset).toHaveBeenCalledWith('/steps', 'check')
  })

  it('lists a removed step as a muted row under the graph, whose Reset to module resets that step', async () => {
    const onReset = renderLayered()

    const row = await screen.findByTestId('studio-layer-removed-/steps/review', undefined, slow)
    expect(row).toHaveAttribute('data-muted')
    expect(within(row).getByRole('note', { name: 'review, removed by this application' })).toBeInTheDocument()
    await userEvent.click(within(row).getByRole('button', { name: 'Reset to module review' }))

    expect(onReset).toHaveBeenCalledWith('/steps', 'review')
  })

  it('turns Reset to module off while the draft has unsaved edits', async () => {
    renderLayered(true)

    expect(await screen.findByRole('button', { name: 'Reset to module check' }, slow)).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Reset to module review' })).toBeDisabled()
  })

  it('shows no marks when the process has no layer', async () => {
    renderDesign(2)

    expect(await screen.findByRole('button', { name: 'check, decision' }, slow)).toBeInTheDocument()
    expect(screen.queryByTestId('studio-layer-mark-/steps/2')).toBeNull()
  })
})

describe('downloading the graph', () => {
  const slow = { timeout: 10000 }
  const purchase = {
    name: 'PurchaseApproval',
    start: 'submit',
    steps: [
      { name: 'submit', type: 'operation', operation: 'updateRecord', set: {}, next: 'check' },
      { name: 'check', type: 'decision', branches: [{ when: 'approved', next: 'done' }], otherwise: 'submit' },
      { name: 'done', type: 'end' },
    ],
  }

  beforeEach(() => {
    vi.mocked(saveFile).mockClear()
    stubResizeObserver()
    // The image library tests for SVG images, which jsdom does not have.
    vi.stubGlobal('SVGImageElement', class {})
  })

  /** Chooses `format` in the Download menu and waits for the file that is saved. */
  async function download(format: 'SVG' | 'PNG') {
    await userEvent.click(await screen.findByRole('button', { name: 'Download' }, slow))
    await userEvent.click(await screen.findByRole('menuitem', { name: format }))
    await waitFor(() => expect(saveFile).toHaveBeenCalledTimes(1), slow)
    return vi.mocked(saveFile).mock.calls[0]!
  }

  it('saves the whole graph as an SVG named after the process, with no control', async () => {
    renderDesign(null, undefined, { draft: purchase })

    const [blob, name] = await download('SVG')
    const svg = await blob.text()

    expect(name).toBe('purchase-approval.svg')
    expect(blob.type).toBe('image/svg+xml')
    for (const label of ['submit', 'check', 'done', 'approved', 'otherwise']) {
      expect(svg).toContain(label)
    }
    for (const left of [
      'react-flow__minimap',
      'react-flow__background',
      'react-flow__attribution',
      'react-flow__controls',
      'Zoom in',
      'Find a step',
      'Download',
    ]) {
      expect(svg).not.toContain(left)
    }
  })

  it('saves a PNG named after the process', async () => {
    const draw = vi.fn()
    vi.stubGlobal(
      'Image',
      class {
        onload: (() => void) | null = null
        set src(_: string) {
          queueMicrotask(() => this.onload?.())
        }
      },
    )
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue({ drawImage: draw } as never)
    vi.spyOn(HTMLCanvasElement.prototype, 'toBlob').mockImplementation((callback, type) =>
      callback(new Blob(['png'], { type })),
    )
    renderDesign(null, undefined, { draft: purchase })

    const [blob, name] = await download('PNG')

    expect(name).toBe('purchase-approval.png')
    expect(blob.type).toBe('image/png')
    expect(draw).toHaveBeenCalledOnce()
  })

  it('names a module process after the text past its last slash', async () => {
    renderDesign(null, undefined, { draft: { ...purchase, name: 'orders/ShipOrder' } })

    const [, name] = await download('SVG')

    expect(name).toBe('ship-order.svg')
  })

  it('draws the light theme on a white surface in dark mode, with no selection and no diagnostic mark', async () => {
    renderDesign(0, undefined, { draft: purchase, diagnosticPaths: ['/steps/0'], initialMode: 'dark' })

    const [blob] = await download('SVG')
    const svg = await blob.text()

    const rgb = (hex: string) => {
      const [r, g, b] = [1, 3, 5].map((at) => Number.parseInt(hex.slice(at, at + 2), 16))
      return `rgb(${r}, ${g}, ${b})`
    }
    expect(svg).toContain(rgb(colourTokens.surface.light))
    expect(svg).not.toContain(rgb(colourTokens.surface.dark))
    expect(svg).not.toContain(rgb(colourTokens.danger.light))
    expect(svg).not.toContain(rgb(colourTokens.danger.dark))
    expect(svg).not.toContain(rgb(colourTokens.primary.light))
    expect(svg).not.toContain(rgb(colourTokens.primary.dark))
  })
})
