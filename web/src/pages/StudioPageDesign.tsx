import {
  DndContext,
  DragOverlay,
  PointerSensor,
  pointerWithin,
  useSensor,
  useSensors,
  type CollisionDetection,
  type DragEndEvent,
} from '@dnd-kit/core'
import { Button, Flex } from 'antd'
import { useContext, useState } from 'react'
import { getAt } from '../platform/jsonPatch'
import {
  addableTypes,
  boundWidget,
  dropPlace,
  innermostPointer,
  insertionPlace,
  newWidget,
  targetPlace,
  widgetPointerOf,
  type DataBinding,
  type DropTarget,
} from '../platform/pageWidgets'
import { StudioNamesContext } from '../platform/studioNames'
import { useText } from '../platform/texts'
import { BindingChoice, PageDataPanel } from './PageDataPanel'
import { PageWidgets } from './PageEditor'
import { PagePalette } from './PagePalette'
import { landedText, paletteReasons, usePartLabel } from './pagePaletteSupport'
import type { ChangeHandler } from './SchemaForm'
import { StudioPageCanvas } from './StudioPageCanvas'

/** The pointer of the draft that a droppable stands for. */
function pointerOf(data: unknown): string | undefined {
  const pointer = (data as { pointer?: unknown } | undefined)?.pointer
  return typeof pointer === 'string' ? pointer : undefined
}

/** Of the droppables under the pointer, only the innermost one: the one whose pointer has the most segments. */
const innermostCollision: CollisionDetection = (args) => {
  const collisions = pointerWithin(args)
  const wanted = innermostPointer(
    collisions.flatMap((collision) => pointerOf(collision.data?.droppableContainer.data.current) ?? []),
  )
  return collisions.filter((collision) => pointerOf(collision.data?.droppableContainer.data.current) === wanted)
}

/** What a drag of the library says aloud by itself is off: the palette's live region is the only voice. */
const silent = {
  onDragStart: () => undefined,
  onDragOver: () => undefined,
  onDragEnd: () => undefined,
  onDragCancel: () => undefined,
}

/** The binding that a draggable of the data panel carries, or `undefined` for any other draggable. */
function bindingOf(data: unknown): DataBinding | undefined {
  const binding = (data as { binding?: Partial<DataBinding> } | undefined)?.binding
  return (binding?.kind === 'entity' || binding?.kind === 'dataSource') && typeof binding.name === 'string'
    ? { kind: binding.kind, name: binding.name }
    : undefined
}

/**
 * The Design tab of a page: the canvas, and beside it the palette, the data panel and the widget tree. A palette type that is dragged
 * with the pointer onto the canvas is inserted where it is dropped: before a widget on its upper half, after it on its
 * lower half, or at the end of a list when it is dropped on the list. The innermost target wins. A drop that a page
 * limit forbids inserts nothing, and the live region gives the reason. An allowed drop selects the new widget, and
 * the live region names the list and position where it landed. The canvas, the palette and the drag library are
 * one lazy chunk, which loads when the tab opens.
 *
 * An entity or data source of the data panel is dropped the same way, or picked with the keyboard, which places it
 * after the selected widget as the palette does. Either opens a choice of the widgets that can show it. The chosen
 * widget is inserted bound, selected, and announced like a palette insert.
 */
