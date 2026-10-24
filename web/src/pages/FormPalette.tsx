import { useDraggable } from '@dnd-kit/core'
import { Button, Flex, Typography } from 'antd'
import { useContext, type PointerEventHandler } from 'react'
import { insertionPlace, paletteFields } from '../platform/formLayout'
import type { StudioNameField } from '../platform/studio'
import { StudioNamesContext } from '../platform/studioNames'
import { useText } from '../platform/texts'
import { insertField } from './formPaletteSupport'
import { visuallyHidden } from './pagePaletteSupport'
import type { ChangeHandler } from './SchemaForm'

/**
 * The palette of a form: the fields of its entity that no section lists, child collections included, read from
 * Studio's names. A click, Enter or Space on a field inserts it at the end of the selected section, after the
 * selected field, or at the end of the selected step's first section. With nothing selected it goes to the end of the
 * first section. The new field is selected and leaves the palette. A pointer drag from a field to the outline is
 * handled by the form designer around it. A hidden live region shows `announcement`, which names the field, its
 * section and its position.
 */
export function FormPalette({
  draft,
  selected,
  announcement,
  onAnnounce,
  onSelect,
  onChange,
}: {
  draft: Record<string, unknown>
  selected: string | null
  announcement: string
  onAnnounce: (text: string) => void
  onSelect: (pointer: string) => void
  onChange: ChangeHandler
}) {
  const t = useText()
  const names = useContext(StudioNamesContext)
  const fields = paletteFields(draft, names)
  const place = insertionPlace(draft, selected)

  return (
    <div data-testid="studio-form-palette" role="group" aria-label={t('shell.studio.design.fields')}>
      <Flex vertical gap="small">
        <Typography.Text strong>{t('shell.studio.design.fields')}</Typography.Text>
        {fields.length === 0 ? (
          <Typography.Text type="secondary">{t('shell.studio.design.allFieldsPlaced')}</Typography.Text>
        ) : (
          <Flex gap="small" wrap>
            {fields.map((field) => (
              <PaletteField
                key={field.name}
                field={field}
                disabled={place === undefined}
                onInsert={() => {
                  if (place !== undefined) {
                    insertField(t, draft, field.name, place, onChange, onSelect, onAnnounce)
                  }
                }}
              />
            ))}
          </Flex>
        )}
        <div role="status" aria-live="polite" data-testid="studio-form-palette-announcement" style={visuallyHidden}>
          {announcement}
        </div>
      </Flex>
    </div>
  )
}

/**
 * One field of the palette: a button that inserts it, with its type beside the name. Only the library's pointer listener
 * is on the button, not its attributes or its keyboard listener, so the button keeps its own role and Enter or Space
 * inserts the field.
 */
function PaletteField({
  field,
  disabled,
  onInsert,
}: {
  field: StudioNameField
  disabled: boolean
  onInsert: () => void
}) {
  const { setNodeRef, listeners } = useDraggable({
    id: `form-palette:${field.name}`,
    data: { field: field.name },
    disabled,
  })
  return (
    <Button
      ref={setNodeRef}
      onPointerDown={listeners?.onPointerDown as PointerEventHandler<HTMLElement> | undefined}
      disabled={disabled}
      data-testid={`studio-form-palette-${field.name}`}
      onClick={onInsert}
    >
      {field.name}{' '}
      <Typography.Text type="secondary" style={{ fontSize: 'inherit' }}>
        {field.type}
      </Typography.Text>
    </Button>
  )
}
