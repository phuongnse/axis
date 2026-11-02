import { useDraggable } from '@dnd-kit/core'
import { Button, Flex, Typography } from 'antd'
import { getAt } from '../platform/jsonPatch'
import {
  addableTypes,
  insertionPlace,
  namedWidget,
  newWidget,
  paletteLimit,
  widgetPointerOf,
} from '../platform/pageWidgets'
import { useText } from '../platform/texts'
import { landedText, paletteReasons, usePartLabel, visuallyHidden } from './pagePaletteSupport'
import type { ChangeHandler } from './SchemaForm'

type Content = Record<string, unknown>

/**
 * The palette of the ten widget types. A click, Enter or Space on a type inserts a new widget at the selection and
 * selects it: after a leaf, at the end of a container's first list, or at the end of the page's list when the page has
 * no widgets. A pointer drag from a type to the canvas is handled by the page designer around it. A hidden live region
 * shows `announcement`, which names the list and position where a widget landed. A type the page cannot take under
 * the limits of D61 is disabled, with its reason beside it, and cannot be dragged. A pointer that names no widget
 * selects the first widget, as the tree does. A pointer to an action of a widget counts as that widget.
 */
export function PagePalette({
  value,
  selected: pointer,
  announcement,
  onAnnounce,
  onSelect,
  onChange,
}: {
  value: Content
  selected: string
  announcement: string
  onAnnounce: (text: string) => void
  onSelect: (pointer: string) => void
  onChange: ChangeHandler
}) {
  const t = useText()
  const partLabel = usePartLabel()
  const widgets = Array.isArray(value.widgets) ? value.widgets : undefined
  if (widgets === undefined) {
    return null
  }
  const widgetPointer = widgetPointerOf(pointer)
  const selected = getAt(value, widgetPointer) === undefined ? '/widgets/0' : widgetPointer
  const place = insertionPlace(widgets, selected)

  function insert(type: string) {
    if (widgets === undefined) {
      return
    }
    const items = getAt({ widgets }, place.list) as unknown[]
    onChange(place.list, [
      ...items.slice(0, place.index),
      namedWidget(widgets, newWidget(type)),
      ...items.slice(place.index),
    ])
    onSelect(`${place.list}/${place.index}`)
    onAnnounce(landedText(t, partLabel, widgets, place.list, place.index, type))
  }

  return (
    <div data-testid="studio-palette" role="group" aria-label={t('shell.studio.palette')}>
      <Flex vertical gap="small">
        <Typography.Text strong>{t('shell.studio.palette')}</Typography.Text>
        <Flex gap="small" wrap>
          {addableTypes.map((type) => (
            <PaletteType
              key={type}
              type={type}
              limit={paletteLimit(widgets, place.list, type)}
              onInsert={() => insert(type)}
            />
          ))}
        </Flex>
        <div role="status" aria-live="polite" data-testid="studio-palette-announcement" style={visuallyHidden}>
          {announcement}
        </div>
      </Flex>
    </div>
  )
}

/**
 * One type of the palette: a button that inserts it, and the reason beside it when the page cannot take it. A type
 * without a limit can be dragged. Only the library's listeners are on the button, not its attributes, so the button
 * keeps its own role and its reason as the description.
 */
function PaletteType({
  type,
  limit,
  onInsert,
}: {
  type: string
  limit: keyof typeof paletteReasons | undefined
  onInsert: () => void
}) {
  const t = useText()
  const { setNodeRef, listeners } = useDraggable({
    id: `palette:${type}`,
    data: { type },
    disabled: limit !== undefined,
  })
  const reasonId = `studio-palette-reason-${type}`
  return (
    <Flex vertical>
      <Button
        ref={setNodeRef}
        {...listeners}
        disabled={limit !== undefined}
        aria-describedby={limit === undefined ? undefined : reasonId}
        data-testid={`studio-palette-${type}`}
        onClick={onInsert}
      >
        {type}
      </Button>
      {limit !== undefined && (
        <Typography.Text type="secondary" id={reasonId}>
          {t(paletteReasons[limit])}
        </Typography.Text>
      )}
    </Flex>
  )
}
