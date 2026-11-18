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
  CheckCircleFilled,
  ClockCircleFilled,
  ClockCircleOutlined,
  CloseCircleFilled,
  ExclamationCircleFilled,
  MinusCircleFilled,
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
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
} from 'react'
import { ProcessGraphControls } from './ProcessGraphControls'
import type { StatusTone } from './StatusTag'
import { Tooltip, Typography } from 'antd'
import { typeTokens } from './designTokens'
import { useDesignTokens, useThemeMode } from './themeMode'

const nodeWidth = 216
/**
 * Proposed, see the review on #3650: a card is as tall as it is drawn: its label takes one line or two, a longer one
 * ending in an ellipsis with its tooltip and accessible name holding it whole. The graph draws each card hidden, once
 * the label's font has loaded, measures it, and lays out with that height, as React Flow lays out with ELK; a card that
 * changes size later, as when a font arrives late, lays the graph out again. `fallbackHeight` stands in before then.
 */
const fallbackHeight = 52
/** The label's type, which the card draws it in and the measure uses, so both wrap alike. */
const labelType = { size: 14, weight: 600, lineHeight: 20 }
/**
 * Loads the face the labels are drawn in before anything is measured: a browser loads a face only when text needs it,
 * so `document.fonts.ready` alone may settle first. A face that fails to load leaves the fallback, which the cards and
 * the measure then share.
 */
async function labelFontLoaded(): Promise<void> {
  try {
    await document.fonts?.load(`${labelType.weight} ${labelType.size}px ${typeTokens.fontFamily}`)
  } catch {
    // The fallback font draws and measures alike.
  }
}

const handleSize = 10

/**
 * How a step's node is drawn. `selected` is the step a designer edits, with a 2 px `primary` border; `error` has a
 * diagnostic in a designer, with a `danger` border, or failed on an instance; `current` is the step an instance is at;
 * `taken` is a step an instance has run; `idle` is a step an instance has not reached, its text muted; `plain` is any
 * other. On an instance a step's state is its corner's icon, and its border is `borderSubtle` unless it is chosen.
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
  /** Proposed, see D155 and the review on #3650: the frame the step is drawn in, such as a branch of a block. */
  parent?: string
}

/**
 * Proposed, see D155 and the review on #3650: a frame the graph draws round steps, such as a `parallel` block, which
 * transitions enter and leave as a whole, or one of its branches inside it, named by `label`, with `note` beside it,
 * such as the block's rule of completion. A frame may sit in another one.
 */
export interface ProcessGraphFrame {
  id: string
  label: string
  note?: string
  kind: 'block' | 'branch'
  parent?: string
  testId?: string
}

