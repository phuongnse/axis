import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import type { RunState } from './inspection'
import { ProcessGraphCanvas } from './ProcessGraph'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'
import { stubResizeObserver } from '../test/resizeObserver'
import { reducedMotionQuery } from './useReducedMotion'

const steps = [
  { id: 'a', label: 'Submit request', type: 'task' },
  { id: 'b', label: 'Check the amount', type: 'decision' },
  { id: 'c', label: 'Pay the supplier', type: 'operation' },
  { id: 'd', label: 'Done', type: 'end' },
]

const transitions = [
  { id: 'a-b', source: 'a', target: 'b', name: 'Submit request to Check the amount' },
  { id: 'b-c', source: 'b', target: 'c', name: 'Check the amount to Pay the supplier' },
  { id: 'c-d', source: 'c', target: 'd', name: 'Pay the supplier to Done' },
]

// The first lookup waits for the graph to lay out, which is slow while other test files run.
const slow = { timeout: 5000 }

function Providers({ children }: { children: ReactNode }) {
  return (
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <div style={{ width: 800, height: 600 }}>{children}</div>
      </TextProvider>
    </ThemeModeProvider>
  )
}

/** The scale of the viewport, from its transform. */
function scale(container: HTMLElement): number {
  const transform = container.querySelector<HTMLElement>('.react-flow__viewport')!.style.transform
  const match = /scale\(([\d.]+)\)/.exec(transform)
  expect(match, `the transform of the viewport: ${transform}`).not.toBeNull()
  return Number(match![1])
}

/** Stubs matchMedia, so that only the reduced-motion query matches. */
function stubReducedMotion() {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query === reducedMotionQuery,
    media: query,
    onchange: null,
    addListener() {},
    removeListener() {},
    addEventListener() {},
    removeEventListener() {},
    dispatchEvent: () => false,
  }))
}

