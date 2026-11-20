import { DndContext } from '@dnd-kit/core'
import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { buildProcessGraph } from './processGraph'
import StudioProcessGraph from './StudioProcessGraph'

const graph = buildProcessGraph(
  {
    start: 'submit',
    steps: [
      { name: 'submit', type: 'operation', operation: 'a/b', next: 'review' },
      { name: 'review', type: 'operation', operation: 'a/b', next: 'approve' },
      { name: 'approve', type: 'operation', operation: 'a/b', next: 'done' },
      { name: 'done', type: 'end' },
    ],
  },
  [],
)
const paths = graph.transitions.map((transition) => transition.path)

// The first lookup waits for the graph to lay out, which is slow while other test files run.
const slow = { timeout: 5000 }

function renderGraph(handlers: Partial<Parameters<typeof StudioProcessGraph>[0]> = {}) {
  const props = {
    graph,
    selected: null,
    selectedEdge: null,
    onSelect: vi.fn(),
    onSelectEdge: vi.fn(),
    onRemove: vi.fn(),
    onConnect: vi.fn(),
    ...handlers,
  }
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <DndContext>
          <div style={{ width: 800, height: 600 }}>
            <StudioProcessGraph {...props} />
          </div>
        </DndContext>
      </TextProvider>
    </ThemeModeProvider>,
  )
  return props
}

async function nodeX(index: number): Promise<number> {
  const node = await screen.findByTestId(`rf__node-${index}`, undefined, slow)
  const match = /translate\((-?[\d.]+)px,\s*(-?[\d.]+)px\)/.exec(node.style.transform)
  expect(match, `the transform of ${index}: ${node.style.transform}`).not.toBeNull()
  return Number(match![1])
}

describe('StudioProcessGraph', () => {
  it('lays the steps from left to right, with a handle on the right edge of each step but an end step', async () => {
    renderGraph()

    const xs = [await nodeX(0), await nodeX(1), await nodeX(2), await nodeX(3)]
    expect(xs[1]).toBeGreaterThan(xs[0]!)
    expect(xs[2]).toBeGreaterThan(xs[1]!)
    expect(xs[3]).toBeGreaterThan(xs[2]!)
    for (const index of [0, 1, 2]) {
      expect(screen.getByTestId(`studio-graph-handle-${index}`)).toHaveClass('react-flow__handle-right')
    }
    expect(screen.queryByTestId('studio-graph-handle-3')).not.toBeInTheDocument()
  })

  it('shows no handle and removes nothing with readOnly, and removes without it', async () => {
    const readOnly = renderGraph({ readOnly: true })
    const node = await screen.findByTestId('studio-graph-node-1', undefined, slow)
    expect(document.querySelectorAll('[data-testid^="studio-graph-handle-"]')).toHaveLength(0)
    node.focus()
    fireEvent.keyDown(node, { key: 'Delete' })
    expect(readOnly.onRemove).not.toHaveBeenCalled()
  })

  it('calls onRemove on Delete', async () => {
    const editable = renderGraph()
    const node = await screen.findByTestId('studio-graph-node-1', undefined, slow)
    node.focus()
    fireEvent.keyDown(node, { key: 'Delete' })
    expect(editable.onRemove).toHaveBeenCalledOnce()
  })

  it('gives each transition its drop target', async () => {
    renderGraph()

    await screen.findByTestId('studio-graph-node-0', undefined, slow)
    expect(paths).toHaveLength(3)
    for (const path of paths) {
      expect(screen.getByTestId(`studio-process-edge-drop-${path}`)).toBeInTheDocument()
    }
  })

  it('selects a step by a click and a transition by Enter', async () => {
    const props = renderGraph()

    fireEvent.click(await screen.findByTestId('studio-graph-node-1', undefined, slow))
    expect(props.onSelect).toHaveBeenCalledWith(1)

    const edge = screen.getByTestId(`rf__edge-${paths[0]}`)
    fireEvent.keyDown(edge, { key: 'Enter' })
    expect(props.onSelectEdge).toHaveBeenCalledWith(graph.transitions[0])
  })
})