/** One transition of a `ProcessGraphCanvas`, between two steps' or frames' ids. */
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
type FrameNode = Node<ProcessGraphFrame & Record<string, unknown>, 'frame'>

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
  /** Each step's and frame's place, inside the frame it sits in, as React Flow places a child node. */
  places: Map<string, Point>
  /** Each frame's size, which ELK gives it round what it holds. */
  sizes: Map<string, { width: number; height: number }>
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
  frames: readonly ProcessGraphFrame[],
  heights: ReadonlyMap<string, number>,
): Promise<Layout> {
  // Proposed, see D155: a frame is a node that holds its steps and frames, laid out inside it, and every transition is
  // routed across them, with its points in the graph's own coordinates.
  const nodes = new Map<string, ElkNode>()
  for (const frame of frames) {
    nodes.set(frame.id, {
      id: frame.id,
      layoutOptions: {
        'elk.padding':
          frame.kind === 'block' ? '[top=40,left=16,bottom=16,right=16]' : '[top=32,left=12,bottom=12,right=12]',
        // A block's branches have no transitions between them, so they stand in one column, one under the other.
        'elk.direction': 'RIGHT',
      },
      children: [],
    })
  }
  const roots: ElkNode[] = []
  const placed = new Set<string>()
  const parentOf = new Map(frames.map((frame) => [frame.id, frame.parent]))
  // A node goes into its frame, and a frame into its own, the first time a step inside it comes, so everything keeps
  // the order the process gives it, which the layout follows from left to right.
  const place = (node: ElkNode, parent: string | undefined) => {
    if (parent !== undefined && nodes.has(parent) && !placed.has(parent)) {
      placed.add(parent)
      place(nodes.get(parent)!, parentOf.get(parent))
    }
    ;(parent !== undefined && nodes.has(parent) ? nodes.get(parent)!.children! : roots).push(node)
  }
  for (const step of steps)
    place({ id: step.id, width: nodeWidth, height: heights.get(step.id) ?? fallbackHeight }, step.parent)
  for (const frame of frames) {
    if (!placed.has(frame.id)) {
      placed.add(frame.id)
      place(nodes.get(frame.id)!, frame.parent)
    }
  }
  const known = new Set([...steps.map((step) => step.id), ...frames.map((frame) => frame.id)])
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
      'elk.hierarchyHandling': 'INCLUDE_CHILDREN',
      // A block's branches keep the order the process gives them, from the top.
      'elk.layered.crossingMinimization.forceNodeModelOrder': 'true',
      'org.eclipse.elk.json.edgeCoords': 'ROOT',
    },
    children: roots,
    edges: transitions
      .filter((transition) => known.has(transition.source) && known.has(transition.target))
      .map((transition): ElkExtendedEdge => ({
        id: transition.id,
        sources: [transition.source],
        targets: [transition.target],
        labels: transition.label ? [{ text: transition.label, width: labelWidth(transition.label), height: 16 }] : [],
      })),
  }
  const laid = await elk.layout(graph)
  const places = new Map<string, Point>()
  const sizes = new Map<string, { width: number; height: number }>()
  // Where each node is on the whole graph, which a route's straightening needs.
  const absolute = new Map<string, Rect>()
  const walk = (children: ElkNode[] | undefined, origin: Point) => {
    for (const child of children ?? []) {
      const at = { x: child.x ?? 0, y: child.y ?? 0 }
      places.set(child.id, at)
      const rect = {
        x: origin.x + at.x,
        y: origin.y + at.y,
        width: child.width ?? nodeWidth,
        height: child.height ?? fallbackHeight,
      }
      absolute.set(child.id, rect)
      if (nodes.has(child.id)) {
        sizes.set(child.id, { width: child.width ?? 0, height: child.height ?? 0 })
      }
      walk(child.children, rect)
    }
  }
  walk(laid.children, { x: 0, y: 0 })
  const routes = new Map(
    (laid.edges ?? []).map((edge) => {
      const section = edge.sections?.[0]
      const rectOf = (id: string | undefined) => (id === undefined ? undefined : absolute.get(id))
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
  return { places, sizes, routes }
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
              // On the graph's ground, `fillSubtle`, so the label cuts the line.
              background: colour.fillSubtle,
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
  // Only a new layout fits the view, never a page that draws again, which would undo where the reader has moved to.
  const { fitView } = useReactFlow()
  const fit = useRef(fitView)
  useEffect(() => {
    fit.current = fitView
  })
  useEffect(() => {
    if (layout === null) {
      return
    }
    const frame = requestAnimationFrame(() => {
      void fit.current(focus === undefined ? undefined : { nodes: [{ id: focus }], minZoom: 1, maxZoom: 1 })
    })
    return () => cancelAnimationFrame(frame)
  }, [layout, focus])
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
/**
 * Proposed, see the review on #3650: a step's card, which the graph draws in its node and also, hidden, to measure it,
 * so the height the layout gives a card is the height the card takes, in the same font and widths. A card's state on an
 * instance is its corner's icon, as Camunda Operate, n8n and Azure Logic Apps mark a run's steps, so its border says
 * only which step is chosen: `borderSubtle` on every card, and 2 px of `primary` on the step chosen, in a designer or
 * by a click or Find a step, the second pixel inside the border so the card keeps its size. A designer's step with a
 * diagnostic keeps a `danger` border.
 */
function StepCard({ data, fill }: { data: ProcessGraphStep; fill: boolean }) {
  const { colour, radius, spacing } = useDesignTokens()
  const found = useContext(FoundStep)
  const chosen = data.tone === 'selected' || found === data.id
  const edge = chosen
    ? colour.primary
    : data.tone === 'error' && data.status === undefined
      ? colour.danger
      : data.tone === 'plain'
        ? colour.border
        : colour.borderSubtle
  const Icon = stepIcons[data.kind] ?? ThunderboltOutlined
  const muted = data.tone === 'idle'
  return (
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
        gap: spacing.xs,
        width: '100%',
        height: fill ? '100%' : undefined,
        padding: `${spacing.xs}px ${spacing.sm}px`,
        overflow: 'hidden',
        textAlign: 'left',
        font: 'inherit',
        color: muted ? colour.textMuted : colour.text,
        background: colour.surface,
        border: `1px solid ${edge}`,
        boxShadow: chosen ? `inset 0 0 0 1px ${edge}` : undefined,
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
            width: 24,
            height: 24,
            borderRadius: radius.base,
            color: chosen ? colour.primaryText : colour.textMuted,
            background: colour.fillSubtle,
          }}
        >
          <Icon />
        </span>
      </Tooltip>
      {/* The label's first line sits level with the icon: 2 px down, half of the 24 px tile less its 20 px line. */}
      <span style={{ display: 'flex', flexDirection: 'column', gap: 0, minWidth: 0, paddingTop: 2 }}>
        {/* At most two lines, then an ellipsis, with the whole label in a tooltip only when it is cut. */}
        <Typography.Paragraph
          ellipsis={{ rows: 2, tooltip: data.name }}
          style={{
            margin: 0,
            color: 'inherit',
            fontSize: labelType.size,
            fontWeight: labelType.weight,
            lineHeight: `${labelType.lineHeight}px`,
          }}
        >
          {data.name}
        </Typography.Paragraph>
        {data.status
          ? // The state is the corner's icon; the line keeps how long the step lasted or how often it ran.
            data.status.detail !== undefined && (
              <span
                style={{
                  fontSize: 12,
                  lineHeight: '16px',
                  color: colour.textMuted,
                  whiteSpace: 'nowrap',
                  overflow: 'hidden',
                  textOverflow: 'ellipsis',
                }}
              >
                {data.status.detail}
              </span>
            )
          : // A step's type is its icon's tooltip, so the card says no more than a layer's mark under its label.
            data.note !== undefined && (
              <span style={{ fontSize: 12, lineHeight: '16px', color: colour.textMuted }}>
                <span data-layer-mark={data.note.kind}>{data.note.text}</span>
              </span>
            )}
      </span>
    </button>
  )
}

/** Proposed, see the review on #3650: the icon of a step's state on an instance, in its tone's colour. */
const stateIcons: Record<StatusTone, ComponentType<{ style?: React.CSSProperties }>> = {
  success: CheckCircleFilled,
  danger: CloseCircleFilled,
  attention: ExclamationCircleFilled,
  neutral: ClockCircleFilled,
  muted: MinusCircleFilled,
}

/**
 * Proposed, see the review on #3650: a step's state on an instance as an icon on its card's top right corner, on a disc
 * of `surface` that cuts the border, with the state's words as its tooltip; the card's accessible name says it too.
 */
function StateBadge({ status }: { status: NonNullable<ProcessGraphStep['status']> }) {
  const { colour } = useDesignTokens()
  const tones: Record<StatusTone, string> = {
    success: colour.successText,
    danger: colour.dangerText,
    attention: colour.warningText,
    neutral: colour.infoText,
    muted: colour.textMuted,
  }
  const Icon = stateIcons[status.tone]
  return (
    <Tooltip title={status.text}>
      <span
        aria-hidden
        style={{
          position: 'absolute',
          top: -8,
          right: -8,
          display: 'flex',
          padding: 1,
          borderRadius: '50%',
          background: colour.surface,
          fontSize: 16,
          lineHeight: 1,
        }}
      >
        <Icon style={{ color: tones[status.tone] }} />
      </span>
    </Tooltip>
  )
}

function StepNodeView({ data }: NodeProps<StepNode>) {
  const { colour } = useDesignTokens()
  return (
    <>
      {/* Hidden, not removed, so React Flow still measures where a transition comes in. */}
      <Handle type="target" position={Position.Left} isConnectable={false} style={{ visibility: 'hidden' }} />
      <StepCard data={data} fill />
      {data.status && <StateBadge status={data.status} />}
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

/** The step chosen on the graph, which its node marks while it stays chosen. */
const FoundStep = createContext<string | null>(null)

/**
 * Proposed, see D155 and the review on #3650: a frame round the steps of a block or a branch: a block in a dashed
 * border with its name and its rule of completion, a branch in a light border with its name.
 */
function FrameNodeView({ data }: NodeProps<FrameNode>) {
  const { colour, radius, spacing } = useDesignTokens()
  const block = data.kind === 'block'
  return (
    <>
      <Handle type="target" position={Position.Left} isConnectable={false} style={{ visibility: 'hidden' }} />
      <div
        data-testid={data.testId}
        role="group"
        aria-label={data.note ? `${data.label}, ${data.note}` : data.label}
        style={{
          width: '100%',
          height: '100%',
          border: `1px ${block ? 'dashed' : 'solid'} ${block ? colour.border : colour.borderSubtle}`,
          borderRadius: radius.base,
          background: block ? 'transparent' : colour.surface,
          padding: `${spacing.xs}px ${spacing.sm}px`,
          fontSize: block ? 14 : 12,
          lineHeight: '20px',
          color: block ? colour.text : colour.textMuted,
          pointerEvents: 'none',
        }}
      >
        <span style={{ fontWeight: block ? 600 : 500 }}>{data.label}</span>
        {data.note && <span style={{ color: colour.textMuted, marginInlineStart: spacing.xs }}>({data.note})</span>}
      </div>
      <Handle type="source" position={Position.Right} isConnectable={false} style={{ visibility: 'hidden' }} />
    </>
  )
}

const nodeTypes = { step: StepNodeView, frame: FrameNodeView }
const noFrames: readonly ProcessGraphFrame[] = []

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
  selected,
  onSelect,
  frames = noFrames,
  currents,
}: {
  steps: readonly ProcessGraphStep[]
  transitions: readonly ProcessGraphTransitionView[]
  /** Proposed, see D155 and the review on #3650: the frames drawn round steps, such as a parallel block's branches. */
  frames?: readonly ProcessGraphFrame[]
  /**
   * Proposed, see D155 and the review on #3650: every step an instance is at, which Go to the current step offers when
   * there is more than one.
   */
  currents?: readonly { id: string; label: string }[]
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
  /**
   * Proposed, see the review on #3650: the step chosen on the graph, by a click on it or by Find a step, which it marks
   * with a ring outside its card, and `null` for none; without it the graph keeps its own choice.
   */
  selected?: string | null
  onSelect?: (id: string | null) => void
}) {
  const { mode } = useThemeMode()
  const area = useRef<HTMLElement | null>(null)
  // The step chosen on the graph, marked as long as it stays chosen.
  const [ownChoice, setOwnChoice] = useState<string | null>(focus ?? null)
  const chosen = selected !== undefined ? selected : ownChoice
  const choose = (id: string | null) => {
    setOwnChoice(id)
    onSelect?.(id)
  }
  const { colour, radius } = useDesignTokens()
  const [layout, setLayout] = useState<Layout | null>(null)
  // Each card's height as drawn, measured in the hidden copies below once the label's font has loaded.
  const [heights, setHeights] = useState<ReadonlyMap<string, number> | null>(null)
  const [fontReady, setFontReady] = useState(false)
  useEffect(() => {
    let current = true
    void labelFontLoaded().then(() => {
      if (current) {
        setFontReady(true)
      }
    })
    return () => {
      current = false
    }
  }, [])
  const measureLayer = useRef<HTMLDivElement>(null)
  useLayoutEffect(() => {
    const layer = measureLayer.current
    if (!fontReady || layer === null) {
      return
    }
    const measure = () => {
      const next = new Map<string, number>()
      layer.querySelectorAll<HTMLElement>('[data-measure]').forEach((card) => {
        next.set(card.dataset.measure ?? '', Math.ceil(card.getBoundingClientRect().height))
      })
      setHeights((before) =>
        before !== null && before.size === next.size && [...next].every(([id, height]) => before.get(id) === height)
          ? before
          : next,
      )
    }
    measure()
    if (typeof ResizeObserver === 'undefined') {
      return
    }
    const observer = new ResizeObserver(measure)
    layer.querySelectorAll('[data-measure]').forEach((card) => observer.observe(card))
    return () => observer.disconnect()
  })

  // The graph is laid out again only when its shape changes: its steps, their labels and its transitions. A page that
  // draws again, as when a step is chosen, keeps the layout and the view the reader has moved to.
  const shape = JSON.stringify([
    frames.map((frame) => [frame.id, frame.parent, frame.label]),
    steps.map((step) => [step.id, step.name, step.parent]),
    transitions.map((transition) => [transition.id, transition.source, transition.target, transition.label]),
  ])
  const latest = useRef({ steps, transitions, frames })
  useEffect(() => {
    latest.current = { steps, transitions, frames }
  })
  useEffect(() => {
    if (heights === null) {
      return
    }
    let current = true
    void layOut(latest.current.steps, latest.current.transitions, latest.current.frames, heights).then((next) => {
      if (current) {
        setLayout(next)
      }
    })
    return () => {
      current = false
    }
  }, [shape, heights])

  const { nodes, edges } = useMemo(() => {
    if (layout === null) {
      return { nodes: [], edges: [] }
    }
    // A frame comes before what it holds, as React Flow needs a parent before its children.
    const frameNodes = frames.map((frame): FrameNode => {
      const size = layout.sizes.get(frame.id) ?? { width: nodeWidth, height: fallbackHeight }
      return {
        id: frame.id,
        type: 'frame',
        position: layout.places.get(frame.id) ?? { x: 0, y: 0 },
        parentId: frame.parent,
        width: size.width,
        height: size.height,
        measured: size,
        selectable: false,
        focusable: false,
        zIndex: -1,
        handles: [
          { type: 'target', position: Position.Left, x: 0, y: size.height / 2, width: 1, height: 1 },
          { type: 'source', position: Position.Right, x: size.width - 1, y: size.height / 2, width: 1, height: 1 },
        ],
        data: { ...frame },
      }
    })
    return {
      nodes: [
        ...frameNodes,
        ...steps.map((step): StepNode => ({
          id: step.id,
          type: 'step',
          parentId: step.parent,
          position: layout.places.get(step.id) ?? { x: 0, y: 0 },
          width: nodeWidth,
          height: heights?.get(step.id) ?? fallbackHeight,
          // The card's size is known, so it is given as measured too: a node drawn again keeps it, and Find a step and
          // the fit can place it without waiting for the browser to measure it again.
          measured: { width: nodeWidth, height: heights?.get(step.id) ?? fallbackHeight },
          handles: [
            {
              type: 'target',
              position: Position.Left,
              x: 0,
              y: (heights?.get(step.id) ?? fallbackHeight) / 2,
              width: 1,
              height: 1,
            },
            {
              type: 'source',
              position: Position.Right,
              x: nodeWidth - handleSize / 2,
              y: (heights?.get(step.id) ?? fallbackHeight) / 2 - handleSize / 2,
              width: handleSize,
              height: handleSize,
            },
          ],
          data: step,
        })),
      ],
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
  }, [layout, steps, transitions, frames, heights, colour, Adornment, onTransitionClick])

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
      // The graph lies on `fillSubtle`, set apart from the page in both modes, so its steps on the surface stand out with
      // no pattern behind them, as Camunda Operate and Figma draw a diagram; in full screen it fills the screen the same
      // way.
      style={{ height, position: 'relative', background: colour.fillSubtle, borderRadius: radius.base }}
      onKeyDown={onKeyDown}
    >
      <FoundStep.Provider value={chosen}>
        {/* Each card drawn once more, hidden and out of the way, at a card's width, so the layout takes the height the
            card is drawn at, in the same font, after the label's font has loaded. */}
        {fontReady && (
          <div
            ref={measureLayer}
            aria-hidden
            style={{
              position: 'absolute',
              left: 0,
              top: 0,
              width: nodeWidth,
              visibility: 'hidden',
              pointerEvents: 'none',
            }}
          >
            {steps.map((step) => (
              <div key={step.id} data-measure={step.id} style={{ width: nodeWidth }}>
                <StepCard data={{ ...step, testId: undefined }} fill={false} />
              </div>
            ))}
          </div>
        )}
        <ReactFlow
          nodes={nodes}
          edges={edges}
          nodeTypes={nodeTypes}
          edgeTypes={edgeTypes}
          colorMode={mode}
          // React Flow's dark mode paints its own ground, which would hide `fillSubtle` under it.
          style={{ background: 'transparent' }}
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
          // With the controls a click chooses the step, then calls onStepClick, and a click on the ground clears the
          // choice; without them, as in the designer, a click only calls onStepClick.
          onNodeClick={
            controls
              ? (_, node) => {
                  choose(node.id)
                  onStepClick?.(node.id)
                }
              : onStepClick
                ? (_, node) => onStepClick(node.id)
                : undefined
          }
          onPaneClick={controls ? () => choose(null) : undefined}
          onEdgeClick={onTransitionClick ? (_, edge) => onTransitionClick(edge.id) : undefined}
        >
          <FitOnLayout layout={layout} focus={focus} />
          {controls && (
            <ProcessGraphControls
              area={area}
              focus={focus}
              currents={currents}
              steps={steps.map((step) => ({ id: step.id, label: step.name }))}
              selected={chosen}
              onSelect={choose}
            />
          )}
        </ReactFlow>
      </FoundStep.Provider>
    </section>
  )
}
