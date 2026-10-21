import { render, screen } from '@testing-library/react'
import { type Edge, type Node, ReactFlow } from '@xyflow/react'
import { expect, it } from 'vitest'

const nodes: Node[] = [
  { id: 'a', position: { x: 0, y: 0 }, data: { label: 'Start' }, width: 150, height: 40 },
  { id: 'b', position: { x: 0, y: 100 }, data: { label: 'End' }, width: 150, height: 40 },
]

const edges: Edge[] = [{ id: 'a-b', source: 'a', target: 'b' }]

it('renders a two-node React Flow graph under jsdom', () => {
  render(
    <div style={{ width: 800, height: 600 }}>
      <ReactFlow nodes={nodes} edges={edges} />
    </div>,
  )

  expect(screen.getByTestId('rf__node-a')).toBeInTheDocument()
  expect(screen.getByTestId('rf__node-b')).toBeInTheDocument()
  expect(screen.getByText('Start')).toBeInTheDocument()
  expect(screen.getByText('End')).toBeInTheDocument()
})
