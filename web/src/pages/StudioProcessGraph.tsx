import '@xyflow/react/dist/style.css'
import dagre from '@dagrejs/dagre'
import {
  Handle,
  MarkerType,
  Position,
  ReactFlow,
  type Edge,
  type Node as FlowNode,
  type NodeProps,
} from '@xyflow/react'
import { theme } from 'antd'
import { useContext, useMemo } from 'react'
import { ThemeModeContext } from '../platform/themeMode'
import { useText } from '../platform/texts'
import type { ProcessGraph, ProcessGraphStep } from './processGraph'

const nodeWidth = 200
const nodeHeight = 44

type StepNodeData = {
  step: ProcessGraphStep
  current: boolean
  onOpen: (path: string) => void
}

type StepFlowNode = FlowNode<StepNodeData, 'step'>

/** One step as a button with its name and type. The hidden handles only let React Flow draw the edges. */
function StepNode({ data }: NodeProps<StepFlowNode>) {
  const t = useText()
  const { token } = theme.useToken()
  const { step, current, onOpen } = data
  const name = step.name ?? `/steps/${step.index}`
  const type = step.type ?? '?'
  const label = step.marked ? `${name} (${type}), ${t('shell.studio.graphStepHasDiagnostics')}` : `${name} (${type})`

  return (
    <>
      <Handle type="target" position={Position.Top} isConnectable={false} style={{ opacity: 0 }} />
      <button
        type="button"
        data-testid={`studio-graph-node-${step.index}`}
        aria-label={label}
        aria-current={current ? 'true' : undefined}
        data-marked={step.marked ? 'true' : undefined}
        onClick={() => onOpen(`/steps/${step.index}`)}
        style={{
          width: '100%',
          height: '100%',
          boxSizing: 'border-box',
          padding: '0 12px',
          overflow: 'hidden',
          textOverflow: 'ellipsis',
          whiteSpace: 'nowrap',
          textAlign: 'left',
          cursor: 'pointer',
          background: token.colorBgContainer,
          color: token.colorText,
          border: step.marked ? `2px solid ${token.colorError}` : `1px solid ${token.colorBorder}`,
          borderRadius: token.borderRadius,
          boxShadow: current ? `0 0 0 2px ${token.colorPrimary}` : undefined,
          font: 'inherit',
        }}
      >
        <strong>{name}</strong> <span style={{ color: token.colorTextSecondary }}>{type}</span>
      </button>
      <Handle type="source" position={Position.Bottom} isConnectable={false} style={{ opacity: 0 }} />
    </>
  )
}

const nodeTypes = { step: StepNode }

function isOnPath(openPath: string | null, index: number): boolean {
  return openPath !== null && (openPath === `/steps/${index}` || openPath.startsWith(`/steps/${index}/`))
}

/** The centre of each step's node, laid out top to bottom by dagre. */
function layoutCentres(graph: ProcessGraph): Map<number, { x: number; y: number }> {
  const layout = new dagre.graphlib.Graph({ multigraph: true })
  layout.setGraph({ rankdir: 'TB', nodesep: 40, ranksep: 60 })
  layout.setDefaultEdgeLabel(() => ({}))
  for (const step of graph.steps) {
    layout.setNode(String(step.index), { width: nodeWidth, height: nodeHeight })
  }
  for (const transition of graph.transitions) {
    layout.setEdge(String(transition.source), String(transition.target), {}, transition.path)
  }
  dagre.layout(layout)
  return new Map(
    graph.steps.map((step) => {
      const { x, y } = layout.node(String(step.index))
      return [step.index, { x, y }]
    }),
  )
}

/**
 * The read-only graph of a process's steps and transitions, drawn above its editor. Nodes are steps, in file order,
 * so Tab moves through them in that order. Selecting a node or an edge calls `onOpen` with its path, as a diagnostic
 * does. The node of the open path has `aria-current`. Its diagnostics mark a step, and the layout places every node.
 */
export function StudioProcessGraph({
  graph,
  openPath,
  onOpen,
}: {
  graph: ProcessGraph
  openPath: string | null
  onOpen: (path: string) => void
}) {
  const t = useText()
  const { token } = theme.useToken()
  const mode = useContext(ThemeModeContext)?.mode ?? 'light'

  const centres = useMemo(() => layoutCentres(graph), [graph])
  const nodes = useMemo<StepFlowNode[]>(
    () =>
      graph.steps.map((step) => {
        const centre = centres.get(step.index)!
        return {
          id: String(step.index),
          type: 'step',
          position: { x: centre.x - nodeWidth / 2, y: centre.y - nodeHeight / 2 },
          width: nodeWidth,
          height: nodeHeight,
          data: { step, current: isOnPath(openPath, step.index), onOpen },
        }
      }),
    [graph, centres, openPath, onOpen],
  )
  const edges = useMemo<Edge[]>(
    () =>
      graph.transitions.map((transition) => ({
        id: transition.path,
        source: String(transition.source),
        target: String(transition.target),
        label: transition.label ?? transition.kind,
        markerEnd: { type: MarkerType.ArrowClosed },
        style: { stroke: token.colorTextSecondary },
        labelStyle: { fill: token.colorText },
        labelBgStyle: { fill: token.colorBgContainer },
      })),
    [graph, token],
  )

  return (
    <section aria-label={t('shell.studio.processGraph')} data-testid="studio-process-graph" style={{ height: 360 }}>
      <ReactFlow
        nodes={nodes}
        edges={edges}
        nodeTypes={nodeTypes}
        fitView
        nodesDraggable={false}
        nodesConnectable={false}
        elementsSelectable={false}
        nodesFocusable={false}
        edgesFocusable={false}
        disableKeyboardA11y
        colorMode={mode}
        onEdgeClick={(_, edge) => onOpen(edge.id)}
      />
    </section>
  )
}
