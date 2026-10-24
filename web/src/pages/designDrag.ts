import { pointerWithin, type CollisionDetection } from '@dnd-kit/core'
import { innermostPointer } from '../platform/pageWidgets'

/** The pointer of the draft that a droppable stands for. */
function pointerOf(data: unknown): string | undefined {
  const pointer = (data as { pointer?: unknown } | undefined)?.pointer
  return typeof pointer === 'string' ? pointer : undefined
}

/** Of the droppables under the pointer, only the innermost one: the one whose pointer has the most segments. */
export const innermostCollision: CollisionDetection = (args) => {
  const collisions = pointerWithin(args)
  const wanted = innermostPointer(
    collisions.flatMap((collision) => pointerOf(collision.data?.droppableContainer.data.current) ?? []),
  )
  return collisions.filter((collision) => pointerOf(collision.data?.droppableContainer.data.current) === wanted)
}

/** What a drag of the library says aloud by itself is off: the palette's live region is the only voice. */
export const silent = {
  onDragStart: () => undefined,
  onDragOver: () => undefined,
  onDragEnd: () => undefined,
  onDragCancel: () => undefined,
}
