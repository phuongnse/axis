import ELK, { type ElkExtendedEdge, type ElkNode } from 'elkjs/lib/elk.bundled.js'
import {
  BaseEdge,
  type Edge,
  EdgeLabelRenderer,
  type EdgeProps,
  Handle,
  MarkerType,
  type Node,
  type NodeProps,
  type OnConnectEnd,
  Position,
  ReactFlow,
  Background,
  BackgroundVariant,
  useReactFlow,
} from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { type ComponentType, type KeyboardEvent, useEffect, useMemo, useRef, useState } from 'react'
import { ProcessGraphControls } from './ProcessGraphControls'
import { useDesignTokens, useThemeMode } from './themeMode'

const nodeWidth = 240
const nodeHeight = 56
const handleSize = 10

/**
 * How a step's node is drawn. `selected` is the step a designer edits, in the primary colour; `error` has a diagnostic,
 * in the danger colour; `current` is the step an instance is at, in the primary colour and twice as thick; `taken` is a
 * step an instance has run, in the success text colour; `idle` is a step an instance has not reached, its border and
 * text muted; `plain` is any other.
 */
export type ProcessGraphTone = 'plain' | 'selected' | 'error' | 'current' | 'taken' | 'idle'

/** One step of a `ProcessGraphCanvas`: its node's id, its texts, its look and its accessible name. */
export type ProcessGraphStep = {
  id: string
  name: string
  type: string
  /** The process's start step, drawn with a thick border. */
  start: boolean
  tone: ProcessGraphTone
  ariaLabel: string
  ariaCurrent?: boolean
  /** Proposed, see the review on #3650: a count drawn at the node's corner, such as how many times an instance ran it. */
  badge?: string
  /** A mark shown after the type, such as a customization layer's Added, and its value for `data-layer-mark`. */
  note?: { text: string; kind: string }
  /** Whether a transition can be drawn from the step's bottom handle. */
  connectable?: boolean
  testId?: string
  handleTestId?: string
}

/** One transition of a `ProcessGraphCanvas`, between two steps' ids. */
export interface ProcessGraphTransitionView {
  id: string
  source: string
  target: string
  label: string
  ariaLabel: string
  tone: Exclude<ProcessGraphTone, 'error'>
  ariaCurrent?: boolean
}

type StepNodeData = ProcessGraphStep
type StepNode = Node<StepNodeData, 'step'>

/** A point of a laid-out graph, in its own coordinates. */
interface Point {
  x: number
  y: number
}

/** Where ELK put each step and how it routed each transition: its points, and its label's centre. */
interface Layout {
  places: Map<string, Point>
  routes: Map<string, { points: Point[]; label: Point | null }>
}

const elk = new ELK()

/** The width a label of `text` takes at the 12px of an edge label, with room on either side. */
const labelWidth = (text: string) => Math.ceil(text.length * 7 + 8)

/**
 * Proposed, see the review on #3650: lays a process out from left to right, as process diagrams read, with ELK's
 * layered algorithm, which places the steps in the order the process gives them and routes each transition in right
 * angles around every step, never through one, a transition back to an earlier step around the others, with each
 * label beside its line and clear of the others.
 */
async function layOut(
  steps: readonly ProcessGraphStep[],
  transitions: readonly ProcessGraphTransitionView[],
): Promise<Layout> {
  const graph: ElkNode = {
    id: 'process',
    layoutOptions: {
      'elk.algorithm': 'layered',
      'elk.direction': 'RIGHT',
      'elk.edgeRouting': 'ORTHOGONAL',
      'elk.layered.considerModelOrder.strategy': 'NODES_AND_EDGES',
      'elk.layered.cycleBreaking.strategy': 'MODEL_ORDER',
      'elk.layered.nodePlacement.strategy': 'NETWORK_SIMPLEX',
      'elk.layered.spacing.nodeNodeBetweenLayers': '72',
      'elk.spacing.nodeNode': '40',
      'elk.layered.spacing.edgeNodeBetweenLayers': '24',
      'elk.spacing.edgeNode': '24',
      'elk.spacing.edgeEdge': '16',
      'elk.layered.spacing.edgeEdgeBetweenLayers': '16',
      'elk.spacing.edgeLabel': '4',
      'elk.edgeLabels.inline': 'false',
    },
    children: steps.map((step) => ({ id: step.id, width: nodeWidth, height: nodeHeight })),
    edges: transitions
      .filter(
        (transition) =>
          steps.some((step) => step.id === transition.source) && steps.some((step) => step.id === transition.target),
      )
      .map((transition): ElkExtendedEdge => ({
        id: transition.id,
        sources: [transition.source],
        targets: [transition.target],
        labels: transition.label ? [{ text: transition.label, width: labelWidth(transition.label), height: 16 }] : [],
      })),
  }
  const laid = await elk.layout(graph)
  const places = new Map((laid.children ?? []).map((child) => [child.id, { x: child.x ?? 0, y: child.y ?? 0 }]))
  const routes = new Map(
    (laid.edges ?? []).map((edge) => {
      const section = edge.sections?.[0]
      const points = section ? [section.startPoint, ...(section.bendPoints ?? []), section.endPoint] : []
      const label = edge.labels?.[0]
      return [
        edge.id,
        {
          points,
          label:
            label?.x === undefined || label.y === undefined
              ? null
              : { x: label.x + (label.width ?? 0) / 2, y: label.y + (label.height ?? 0) / 2 },
        },
      ]
    }),
  )
  return { places, routes }
}

