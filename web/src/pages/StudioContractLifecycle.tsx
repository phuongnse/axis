import { type Edge, Handle, MarkerType, type Node, type NodeProps, Position, ReactFlow } from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { useMemo } from 'react'
import { fillText } from '../platform/fillText'
import { ProcessRouteEdge } from '../platform/ProcessGraph'
import { type GraphPoint, routeBetween, useGraphLayout } from '../platform/processLayout'
import { useText } from '../platform/texts'
import { useDesignTokens, useThemeMode } from '../platform/themeMode'
import type { ContractEdge, ContractState } from './contractView'

const nodeWidth = 200
const nodeHeight = 64

type StateNodeData = { state: ContractState }

type StateNode = Node<StateNodeData, 'state'>

type RouteEdge = Edge<{ route?: GraphPoint[] }, 'route'>

function StateNodeView({ data }: NodeProps<StateNode>) {
  const t = useText()
  const { colour, radius } = useDesignTokens()
  const { state } = data
  const mark = { padding: '0 6px', border: `1px solid ${colour.border}`, borderRadius: radius.base, fontSize: 12 }
  return (
    <>
      <Handle type="target" position={Position.Left} isConnectable={false} style={{ display: 'none' }} aria-hidden />
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
      <Handle type="source" position={Position.Right} isConnectable={false} style={{ display: 'none' }} aria-hidden />
    </>
  )
}

const nodeTypes = { state: StateNodeView }
const edgeTypes = { route: ProcessRouteEdge }

/**
 * The lifecycle of a module contract as a state diagram, laid out from left to right by the process graph's layout, see
 * docs/reference/process-inspector-widget.md#process-graph. Each state is a node, with a thick border and an "Initial"
 * mark when a record may be created in it and a "Deletable" mark when a record may be deleted in it. Each arrow is a
 * transition from one of its `from` states, labelled with the transition's name and "Guard" or "No guard". The diagram
 * only shows: no node moves, connects or takes focus.
 */
export default function StudioContractLifecycle({ states, edges }: { states: ContractState[]; edges: ContractEdge[] }) {
  const t = useText()
  const { mode } = useThemeMode()
  const { colour } = useDesignTokens()
  const edgeLabelText = t('shell.studio.contract.edgeLabel')
  const edgeNameText = t('shell.studio.contract.edgeName')
  const guard = t('shell.studio.contract.guard')
  const noGuard = t('shell.studio.contract.noGuard')

  const layout = useGraphLayout({
    nodes: states.map((state) => ({ id: state.name, width: nodeWidth, height: nodeHeight })),
    edges: edges.map((edge) => ({ id: edge.id, source: edge.from, target: edge.to })),
  })

  const flow = useMemo(() => {
    if (layout === null) {
      return null
    }
    return {
      nodes: states
        .filter((state) => layout.positions.has(state.name))
        .map((state): StateNode => ({
          id: state.name,
          type: 'state',
          position: layout.positions.get(state.name)!,
          width: nodeWidth,
          height: nodeHeight,
          handles: [
            { type: 'target', position: Position.Left, x: 0, y: nodeHeight / 2, width: 1, height: 1 },
            { type: 'source', position: Position.Right, x: nodeWidth, y: nodeHeight / 2, width: 1, height: 1 },
          ],
          data: { state },
        })),
      edges: edges
        .filter((edge) => layout.positions.has(edge.from) && layout.positions.has(edge.to))
        .map((edge): RouteEdge => {
          const label = fillText(edgeLabelText, { name: edge.name, guard: edge.guard ? guard : noGuard })
          return {
            id: edge.id,
            source: edge.from,
            target: edge.to,
            type: 'route',
            data: { route: routeBetween(layout, edge.id, edge.from, edge.to) },
            label,
            ariaLabel: fillText(edgeNameText, { source: edge.from, target: edge.to, label }),
            style: { stroke: colour.textMuted },
            markerEnd: { type: MarkerType.ArrowClosed, color: colour.textMuted },
            labelStyle: { fill: colour.text },
            labelBgStyle: { fill: colour.surface },
          }
        }),
    }
  }, [layout, states, edges, colour, edgeLabelText, edgeNameText, guard, noGuard])

  return (
    <section
      className="studio-contract-lifecycle"
      data-testid="studio-contract-lifecycle"
      aria-label={t('shell.studio.contract.lifecycle')}
      style={{ height: 360 }}
    >
      {flow && (
        <ReactFlow
          nodes={flow.nodes}
          edges={flow.edges}
          nodeTypes={nodeTypes}
          edgeTypes={edgeTypes}
          colorMode={mode}
          fitView
          proOptions={{ hideAttribution: true }}
          nodesDraggable={false}
          nodesConnectable={false}
          nodesFocusable={false}
          elementsSelectable={false}
          edgesFocusable={false}
          zoomOnScroll={false}
        />
      )}
    </section>
  )
}
