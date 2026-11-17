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
  useReactFlow,
} from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import {
  ApartmentOutlined,
  BranchesOutlined,
  ClockCircleOutlined,
  FlagOutlined,
  HourglassOutlined,
  ThunderboltOutlined,
  UserOutlined,
} from '@ant-design/icons'
import {
  type ComponentType,
  createContext,
  type KeyboardEvent,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react'
import { ProcessGraphControls } from './ProcessGraphControls'
import { StatusTag, type StatusTone } from './StatusTag'
import { Tooltip } from 'antd'
import { useDesignTokens, useThemeMode } from './themeMode'

// Room for a label on two lines and the line under it, a status tag's 22 px: 8 + 4 + 2 × 20 + 2 + 22 + 8 px and the
// borders, so nothing is cut.
const nodeWidth = 272
const nodeHeight = 88
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
  /** The process's start step, which the layout places first on the left. */
  start: boolean
  tone: ProcessGraphTone
  ariaLabel: string
  ariaCurrent?: boolean
  /**
   * Proposed, see the review on #3650: the step's type as the process names it, such as `task` or `decision`, which
   * picks the icon the platform draws for it.
   */
  kind: string
  /** Proposed, see the review on #3650: the step's state on an instance, a `StatusTag` in its tone, and how long it lasted. */
  status?: { tone: StatusTone; text: string; detail?: string }
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

/** A step's place and size on the laid-out graph. */
interface Rect extends Point {
  width: number
  height: number
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
      const rectOf = (id: string | undefined) => {
        const place = id === undefined ? undefined : places.get(id)
        return place === undefined ? undefined : { ...place, width: nodeWidth, height: nodeHeight }
      }
      const points = section
        ? straighten(
            [section.startPoint, ...(section.bendPoints ?? []), section.endPoint],
            rectOf(edge.sources[0]),
            rectOf(edge.targets[0]),
          )
        : []
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

/** The shortest a bend of a transition may be: a shorter jog at either end is straightened. */
const shortestBend = 16

/**
 * Proposed, see the review on #3650: straightens a short jog at either end of a route, where ELK leaves a step at one
 * height and bends a few pixels into its lane: the end moves along the step's side to the lane's height, as long as
 * it stays on that side, so the bend goes and the line still touches no step.
 */
function straighten(points: Point[], from: Rect | undefined, to: Rect | undefined): Point[] {
  const route = points.map((point) => ({ ...point }))
  const onSide = (rect: Rect | undefined, y: number) =>
    rect !== undefined && y >= rect.y + 8 && y <= rect.y + rect.height - 8
  // [start, out, jog, ...]: the first part leaves the step level, then a short upright part reaches the lane.
  if (route.length >= 4 && route[0].y === route[1].y && route[1].x === route[2].x) {
    const jog = Math.abs(route[2].y - route[1].y)
    if (jog > 0 && jog < shortestBend && onSide(from, route[2].y)) {
      route[0].y = route[2].y
      route[1].y = route[2].y
    }
  }
  const n = route.length
  if (n >= 4 && route[n - 1].y === route[n - 2].y && route[n - 2].x === route[n - 3].x) {
    const jog = Math.abs(route[n - 3].y - route[n - 2].y)
    if (jog > 0 && jog < shortestBend && onSide(to, route[n - 3].y)) {
      route[n - 1].y = route[n - 3].y
      route[n - 2].y = route[n - 3].y
    }
  }
  // Points in a line with the ones either side of them, or the same as the one before, are no bends.
  return route.filter((point, index) => {
    const before = route[index - 1]
    const after = route[index + 1]
    if (before === undefined || after === undefined) {
      return true
    }
    if (before.x === point.x && before.y === point.y) {
      return false
    }
    return !((before.x === point.x && point.x === after.x) || (before.y === point.y && point.y === after.y))
  })
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
              // On the canvas's own colour, so the label cuts the line and sits on the graph's ground.
              background: colour.canvas,
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

/** The icon the platform draws for each type of step, as it picks the icons of what it knows. */
const stepIcons: Record<string, ComponentType> = {
  task: UserOutlined,
  decision: BranchesOutlined,
  operation: ThunderboltOutlined,
  subProcess: ApartmentOutlined,
  timer: ClockCircleOutlined,
  waitEvent: HourglassOutlined,
  end: FlagOutlined,
}

/**
 * Proposed, see the review on #3650: a step's node, a card on the surface: its type's icon in a tile, its label in
 * bold on up to two lines, and under it its state on an instance, a `StatusTag` and how long it lasted, or its type
 * in a designer. Its border says where an instance went: run, at, not reached or failed, or the step a designer edits.
 */
function StepNodeView({ data }: NodeProps<StepNode>) {
  const { colour, radius, spacing } = useDesignTokens()
  const found = useContext(FoundStep)
  const edge = {
    plain: colour.border,
    selected: colour.primary,
    error: colour.danger,
    current: colour.primary,
    taken: colour.successText,
    idle: colour.borderSubtle,
  }[data.tone]
  // One weight of emphasis: the step an instance is at, and the step Find a step shows, take 2 px in their own colour;
  // every other border is 1 px.
  const width = data.tone === 'current' || found === data.id ? 2 : 1
  const Icon = stepIcons[data.kind] ?? ThunderboltOutlined
  const muted = data.tone === 'idle'
  return (
    <>
      {/* Hidden, not removed, so React Flow still measures where a transition comes in. */}
      <Handle type="target" position={Position.Left} isConnectable={false} style={{ visibility: 'hidden' }} />
      <button
        type="button"
        data-testid={data.testId}
        aria-label={data.ariaLabel}
        aria-current={data.ariaCurrent ? 'true' : undefined}
        data-marked={data.tone === 'error' ? 'true' : undefined}
        data-tone={data.tone}
        style={{
          display: 'flex',
          alignItems: 'flex-start',
          gap: spacing.sm,
          width: '100%',
          height: '100%',
          padding: `${spacing.xs}px ${spacing.sm}px`,
          overflow: 'hidden',
          textAlign: 'left',
          font: 'inherit',
          color: muted ? colour.textMuted : colour.text,
          background: colour.surface,
          border: `${width}px solid ${edge}`,
          borderRadius: radius.base,
          cursor: 'pointer',
        }}
      >
        <Tooltip title={data.type}>
          <span
            aria-hidden
            style={{
              display: 'flex',
              flex: 'none',
              alignItems: 'center',
              justifyContent: 'center',
              width: 28,
              height: 28,
              borderRadius: radius.base,
              color: data.tone === 'current' || data.tone === 'selected' ? colour.primaryText : colour.textMuted,
              background: colour.canvas,
            }}
          >
            <Icon />
          </span>
        </Tooltip>
        {/* The label's first line sits level with the icon: 4 px down, half of the 28 px tile less its 20 px line. */}
        <span style={{ display: 'flex', flexDirection: 'column', gap: 2, minWidth: 0, paddingTop: 4 }}>
          <strong
            style={{
              display: '-webkit-box',
              WebkitLineClamp: 2,
              WebkitBoxOrient: 'vertical',
              overflow: 'hidden',
              lineHeight: '20px',
            }}
          >
            {data.name}
          </strong>
          {data.status ? (
            <span style={{ display: 'flex', alignItems: 'center', gap: spacing.xs, fontSize: 12, lineHeight: '20px' }}>
              <StatusTag tone={data.status.tone}>{data.status.text}</StatusTag>
              {data.status.detail !== undefined && (
                <span
                  style={{
                    color: colour.textMuted,
                    whiteSpace: 'nowrap',
                    overflow: 'hidden',
                    textOverflow: 'ellipsis',
                  }}
                >
                  {data.status.detail}
                </span>
              )}
            </span>
          ) : (
            <span style={{ fontSize: 12, lineHeight: '16px', color: colour.textMuted }}>
              {data.type}
              {data.note !== undefined && (
                <>
                  {' '}
                  <span data-layer-mark={data.note.kind}>{data.note.text}</span>
                </>
              )}
            </span>
          )}
        </span>
      </button>
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

/** The step Find a step shows, which its node marks while it stays chosen. */
const FoundStep = createContext<string | null>(null)

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
  // The step Find a step shows, marked as long as it stays chosen.
  const [found, setFound] = useState<string | null>(focus ?? null)
  const { colour, radius } = useDesignTokens()
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
      // A graph with controls takes the focus, so its keys work; a designer's keeps its own order of Tab.
      tabIndex={controls ? 0 : undefined}
      // The graph lies on the canvas colour, so its steps on the surface stand out with no pattern behind them, as
      // Camunda Operate and Figma draw a diagram; in full screen it fills the screen the same way.
      style={{ height, position: 'relative', background: colour.canvas, borderRadius: radius.base }}
      onKeyDown={onKeyDown}
    >
      <FoundStep.Provider value={found}>
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
          {controls && (
            <ProcessGraphControls
              area={area}
              focus={focus}
              steps={steps.map((step) => ({ id: step.id, label: step.name }))}
              onFound={setFound}
            />
          )}
        </ReactFlow>
      </FoundStep.Provider>
    </section>
  )
}
