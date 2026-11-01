import dagre from '@dagrejs/dagre'
import { type Edge, Handle, MarkerType, type Node, type NodeProps, Position, ReactFlow } from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { useMemo } from 'react'
import { useText } from '../platform/texts'
import { useDesignTokens, useThemeMode } from '../platform/themeMode'
import type { ContractEdge, ContractState } from './contractView'

const nodeWidth = 200
const nodeHeight = 64

type StateNodeData = { state: ContractState }

type StateNode = Node<StateNodeData, 'state'>

function placeStates(states: ContractState[], edges: ContractEdge[]): (name: string) => { x: number; y: number } {
  const layout = new dagre.graphlib.Graph()
  layout.setGraph({ rankdir: 'TB', nodesep: 40, ranksep: 80 })
  layout.setDefaultEdgeLabel(() => ({}))
  for (const state of states) {
    layout.setNode(state.name, { width: nodeWidth, height: nodeHeight })
  }
  for (const edge of edges) {
    if (layout.hasNode(edge.from) && layout.hasNode(edge.to)) {
      layout.setEdge(edge.from, edge.to)
    }
  }
  dagre.layout(layout)
  return (name) => {
    const placed = layout.node(name)
    return { x: placed.x - nodeWidth / 2, y: placed.y - nodeHeight / 2 }
  }
}

function StateNodeView({ data }: NodeProps<StateNode>) {
  const t = useText()
  const { colour, radius } = useDesignTokens()
  const { state } = data
  const mark = { padding: '0 6px', border: `1px solid ${colour.border}`, borderRadius: radius.base, fontSize: 12 }
  return (
    <>
      <Handle type="target" position={Position.Top} isConnectable={false} style={{ display: 'none' }} aria-hidden />
      <div
        data-testid={`studio-contract-state-${state.name}`}
        style={{
          width: '100%',
          height: '100%',
          padding: '8px',
          overflow: 'hidden',
          boxSizing: 'border-box',
          color: colour.text,
          background: colour.surface,
          border: `${state.initial ? 3 : 1}px solid ${state.initial ? colour.primary : colour.border}`,
          borderRadius: radius.base,
        }}
      >
        <strong>{state.name}</strong>
        <div style={{ display: 'flex', gap: 4, marginTop: 4 }}>
          {state.initial && (
            <span style={{ ...mark, color: colour.primaryText, borderColor: colour.primaryText }}>
              {t('shell.studio.contract.initial')}
            </span>
          )}
          {state.deletable && <span style={mark}>{t('shell.studio.contract.deletable')}</span>}
        </div>
      </div>
      <Handle type="source" position={Position.Bottom} isConnectable={false} style={{ display: 'none' }} aria-hidden />
    </>
  )
}

const nodeTypes = { state: StateNodeView }

/**
 * The lifecycle of a module contract as a state diagram. Each state is a node, with a thick border and an "Initial"
 * mark when a record may be created in it and a "Deletable" mark when a record may be deleted in it. Each arrow is a
 * transition from one of its `from` states, labelled with the transition's name and "Guard" or "No guard". The diagram
 * only shows: no node moves, connects or takes focus.
 */
export default function StudioContractLifecycle({ states, edges }: { states: ContractState[]; edges: ContractEdge[] }) {
  const t = useText()
  const { mode } = useThemeMode()
  const { colour } = useDesignTokens()
  const to = t('shell.studio.processGraph.to')
  const guard = t('shell.studio.contract.guard')
  const noGuard = t('shell.studio.contract.noGuard')

  const flow = useMemo(() => {
    const place = placeStates(states, edges)
    return {
      nodes: states.map((state): StateNode => ({
        id: state.name,
        type: 'state',
        position: place(state.name),
        width: nodeWidth,
        height: nodeHeight,
        handles: [
          { type: 'target', position: Position.Top, x: nodeWidth / 2, y: 0, width: 1, height: 1 },
          { type: 'source', position: Position.Bottom, x: nodeWidth / 2, y: nodeHeight, width: 1, height: 1 },
        ],
        data: { state },
      })),
      edges: edges
        .filter(
          (edge) => states.some((state) => state.name === edge.from) && states.some((state) => state.name === edge.to),
        )
        .map((edge): Edge => {
          const label = `${edge.name} · ${edge.guard ? guard : noGuard}`
          return {
            id: edge.id,
            source: edge.from,
            target: edge.to,
            label,
            ariaLabel: `${edge.from} ${to} ${edge.to} · ${label}`,
            style: { stroke: colour.textMuted },
            markerEnd: { type: MarkerType.ArrowClosed, color: colour.textMuted },
            labelStyle: { fill: colour.text },
            labelBgStyle: { fill: colour.surface },
          }
        }),
    }
  }, [states, edges, colour, to, guard, noGuard])

  return (
    <section
      className="studio-contract-lifecycle"
      data-testid="studio-contract-lifecycle"
      aria-label={t('shell.studio.contract.lifecycle')}
      style={{ height: 360 }}
    >
      {/* React Flow's attribution link is a fixed grey that misses the contrast of both modes, so it takes the tokens. */}
      <style>
        {`.studio-contract-lifecycle .react-flow__attribution { background: ${colour.surface}; }
.studio-contract-lifecycle .react-flow__attribution a { color: ${colour.textMuted}; }`}
      </style>
      <ReactFlow
        nodes={flow.nodes}
        edges={flow.edges}
        nodeTypes={nodeTypes}
        colorMode={mode}
        fitView
        nodesDraggable={false}
        nodesConnectable={false}
        nodesFocusable={false}
        elementsSelectable={false}
        edgesFocusable={false}
        zoomOnScroll={false}
      />
    </section>
  )
}
