import dagre from '@dagrejs/dagre'
import { type Edge, Handle, MarkerType, type Node, type NodeProps, Position, ReactFlow } from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { theme } from 'antd'
import { useMemo } from 'react'
import { useText } from '../platform/texts'
import { useThemeMode } from '../platform/themeMode'
import { buildProcessGraph, type ProcessGraph } from './processGraph'

const nodeWidth = 200
const nodeHeight = 48

type StepNodeData = {
  index: number
  name: string
  type: string
  start: boolean
  current: boolean
  onOpen: (path: string) => void
}

type StepNode = Node<StepNodeData, 'step'>

function placeSteps(graph: ProcessGraph): (index: number) => { x: number; y: number } {
  const layout = new dagre.graphlib.Graph()
  layout.setGraph({ rankdir: 'TB', nodesep: 40, ranksep: 56 })
  layout.setDefaultEdgeLabel(() => ({}))
  for (const step of graph.steps) {
    layout.setNode(String(step.index), { width: nodeWidth, height: nodeHeight })
  }
  for (const transition of graph.transitions) {
    layout.setEdge(String(transition.source), String(transition.target))
  }
  dagre.layout(layout)
  return (index) => {
    const placed = layout.node(String(index))
    return { x: placed.x - nodeWidth / 2, y: placed.y - nodeHeight / 2 }
  }
}

function StepNodeView({ data }: NodeProps<StepNode>) {
  const { token } = theme.useToken()
  return (
    <>
      <Handle type="target" position={Position.Top} isConnectable={false} />
      <button
        type="button"
        data-testid={`studio-graph-node-${data.index}`}
        aria-label={`${data.name} · ${data.type}`}
        aria-current={data.current ? 'true' : undefined}
        onClick={() => data.onOpen(`/steps/${data.index}`)}
        style={{
          width: '100%',
          height: '100%',
          padding: '0 8px',
          overflow: 'hidden',
          textAlign: 'left',
          textOverflow: 'ellipsis',
          whiteSpace: 'nowrap',
          font: 'inherit',
          color: token.colorText,
          background: token.colorBgContainer,
          border: `${data.start ? 3 : 1}px solid ${data.current ? token.colorPrimary : token.colorBorder}`,
          borderRadius: token.borderRadius,
          cursor: 'pointer',
        }}
      >
        <strong>{data.name}</strong> <span>{data.type}</span>
      </button>
      <Handle type="source" position={Position.Bottom} isConnectable={false} />
    </>
  )
}

const nodeTypes = { step: StepNodeView }

/** A read-only graph of the draft's steps and transitions: a node opens its step, an edge opens its transition. */
export default function StudioProcessGraph({
  draft,
  path,
  diagnosticPaths,
  onOpenPath,
}: {
  draft: Record<string, unknown>
  path: string | null
  diagnosticPaths: readonly string[]
  onOpenPath: (path: string) => void
}) {
  const t = useText()
  const { mode } = useThemeMode()
  const { token } = theme.useToken()

  const { nodes, edges } = useMemo(() => {
    const graph = buildProcessGraph(draft, diagnosticPaths)
    const place = placeSteps(graph)
    return {
      nodes: graph.steps.map((step): StepNode => ({
        id: String(step.index),
        type: 'step',
        position: place(step.index),
        width: nodeWidth,
        height: nodeHeight,
        handles: [
          { type: 'target', position: Position.Top, x: nodeWidth / 2, y: 0, width: 1, height: 1 },
          { type: 'source', position: Position.Bottom, x: nodeWidth / 2, y: nodeHeight, width: 1, height: 1 },
        ],
        data: {
          index: step.index,
          name: step.name ?? '',
          type: step.type ?? '',
          start: graph.start === step.index,
          current: path === `/steps/${step.index}` || (path?.startsWith(`/steps/${step.index}/`) ?? false),
          onOpen: onOpenPath,
        },
      })),
      edges: graph.transitions.map((transition): Edge => ({
        id: transition.path,
        source: String(transition.source),
        target: String(transition.target),
        label: transition.label ?? transition.kind,
        style: { stroke: token.colorTextTertiary },
        markerEnd: { type: MarkerType.ArrowClosed, color: token.colorTextTertiary },
        labelStyle: { fill: token.colorText },
        labelBgStyle: { fill: token.colorBgContainer },
      })),
    }
  }, [draft, diagnosticPaths, path, onOpenPath, token])

  return (
    <section data-testid="studio-process-graph" aria-label={t('shell.studio.processGraph')} style={{ height: 320 }}>
      <ReactFlow
        nodes={nodes}
        edges={edges}
        nodeTypes={nodeTypes}
        colorMode={mode}
        fitView
        nodesDraggable={false}
        nodesConnectable={false}
        nodesFocusable={false}
        elementsSelectable={false}
        edgesFocusable={false}
        zoomOnScroll={false}
        onEdgeClick={(_, edge) => onOpenPath(edge.id)}
      />
    </section>
  )
}