export default function StudioPageDesign({
  application,
  resourceId,
  draft,
  selected,
  onSelect,
  onChange,
}: {
  application: string
  resourceId: string
  draft: Record<string, unknown>
  /** The pointer of the selected widget. */
  selected: string
  onSelect: (pointer: string) => void
  onChange: ChangeHandler
}) {
  const t = useText()
  const partLabel = usePartLabel()
  const [announcement, setAnnouncement] = useState('')
  const [dragging, setDragging] = useState<string>()
  const [pending, setPending] = useState<{ binding: DataBinding; place: { list: string; index: number } }>()
  const names = useContext(StudioNamesContext)
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 4 } }))

  function drop({ active, over, delta, activatorEvent }: DragEndEvent) {
    setDragging(undefined)
    const type = (active.data.current as { type?: unknown } | undefined)?.type
    const binding = bindingOf(active.data.current)
    const data = over?.data.current as { kind?: unknown; pointer?: unknown } | undefined
    const widgets = draft.widgets
    if (
      over === null ||
      (binding === undefined && (typeof type !== 'string' || !addableTypes.includes(type))) ||
      typeof data?.pointer !== 'string' ||
      !Array.isArray(widgets)
    ) {
      return
    }
    let target: DropTarget
    if (data.kind === 'widget') {
      const pointerY = ((activatorEvent as { clientY?: number }).clientY ?? 0) + delta.y
      target = {
        kind: 'widget',
        pointer: data.pointer,
        half: pointerY < over.rect.top + over.rect.height / 2 ? 'upper' : 'lower',
      }
    } else if (data.kind === 'list') {
      target = { kind: 'list', pointer: data.pointer }
    } else {
      return
    }
    if (binding !== undefined) {
      const place = targetPlace(widgets, target)
      if (place !== undefined) {
        setPending({ binding, place })
      }
      return
    }
    const place = dropPlace(widgets, type as string, target)
    if (place === undefined) {
      return
    }
    if ('limit' in place) {
      setAnnouncement(t(paletteReasons[place.limit]))
      return
    }
    const items = getAt({ widgets }, place.list) as unknown[]
    onChange(place.list, [...items.slice(0, place.index), newWidget(type as string), ...items.slice(place.index)])
    onSelect(`${place.list}/${place.index}`)
    setAnnouncement(landedText(t, partLabel, widgets, place.list, place.index, type as string))
  }

  /** Opens the choice for a binding picked with the keyboard, at the place after the selected widget. */
  function pick(binding: DataBinding) {
    const widgets = draft.widgets
    if (!Array.isArray(widgets)) {
      return
    }
    const widgetPointer = widgetPointerOf(selected)
    setPending({
      binding,
      place: insertionPlace(widgets, getAt(draft, widgetPointer) === undefined ? '/widgets/0' : widgetPointer),
    })
  }

  function choose(type: string) {
    const widgets = draft.widgets
    setPending(undefined)
    if (pending === undefined || names === undefined || !Array.isArray(widgets)) {
      return
    }
    const { binding, place } = pending
    const items = getAt({ widgets }, place.list)
    if (!Array.isArray(items)) {
      return
    }
    onChange(place.list, [
      ...items.slice(0, place.index),
      boundWidget(names, draft, binding, type),
      ...items.slice(place.index),
    ])
    onSelect(`${place.list}/${place.index}`)
    setAnnouncement(landedText(t, partLabel, widgets, place.list, place.index, type))
  }

  return (
    <DndContext
      sensors={sensors}
      collisionDetection={innermostCollision}
      accessibility={{ announcements: silent }}
      onDragStart={({ active }) =>
        setDragging(
          bindingOf(active.data.current)?.name ?? String((active.data.current as { type?: unknown })?.type ?? ''),
        )
      }
      onDragCancel={() => setDragging(undefined)}
      onDragEnd={drop}
    >
      <Flex gap="middle" align="flex-start" data-testid="studio-page-design">
        <div style={{ flex: 1, minWidth: 0 }}>
          <StudioPageCanvas
            application={application}
            resourceId={resourceId}
            draft={draft}
            selected={selected}
            onSelect={onSelect}
          />
        </div>
        <div data-testid="studio-design-panel" style={{ width: 400, flexShrink: 0 }}>
          <PagePalette
            value={draft}
            selected={selected}
            announcement={announcement}
            onAnnounce={setAnnouncement}
            onSelect={onSelect}
            onChange={onChange}
          />
          <PageDataPanel onPick={pick} />
          <PageWidgets value={draft} selected={selected} onSelect={onSelect} onChange={onChange} />
        </div>
      </Flex>
      <BindingChoice
        binding={pending?.binding}
        widgets={Array.isArray(draft.widgets) ? draft.widgets : []}
        list={pending?.place.list ?? '/widgets'}
        onChoose={choose}
        onCancel={() => setPending(undefined)}
      />
      <DragOverlay dropAnimation={null}>{dragging === undefined ? null : <Button>{dragging}</Button>}</DragOverlay>
    </DndContext>
  )
}