/** A path through `points` in straight lines, each corner rounded by up to `radius`. */
function roundedPath(points: readonly Point[], radius: number): string {
  if (points.length === 0) {
    return ''
  }
  let path = `M ${points[0].x} ${points[0].y}`
  for (let index = 1; index < points.length - 1; index += 1) {
    const [before, corner, after] = [points[index - 1], points[index], points[index + 1]]
    const inLength = Math.hypot(corner.x - before.x, corner.y - before.y)
    const outLength = Math.hypot(after.x - corner.x, after.y - corner.y)
    const r = Math.min(radius, inLength / 2, outLength / 2)
    const from = {
      x: corner.x - ((corner.x - before.x) / inLength) * r,
      y: corner.y - ((corner.y - before.y) / inLength) * r,
    }
    const to = {
      x: corner.x + ((after.x - corner.x) / outLength) * r,
      y: corner.y + ((after.y - corner.y) / outLength) * r,
    }
    path += ` L ${from.x} ${from.y} Q ${corner.x} ${corner.y} ${to.x} ${to.y}`
  }
  const last = points[points.length - 1]
  return `${path} L ${last.x} ${last.y}`
}

/** The middle of the longest straight part of a route, where something placed on the line sits clear of the steps. */
function middleOf(points: readonly Point[]): Point {
  let best = { length: -1, at: points[0] ?? { x: 0, y: 0 } }
  for (let index = 1; index < points.length; index += 1) {
    const [a, b] = [points[index - 1], points[index]]
    const length = Math.hypot(b.x - a.x, b.y - a.y)
    if (length > best.length) {
      best = { length, at: { x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 } }
    }
  }
  return best.at
}

/** What a routed transition carries: its points, its label and what a designer draws at its middle. */
type RouteData = {
  points: Point[]
  text: string
  label: Point | null
  Adornment?: ComponentType<{ id: string; x: number; y: number }>
}

/**
 * A transition drawn along the route ELK gave it, in right angles with rounded corners, its label beside it on the
 * surface, and what a designer adds at its middle, such as a drop target.
 */
function RoutedEdge({ id, data, markerEnd, style, interactionWidth }: EdgeProps<Edge<RouteData, 'routed'>>) {
  const { colour } = useDesignTokens()
  if (!data || data.points.length === 0) {
    return null
  }
  const middle = middleOf(data.points)
  const { Adornment } = data
  return (
    <>
      <BaseEdge
        id={id}
        path={roundedPath(data.points, 8)}
        markerEnd={markerEnd}
        style={style}
        interactionWidth={interactionWidth}
      />
      {data.label !== null && data.text !== '' && (
        <EdgeLabelRenderer>
          <div
            aria-hidden
            style={{
              position: 'absolute',
              transform: `translate(-50%, -50%) translate(${data.label.x}px, ${data.label.y}px)`,
              padding: '0 4px',
              fontSize: 12,
              lineHeight: '16px',
              whiteSpace: 'nowrap',
              color: colour.text,
              background: colour.surface,
              pointerEvents: 'none',
            }}
          >
            {data.text}
          </div>
        </EdgeLabelRenderer>
      )}
      {Adornment && <Adornment id={id} x={middle.x} y={middle.y} />}
    </>
  )
}

const edgeTypes = { routed: RoutedEdge }

