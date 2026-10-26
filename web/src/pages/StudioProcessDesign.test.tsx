import type { DndContextProps } from '@dnd-kit/core'
import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { setAt } from '../platform/jsonPatch'
import type { StudioNames } from '../platform/studio'
import { StudioNamesContext } from '../platform/studioNames'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { paletteEntries } from './processPalette'
import StudioProcessDesign from './StudioProcessDesign'

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

/** Applies each change to the draft, as the Studio editor does, and records the changes and the selections. */
function renderDesign(start: number | null) {
  const calls = { changes: [] as [string, unknown][], selected: [] as (number | null)[] }
  function Harness() {
    const [draft, setDraft] = useState<Record<string, unknown>>(initial)
    const [selected, setSelected] = useState(start)
    return (
      <ThemeModeProvider>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <StudioNamesContext.Provider value={names}>
            <StudioProcessDesign
              draft={draft}
              diagnosticPaths={[]}
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
    expect(announcement()).toHaveTextContent('Step raiseEvent1 added after resolve')
  })

  it('adds an unconnected step, with no source named, when the selected step has no next', async () => {
    const calls = renderDesign(2)
    await userEvent.click(screen.getByTestId('studio-process-palette-task'))

    expect(calls.changes).toHaveLength(1)
    const steps = calls.changes[0][1] as Steps
    expect(steps[2]).toEqual(initial.steps[2])
    expect(steps[4]).toMatchObject({ name: 'task1', type: 'task', outcomes: [] })
    expect(announcement()).toHaveTextContent(/^Step task1 added$/)
  })

  it('adds a step with no source when nothing is selected', async () => {
    const calls = renderDesign(null)
    await userEvent.click(screen.getByTestId('studio-process-palette-end'))

    expect(calls.changes).toEqual([['/steps', [...initial.steps, { name: 'end1', type: 'end' }]]])
    expect(calls.selected.at(-1)).toBe(4)
    expect(announcement()).toHaveTextContent(/^Step end1 added$/)
  })

  it('inserts into the selected edge before the selected step', async () => {
    const calls = renderDesign(0)
    await userEvent.click(await screen.findByTestId('rf__edge-/steps/2/otherwise'))
    await userEvent.click(screen.getByTestId('studio-process-palette-decision'))

    // The edge leaves check, so it is selected together with that step.
    const steps = calls.changes.at(-1)![1] as Steps
    expect(steps[2].otherwise).toBe('decision1')
    expect(steps[4]).toMatchObject({ name: 'decision1', otherwise: 'done' })
    expect(announcement()).toHaveTextContent('Step decision1 added after check')
  })

  it('inserts into the edge that a drop names with one change, and selects the new step', () => {
    const calls = renderDesign(null)
    drop('updateRecord', '/steps/1/next')

    expect(calls.changes).toHaveLength(1)
    const steps = calls.changes[0][1] as Steps
    expect(steps[1].next).toBe('updateRecord1')
    expect(steps[4]).toMatchObject({ name: 'updateRecord1', operation: 'updateRecord', next: 'done' })
    expect(calls.selected.at(-1)).toBe(4)
    expect(announcement()).toHaveTextContent('Step updateRecord1 added after resolve')
  })

  it('adds an unconnected step when the drop names the canvas', () => {
    const calls = renderDesign(null)
    drop('callConnector', '/steps')

    const steps = calls.changes[0][1] as Steps
    expect(steps[4]).toMatchObject({ name: 'callConnector1', next: '' })
    expect(announcement()).toHaveTextContent(/^Step callConnector1 added$/)
  })

  it('refuses an end step on an edge, changes nothing and says why', async () => {
    const calls = renderDesign(1)
    drop('end', '/steps/1/next')
    expect(calls.changes).toEqual([])
    expect(announcement()).toHaveTextContent('An end step cannot be inserted into a transition')

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