beforeEach(stubResizeObserver)

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('ProcessGraphControls', () => {
  it('gives each control its tooltip and its key', async () => {
    render(
      <Providers>
        <ProcessGraphCanvas steps={steps} transitions={transitions} label="Process" />
      </Providers>,
    )

    const controls = [
      ['Zoom in', 'Plus'],
      ['Zoom out', '-'],
      ['Show the whole process', '0'],
      ['Full screen', 'F'],
    ] as const
    for (const [name, key] of controls) {
      const button = await screen.findByRole('button', { name }, slow)
      expect(button).toHaveAttribute('aria-keyshortcuts', key)
      await userEvent.hover(button)
      expect(await screen.findByRole('tooltip', { name })).toBeInTheDocument()
      await userEvent.unhover(button)
    }
  })

  it('zooms with + and shows the whole process with 0 while a control has the focus', async () => {
    const { container } = render(
      <Providers>
        <ProcessGraphCanvas steps={steps} transitions={transitions} label="Process" />
      </Providers>,
    )

    const showAll = await screen.findByRole('button', { name: 'Show the whole process' }, slow)
    await screen.findByTestId('rf__node-a', undefined, slow)
    const before = scale(container)
    showAll.focus()

    fireEvent.keyDown(showAll, { key: '+' })
    await waitFor(() => expect(scale(container)).toBeGreaterThan(before))
    const zoomed = scale(container)
    fireEvent.keyDown(showAll, { key: '0' })
    await waitFor(() => expect(scale(container)).toBeLessThan(zoomed))
  })

  it('moves the view at once under reduced motion', async () => {
    stubReducedMotion()
    const { container } = render(
      <Providers>
        <ProcessGraphCanvas steps={steps} transitions={transitions} label="Process" />
      </Providers>,
    )

    const showAll = await screen.findByRole('button', { name: 'Show the whole process' }, slow)
    await screen.findByTestId('rf__node-a', undefined, slow)
    const fitted = scale(container)

    fireEvent.keyDown(showAll, { key: '+' })

    expect(scale(container)).toBeGreaterThan(fitted)
  })

  it('does nothing to the view for a key typed into Find a step', async () => {
    const { container } = render(
      <Providers>
        <ProcessGraphCanvas steps={steps} transitions={transitions} label="Process" />
      </Providers>,
    )

    const search = await screen.findByRole('combobox', { name: 'Find a step' }, slow)
    await screen.findByTestId('rf__node-a', undefined, slow)
    const fitted = scale(container)

    await userEvent.type(search, '+0')
    await new Promise((resolve) => setTimeout(resolve, 400))

    expect(scale(container)).toBe(fitted)
  })

  it('shows Go to the current step only on an instance with a current step', async () => {
    const runStates = new Map<string, RunState>([['a', 'done']])
    const { rerender } = render(
      <Providers>
        <ProcessGraphCanvas steps={steps} transitions={transitions} label="Process" runStates={runStates} current="b" />
      </Providers>,
    )

    const goTo = await screen.findByRole('button', { name: 'Go to the current step' }, slow)
    expect(goTo).not.toHaveAttribute('aria-keyshortcuts')

    rerender(
      <Providers>
        <ProcessGraphCanvas steps={steps} transitions={transitions} label="Process" current="b" />
      </Providers>,
    )
    expect(screen.queryByRole('button', { name: 'Go to the current step' })).not.toBeInTheDocument()

    rerender(
      <Providers>
        <ProcessGraphCanvas steps={steps} transitions={transitions} label="Process" runStates={runStates} />
      </Providers>,
    )
    expect(screen.queryByRole('button', { name: 'Go to the current step' })).not.toBeInTheDocument()
  })

  it('opens an instance on the current step at zoom 1, and Show the whole process and Go to the current step move between the views', async () => {
    stubReducedMotion()
    const { container } = render(
      <Providers>
        <ProcessGraphCanvas
          steps={steps}
          transitions={transitions}
          label="Process"
          runStates={new Map<string, RunState>([['a', 'done']])}
          current="b"
        />
      </Providers>,
    )

    const goTo = await screen.findByRole('button', { name: 'Go to the current step' }, slow)
    await screen.findByTestId('rf__node-a', undefined, slow)
    expect(scale(container)).toBe(1)

    fireEvent.click(screen.getByRole('button', { name: 'Show the whole process' }))
    await waitFor(() => expect(scale(container)).toBeLessThan(1))

    fireEvent.click(goTo)

    await waitFor(() => expect(scale(container)).toBe(1))
  })

  it('opens a graph with no current step on the whole process', async () => {
    const { container } = render(
      <Providers>
        <ProcessGraphCanvas steps={steps} transitions={transitions} label="Process" />
      </Providers>,
    )

    await screen.findByTestId('rf__node-a', undefined, slow)

    expect(scale(container)).toBeLessThan(1)
  })

  it('lists the steps in the order given, and choosing one calls onChoose', async () => {
    const onChoose = vi.fn()
    render(
      <Providers>
        <ProcessGraphCanvas
          steps={[steps[2]!, steps[0]!, steps[1]!]}
          transitions={transitions}
          label="Process"
          onChoose={onChoose}
        />
      </Providers>,
    )

    await userEvent.click(await screen.findByRole('combobox', { name: 'Find a step' }, slow))
    const options = screen.getAllByRole('option').map((option) => option.textContent)
    expect(options).toEqual(['Pay the supplier', 'Submit request', 'Check the amount'])

    await userEvent.click(screen.getByRole('option', { name: 'Check the amount' }))

    expect(onChoose).toHaveBeenCalledWith('b')
  })

  it('draws a done step in the overview in successText, and a step not reached muted', async () => {
    const { container } = render(
      <Providers>
        <ProcessGraphCanvas
          steps={steps}
          transitions={transitions}
          label="Process"
          runStates={new Map<string, RunState>([['a', 'done']])}
        />
      </Providers>,
    )

    await screen.findByRole('img', { name: 'Overview' }, slow)
    // The overview draws the steps in the order they are given.
    const [a, b] = [...container.querySelectorAll<SVGRectElement>('.react-flow__minimap-node')].map(
      (node) => node.style.fill,
    )

    expect(a).toBe('rgb(35, 120, 4)') // successText, light
    expect(b).not.toBe(a)
  })
})