/** Fits the view each time the layout changes, on the focused step or on the whole graph. */
function FitOnLayout({ layout, focus }: { layout: Layout | null; focus?: string }) {
  const { fitView } = useReactFlow()
  useEffect(() => {
    if (layout === null) {
      return
    }
    const frame = requestAnimationFrame(() => {
      void fitView(focus === undefined ? undefined : { nodes: [{ id: focus }], minZoom: 1, maxZoom: 1 })
    })
    return () => cancelAnimationFrame(frame)
  }, [layout, focus, fitView])
  return null
}

function StepNodeView({ data }: NodeProps<StepNode>) {
  const { colour, radius } = useDesignTokens()
  const edge = {
    plain: colour.border,
    selected: colour.primary,
    error: colour.danger,
    current: colour.primary,
    taken: colour.successText,
    idle: colour.borderSubtle,
  }[data.tone]
  const width = data.start || data.tone === 'current' ? 3 : 1
  return (
    <>
      <Handle type="target" position={Position.Left} isConnectable={false} />
      <button
        type="button"
        data-testid={data.testId}
        aria-label={data.ariaLabel}
        aria-current={data.ariaCurrent ? 'true' : undefined}
        data-marked={data.tone === 'error' ? 'true' : undefined}
        data-tone={data.tone}
        style={{
          width: '100%',
          height: '100%',
          padding: '8px',
          overflow: 'hidden',
          textAlign: 'left',
          // A long name takes two lines before it is cut, so a step's label reads whole.
          display: '-webkit-box',
          WebkitLineClamp: 2,
          WebkitBoxOrient: 'vertical',
          font: 'inherit',
          lineHeight: '20px',
          color: data.tone === 'idle' ? colour.textMuted : colour.text,
          background: colour.surface,
          border: `${width}px solid ${edge}`,
          borderRadius: radius.base,
          cursor: 'pointer',
        }}
      >
        <strong>{data.name}</strong> <span>{data.type}</span>
        {data.note !== undefined && (
          <>
            {' '}
            <span data-layer-mark={data.note.kind}>{data.note.text}</span>
          </>
        )}
      </button>
      {/* Outside the button, whose overflow is hidden, so the count overlaps the node's corner. */}
      {data.badge !== undefined && (
        <span
          aria-hidden
          style={{
            position: 'absolute',
            top: -10,
            right: -10,
            minWidth: 20,
            padding: '0 6px',
            borderRadius: 10,
            fontSize: 12,
            lineHeight: '20px',
            textAlign: 'center',
            color: colour.onPrimary,
            background: colour.primary,
          }}
        >
          {data.badge}
        </span>
      )}
      <Handle
        type="source"
        position={Position.Right}
        isConnectable={data.connectable === true}
        data-testid={data.handleTestId}
        aria-hidden
        style={{
          width: handleSize,
          height: handleSize,
          background: colour.primary,
          border: `1px solid ${colour.surface}`,
          cursor: data.connectable ? 'crosshair' : 'default',
          // Hidden, not removed, so React Flow still measures where a transition leaves the step.
          visibility: data.connectable ? undefined : 'hidden',
        }}
      />
    </>
  )
}

const nodeTypes = { step: StepNodeView }

/**
 * Proposed, see the review on #3650: the one drawing of a process's steps and transitions, React Flow laid out by ELK
 * from left to right, which the process designer edits and a process instance's page shows. It stores no position:
 * every change lays the graph out again, so a transition never crosses a step, in a view or while a designer edits.
 * Each step is a node named by its accessible name, its look by its tone, and each transition an edge along its route,
 * named by its two steps and its branch or outcome, which Tab reaches before the nodes. A designer passes the
 * `Adornment` it draws at each transition's middle, such as a drop target, and the handlers that edit; without them
 * the graph only shows the process, and a click on a node or an edge does nothing.
 */
