import { render, screen, within } from '@testing-library/react'
import type { ReactNode } from 'react'
import { describe, expect, it } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { ProcessGraphCanvas } from './ProcessGraph'
import type { RunState } from './inspection'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'
import { colourTokens } from './designTokens'

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

function Providers({ children }: { children: ReactNode }) {
  return (
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <div style={{ width: 800, height: 600 }}>{children}</div>
      </TextProvider>
    </ThemeModeProvider>
  )
}

/** The x of a node, from the translate of its wrapper. The first layout is computed by ELK, so it is awaited. */
async function nodeX(id: string): Promise<number> {
  const node = await screen.findByTestId(`rf__node-${id}`, undefined, { timeout: 5000 })
  const match = /translate\((-?[\d.]+)px,\s*(-?[\d.]+)px\)/.exec(node.style.transform)
  expect(match, `the transform of ${id}: ${node.style.transform}`).not.toBeNull()
  return Number(match![1])
}

describe('ProcessGraphCanvas', () => {
  it('lays the steps of a linear process left to right', async () => {
    render(
      <Providers>
        <ProcessGraphCanvas steps={steps} transitions={transitions} label="Process" />
      </Providers>,
    )

    const xs = [await nodeX('a'), await nodeX('b'), await nodeX('c'), await nodeX('d')]

    expect(xs[1]).toBeGreaterThan(xs[0]!)
    expect(xs[2]).toBeGreaterThan(xs[1]!)
    expect(xs[3]).toBeGreaterThan(xs[2]!)
    expect(screen.getByRole('region', { name: 'Process' })).toBeInTheDocument()
  })

  it('names each card by its whole label, with its type as the tooltip of its icon', async () => {
    render(
      <Providers>
        <ProcessGraphCanvas steps={steps} transitions={transitions} label="Process" />
      </Providers>,
    )

    const card = await screen.findByRole('group', { name: 'Check the amount' }, { timeout: 5000 })
    expect(card).toHaveStyle({ width: '200px', height: '64px' })
    expect(within(card).getByRole('img', { name: 'Decision' })).toBeInTheDocument()
  })

  it('shows a state badge in its tone on each step reached, and none on a step not reached', async () => {
    const runStates = new Map<string, RunState>([
      ['a', 'done'],
      ['b', 'waitingForPerson'],
    ])
    render(
      <Providers>
        <ProcessGraphCanvas
          steps={steps}
          transitions={transitions}
          label="Process"
          runStates={runStates}
          taken={new Set(['a-b'])}
        />
      </Providers>,
    )

    const a = await screen.findByTestId('rf__node-a', undefined, { timeout: 5000 })
    const b = screen.getByTestId('rf__node-b')
    const c = screen.getByTestId('rf__node-c')

    expect(within(a).getByRole('img', { name: 'Done' })).toHaveAttribute('data-tone', 'success')
    expect(within(b).getByRole('img', { name: 'Waiting for a person' })).toHaveAttribute('data-tone', 'attention')
    expect(c.querySelector('[data-tone]')).toBeNull()
  })

  it('draws a taken transition in successText and a transition not taken dashed and thinner', async () => {
    const { container } = render(
      <Providers>
        <ProcessGraphCanvas
          steps={steps}
          transitions={transitions}
          label="Process"
          runStates={new Map<string, RunState>([['a', 'done']])}
          taken={new Set(['a-b'])}
        />
      </Providers>,
    )

    await screen.findByTestId('rf__node-a', undefined, { timeout: 5000 })
    const taken = container.querySelector<SVGPathElement>('[data-testid="rf__edge-a-b"] .react-flow__edge-path')!
    const notTaken = container.querySelector<SVGPathElement>('[data-testid="rf__edge-b-c"] .react-flow__edge-path')!

    expect(taken.style.stroke).toBe('rgb(35, 120, 4)') // successText, light
    expect(taken.style.strokeDasharray).toBe('')
    expect(notTaken.style.strokeDasharray).toBe('4 4')
    expect(Number(notTaken.style.strokeWidth)).toBeLessThan(Number(taken.style.strokeWidth))
    expect(taken.getAttribute('d')).toMatch(/^M [\d. -]+ L /)
  })

  it('marks the chosen step with aria-current and a button, and calls onChoose', async () => {
    const chosen: string[] = []
    render(
      <Providers>
        <ProcessGraphCanvas
          steps={steps}
          transitions={transitions}
          label="Process"
          chosen="b"
          onChoose={(id) => chosen.push(id)}
        />
      </Providers>,
    )

    const b = await screen.findByRole('button', { name: 'Check the amount' }, { timeout: 5000 })
    expect(b).toHaveAttribute('aria-current', 'true')
    expect(screen.getByRole('button', { name: 'Done' })).not.toHaveAttribute('aria-current')
    b.click()
    screen.getByRole('button', { name: 'Done' }).click()
    expect(chosen).toEqual(['b', 'd'])
  })

  it('gives a marked step a danger border', async () => {
    render(
      <Providers>
        <ProcessGraphCanvas
          steps={[{ ...steps[0]!, marked: true }, steps[1]!]}
          transitions={[transitions[0]!]}
          label="Process"
        />
      </Providers>,
    )

    const card = await screen.findByRole('group', { name: 'Submit request' }, { timeout: 5000 })
    expect(card).toHaveStyle({ border: `1px solid ${colourTokens.danger.light}` })
  })

  it('draws no attribution and no controls', async () => {
    const { container } = render(
      <Providers>
        <ProcessGraphCanvas steps={steps} transitions={transitions} label="Process" />
      </Providers>,
    )

    await screen.findByTestId('rf__node-a', undefined, { timeout: 5000 })
    expect(container.querySelector('.react-flow__attribution')).toBeNull()
    expect(container.querySelector('.react-flow__controls')).toBeNull()
  })
})
