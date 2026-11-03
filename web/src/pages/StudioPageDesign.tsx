import {
  DndContext,
  DragOverlay,
  PointerSensor,
  useSensor,
  useSensors,
  type DragEndEvent,
  type DragOverEvent,
  type DragStartEvent,
} from '@dnd-kit/core'
import { Button, Flex, Typography } from 'antd'
import { useContext, useState } from 'react'
import { getAt } from '../platform/jsonPatch'
import {
  addableTypes,
  boundWidget,
  dropPlace,
  insertionPlace,
  keyboardMove,
  movePlace,
  moveTo,
  namedWidget,
  newWidget,
  targetPlace,
  widgetPointerOf,
  type DataBinding,
  type DropTarget,
} from '../platform/pageWidgets'
import { StudioNamesContext } from '../platform/studioNames'
import { useText } from '../platform/texts'
import { innermostCollision, silent } from './designDrag'
import { BindingChoice, PageDataPanel } from './PageDataPanel'
import { PageWidgets } from './PageEditor'
import { PagePalette } from './PagePalette'
import { landedText, paletteReasons, usePartLabel } from './pagePaletteSupport'
import type { ChangeHandler } from './SchemaForm'
import { StudioPageCanvas } from './StudioPageCanvas'

/** The pointer of the widget list or widget before the last segment: `/widgets/1/widgets/0` gives `/widgets/1/widgets`. */
function parentList(pointer: string): string {
  return pointer.slice(0, pointer.lastIndexOf('/'))
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
 * the live region names the list and position where it landed.
 *
 * An entity or data source of the data panel is dropped the same way, or picked with the keyboard, which places it
 * after the selected widget as the palette does. Either opens a choice of the widgets that can show it. The chosen
 * widget is inserted bound, selected, and announced like a palette insert.
 *
 * A widget of the canvas that is dragged moves to the same targets, as one edit of the draft, and stays selected. A
 * move the format refuses is flagged while it is dragged: the drag overlay and the live region give the reason, and a
 * drop there changes nothing. These are a widget inside itself, and a container whose lists would go past level 4. A
 * drop on the widget itself or where it already is changes nothing and says nothing.
 *
 * The keyboard moves the selected canvas widget with Alt+ArrowDown and Alt+ArrowUp, one allowed place at a time, as one
 * edit of the draft. The live region names where it landed, or says that it cannot move further. The canvas, the
 * palette, the data panel and the drag library are one lazy chunk, which loads when the tab opens.
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
  const [dragging, setDragging] = useState<{ label: string; refusal?: string }>()
  const [pending, setPending] = useState<{ binding: DataBinding; place: { list: string; index: number } }>()
  const names = useContext(StudioNamesContext)
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 4 } }))

  /** What the pointer is over as a drop target, or undefined. The half is only read for a widget. */
  function targetOf(
    { over, delta, activatorEvent }: DragOverEvent | DragEndEvent,
    lower?: boolean,
  ): DropTarget | undefined {
    const data = over?.data.current as { kind?: unknown; pointer?: unknown } | undefined
    if (over === null || typeof data?.pointer !== 'string') {
      return undefined
    }
    if (data.kind === 'widget') {
      const pointerY = ((activatorEvent as { clientY?: number }).clientY ?? 0) + delta.y
      return {
        kind: 'widget',
        pointer: data.pointer,
        half: lower || pointerY >= over.rect.top + over.rect.height / 2 ? 'lower' : 'upper',
      }
    }
    return data.kind === 'list' ? { kind: 'list', pointer: data.pointer } : undefined
  }

  /** The pointer of the canvas widget that is dragged, or undefined for a palette drag. */
  function movedFrom(active: DragStartEvent['active']): string | undefined {
    const data = active.data.current as { kind?: unknown; pointer?: unknown } | undefined
    return data?.kind === 'move' && typeof data.pointer === 'string' ? data.pointer : undefined
  }

  function start({ active }: DragStartEvent) {
    const data = active.data.current as { type?: unknown; label?: unknown } | undefined
    const binding = bindingOf(active.data.current)
    setDragging({
      label: binding?.name ?? String(movedFrom(active) === undefined ? (data?.type ?? '') : (data?.label ?? '')),
    })
  }

  function dragOver(event: DragOverEvent) {
    const from = movedFrom(event.active)
    if (from === undefined) {
      return
    }
    const target = targetOf(event, true)
    const widgets = draft.widgets
    const place = target === undefined || !Array.isArray(widgets) ? undefined : movePlace(widgets, from, target)
    const refusal = place !== undefined && 'limit' in place ? t(paletteReasons[place.limit]) : undefined
    setDragging((current) => (current && current.refusal !== refusal ? { label: current.label, refusal } : current))
    if (refusal !== undefined) {
      setAnnouncement(refusal)
    }
  }

  function move(event: DragEndEvent, from: string, widgets: unknown[]) {
    const target = targetOf(event)
    const place = target === undefined ? undefined : movePlace(widgets, from, target)
    if (place === undefined) {
      return
    }
    if ('limit' in place) {
      setAnnouncement(t(paletteReasons[place.limit]))
      return
    }
    const moved = moveTo(widgets, from, place.list, place.index)
    if (moved === undefined || moved.pointer === from) {
      return
    }
    applyMove(moved, widgets, from)
  }

  /** Puts a moved widget into the draft, keeps it selected and says where it landed. */
  function applyMove(moved: { widgets: unknown[]; pointer: string }, widgets: unknown[], from: string) {
    const type = (getAt({ widgets }, from) as { type?: unknown }).type
    onChange('/widgets', moved.widgets)
    onSelect(moved.pointer)
    setAnnouncement(
      landedText(
        t,
        partLabel,
        moved.widgets,
        parentList(moved.pointer),
        Number(moved.pointer.slice(moved.pointer.lastIndexOf('/') + 1)),
        String(type),
        'shell.studio.palette.moved',
      ),
    )
  }

  /** Moves the widget one allowed place by key, and answers whether it moved. */
  function keyMove(from: string, direction: 'up' | 'down'): boolean {
    const widgets = draft.widgets
    if (!Array.isArray(widgets)) {
      return false
    }
    const moved = keyboardMove(widgets, from, direction)
    if (moved === undefined) {
      setAnnouncement(t('shell.studio.palette.cannotMoveFurther'))
      return false
    }
    applyMove(moved, widgets, from)
    return true
  }

  function drop(event: DragEndEvent) {
    const { active, over } = event
    setDragging(undefined)
    const widgets = draft.widgets
    const from = movedFrom(active)
    if (from !== undefined) {
      if (Array.isArray(widgets)) {
        move(event, from, widgets)
      }
      return
    }
    const type = (active.data.current as { type?: unknown } | undefined)?.type
    const binding = bindingOf(active.data.current)
    const target = targetOf(event)
    if (
      over === null ||
      (binding === undefined && (typeof type !== 'string' || !addableTypes.includes(type))) ||
      target === undefined ||
      !Array.isArray(widgets)
    ) {
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
    onChange(place.list, [
      ...items.slice(0, place.index),
      namedWidget(widgets, newWidget(type as string)),
      ...items.slice(place.index),
    ])
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
      namedWidget(widgets, boundWidget(names, draft, binding, type)),
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
      onDragStart={start}
      onDragOver={dragOver}
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
            onMove={keyMove}
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
      <DragOverlay dropAnimation={null}>
        {dragging === undefined ? null : (
          <Flex vertical align="flex-start">
            <Button>{dragging.label}</Button>
            {dragging.refusal !== undefined && (
              <Typography.Text type="danger" data-testid="studio-drag-refusal">
                {dragging.refusal}
              </Typography.Text>
            )}
          </Flex>
        )}
      </DragOverlay>
    </DndContext>
  )
}