export function ProcessGraphCanvas({
  steps,
  transitions,
  ariaLabel,
  testId,
  height = 480,
  Adornment,
  sectionRef,
  onKeyDown,
  onStepClick,
  onTransitionClick,
  onConnectEnd,
  focus,
  controls = false,
}: {
  steps: readonly ProcessGraphStep[]
  transitions: readonly ProcessGraphTransitionView[]
  ariaLabel: string
  testId?: string
  height?: number
  /** What a designer draws at the middle of each transition, such as the drop target of its palette. */
  Adornment?: ComponentType<{ id: string; x: number; y: number }>
  sectionRef?: (element: HTMLElement | null) => void
  onKeyDown?: (event: KeyboardEvent<HTMLElement>) => void
  onStepClick?: (id: string) => void
  onTransitionClick?: (id: string) => void
  onConnectEnd?: OnConnectEnd
  /** Proposed, see the review on #3650: the step the view opens on, at the size it is drawn at, else the whole graph. */
  focus?: string
  /** Proposed, see the review on #3650: Zoom in, Zoom out, Show the whole process and Full screen, for a graph too large to read whole. */
  controls?: boolean
}) {
  const { mode } = useThemeMode()
  const area = useRef<HTMLElement | null>(null)
  const { colour } = useDesignTokens()
  const [layout, setLayout] = useState<Layout | null>(null)

  useEffect(() => {
    let current = true
    void layOut(steps, transitions).then((next) => {
      if (current) {
        setLayout(next)
      }
    })
    return () => {
      current = false
    }
  }, [steps, transitions])

  const { nodes, edges } = useMemo(() => {
    if (layout === null) {
      return { nodes: [], edges: [] }
    }
    return {
      nodes: steps.map((step): StepNode => ({
        id: step.id,
        type: 'step',
        position: layout.places.get(step.id) ?? { x: 0, y: 0 },
        width: nodeWidth,
        height: nodeHeight,
        handles: [
          { type: 'target', position: Position.Left, x: 0, y: nodeHeight / 2, width: 1, height: 1 },
          {
            type: 'source',
            position: Position.Right,
            x: nodeWidth - handleSize / 2,
            y: nodeHeight / 2 - handleSize / 2,
            width: handleSize,
            height: handleSize,
          },
        ],
        data: step,
      })),
      edges: transitions.map((transition): Edge<RouteData, 'routed'> => {
        const stroke =
          transition.tone === 'selected' || transition.tone === 'current'
            ? colour.primary
            : transition.tone === 'taken'
              ? colour.successText
              : colour.textMuted
        const thick = transition.tone !== 'plain' && transition.tone !== 'idle'
        const route = layout.routes.get(transition.id)
        return {
          id: transition.id,
          type: 'routed',
          source: transition.source,
          target: transition.target,
          data: { points: route?.points ?? [], text: transition.label, label: route?.label ?? null, Adornment },
          ariaLabel: transition.ariaLabel,
          ariaRole: onTransitionClick ? 'button' : undefined,
          domAttributes: transition.ariaCurrent ? { 'aria-current': 'true' } : undefined,
          style: {
            stroke,
            strokeWidth: thick ? 2 : undefined,
            strokeDasharray: transition.tone === 'idle' ? '4 4' : undefined,
          },
          markerEnd: { type: MarkerType.ArrowClosed, color: stroke },
        }
      }),
    }
  }, [layout, steps, transitions, colour, Adornment, onTransitionClick])

  return (
    <section
      ref={(element) => {
        area.current = element
        sectionRef?.(element)
      }}
      data-testid={testId}
      aria-label={ariaLabel}
      // In full screen the graph fills the screen on the page's surface.
      style={{ height, position: 'relative', background: colour.surface }}
      onKeyDown={onKeyDown}
    >
      <ReactFlow
        nodes={nodes}
        edges={edges}
        nodeTypes={nodeTypes}
        edgeTypes={edgeTypes}
        colorMode={mode}
        minZoom={0.1}
        // A small process is drawn at its own size, never zoomed in past it, so its text keeps the body size.
        maxZoom={1}
        // React Flow is MIT licensed; its attribution may go, as Axis draws its own controls and overview.
        proOptions={{ hideAttribution: true }}
        nodesDraggable={false}
        nodesConnectable={onConnectEnd !== undefined}
        nodesFocusable={false}
        elementsSelectable={false}
        edgesFocusable
        deleteKeyCode={null}
        zoomOnScroll={false}
        onConnectEnd={onConnectEnd}
        onNodeClick={onStepClick ? (_, node) => onStepClick(node.id) : undefined}
        onEdgeClick={onTransitionClick ? (_, edge) => onTransitionClick(edge.id) : undefined}
      >
        <FitOnLayout layout={layout} focus={focus} />
        {/* A dot grid under the graph, in the subtle border colour, as a canvas has. */}
        <Background variant={BackgroundVariant.Dots} gap={16} size={1} color={colour.borderSubtle} />
        {controls && <ProcessGraphControls area={area} focus={focus} />}
      </ReactFlow>
    </section>
  )
}
