import { useReactFlow } from '@xyflow/react'
import { useMemo } from 'react'
import { useReducedMotion } from './useReducedMotion'

/** How long a move of the view takes, in ms, see docs/reference/process-inspector-widget.md#process-graph. */
const moveDuration = 300

/**
 * The moves of a graph's view, each animated over 300 ms, and at once under reduced motion, see
 * docs/reference/frontend.md#reduced-motion. It needs a `ReactFlowProvider` above it. `goTo` centres a step at zoom 1,
 * the size the cards are drawn at, and `centre` moves the view to a point at the zoom it has.
 */
export function useGraphMoves() {
  const { zoomIn, zoomOut, fitView, getNode, setCenter, getZoom } = useReactFlow()
  const duration = useReducedMotion() ? 0 : moveDuration
  return useMemo(
    () => ({
      zoomIn: () => void zoomIn({ duration }),
      zoomOut: () => void zoomOut({ duration }),
      showAll: () => void fitView({ maxZoom: 1, duration }),
      goTo: (id: string) => {
        const node = getNode(id)
        if (node !== undefined) {
          const width = node.measured?.width ?? node.width ?? 0
          const height = node.measured?.height ?? node.height ?? 0
          void setCenter(node.position.x + width / 2, node.position.y + height / 2, { zoom: 1, duration })
        }
      },
      centre: (x: number, y: number) => void setCenter(x, y, { zoom: getZoom(), duration }),
    }),
    [zoomIn, zoomOut, fitView, getNode, setCenter, getZoom, duration],
  )
}
