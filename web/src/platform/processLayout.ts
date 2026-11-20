import type { ELK, ElkExtendedEdge } from 'elkjs/lib/elk-api'
import { useEffect, useState } from 'react'

/** A point of a graph, in the graph's own units. */
export interface GraphPoint {
  x: number
  y: number
}

/** The nodes and edges of a graph to lay out. Each node has the size it is drawn at. */
export interface GraphInput {
  nodes: { id: string; width: number; height: number }[]
  edges: { id: string; source: string; target: string }[]
}

/** Where each node's top left corner lies, and each edge's route from its source to its target. */
export interface GraphLayout {
  positions: Map<string, GraphPoint>
  routes: Map<string, GraphPoint[]>
}

/** The shortest part of a route, in px, see docs/reference/process-inspector-widget.md#process-graph. */
const minimumSegment = 16

const layoutOptions = {
  'elk.algorithm': 'layered',
  'elk.direction': 'RIGHT',
  'elk.edgeRouting': 'ORTHOGONAL',
  'elk.layered.cycleBreaking.strategy': 'MODEL_ORDER',
  'elk.layered.considerModelOrder.strategy': 'NODES_AND_EDGES',
  'elk.layered.feedbackEdges': 'true',
  'elk.spacing.nodeNode': '40',
  'elk.layered.spacing.nodeNodeBetweenLayers': '80',
  'elk.spacing.edgeNode': String(minimumSegment),
  'elk.layered.spacing.edgeNodeBetweenLayers': String(minimumSegment),
}

let elk: Promise<ELK> | undefined

/** The one ELK instance of the page. The library loads with its first use, in a chunk of its own. */
function loadElk(): Promise<ELK> {
  elk ??= import('elkjs/lib/elk.bundled.js').then(({ default: Elk }) => new Elk())
  return elk
}

/**
 * Lays a graph out from left to right with ELK's layered algorithm, routing each edge in right angles, see
 * docs/reference/process-inspector-widget.md#process-graph. An edge whose source or target is not a node is left out.
 */
export async function layoutGraph(input: GraphInput): Promise<GraphLayout> {
  const ids = new Set(input.nodes.map((node) => node.id))
  const edges = input.edges.filter((edge) => ids.has(edge.source) && ids.has(edge.target))
  const engine = await loadElk()
  const laidOut = await engine.layout({
    id: 'graph',
    layoutOptions,
    children: input.nodes.map(({ id, width, height }) => ({ id, width, height })),
    edges: edges.map((edge) => ({ id: edge.id, sources: [edge.source], targets: [edge.target] })),
  })
  const positions = new Map<string, GraphPoint>()
  for (const node of laidOut.children ?? []) {
    positions.set(node.id, { x: node.x ?? 0, y: node.y ?? 0 })
  }
  const routes = new Map<string, GraphPoint[]>()
  for (const edge of (laidOut.edges ?? []) as ElkExtendedEdge[]) {
    const section = edge.sections?.[0]
    if (section) {
      routes.set(edge.id, [section.startPoint, ...(section.bendPoints ?? []), section.endPoint])
    }
  }
  return { positions, routes }
}

/**
 * The last finished layout of a graph, or `null` before the first one. It lays the graph out again when a node, a size
 * or an edge changes. The previous layout stays until the new one is done, and a layout that finishes after a newer
 * one started is dropped.
 */
export function useGraphLayout(input: GraphInput): GraphLayout | null {
  const [layout, setLayout] = useState<GraphLayout | null>(null)
  const [failure, setFailure] = useState<unknown>(null)
  const key = JSON.stringify(input)
  useEffect(() => {
    let stale = false
    layoutGraph(JSON.parse(key) as GraphInput).then(
      (result) => {
        if (!stale) {
          setLayout(result)
        }
      },
      (error: unknown) => {
        if (!stale) {
          setFailure(error ?? new Error('The graph layout failed'))
        }
      },
    )
    return () => {
      stale = true
    }
  }, [key])
  if (failure !== null) {
    throw failure
  }
  return layout
}

const distance = (from: GraphPoint, to: GraphPoint) => Math.hypot(to.x - from.x, to.y - from.y)

/** The point `by` from `from` towards `to`. */
function towards(from: GraphPoint, to: GraphPoint, by: number): GraphPoint {
  const length = distance(from, to)
  return length === 0
    ? from
    : { x: from.x + ((to.x - from.x) * by) / length, y: from.y + ((to.y - from.y) * by) / length }
}

/**
 * An SVG path through the points of a route: straight segments with a quadratic curve at each bend. The radius is
 * clamped to half the shorter of the two segments at the bend.
 */
export function roundedPath(points: GraphPoint[], radius = 8): string {
  const [first, ...rest] = points
  if (!first) {
    return ''
  }
  let path = `M ${first.x} ${first.y}`
  rest.forEach((point, index) => {
    const before = points[index]!
    const after = rest[index + 1]
    if (!after) {
      path += ` L ${point.x} ${point.y}`
      return
    }
    const round = Math.min(radius, distance(before, point) / 2, distance(point, after) / 2)
    const entry = towards(point, before, round)
    const exit = towards(point, after, round)
    path += ` L ${entry.x} ${entry.y} Q ${point.x} ${point.y} ${exit.x} ${exit.y}`
  })
  return path
}
