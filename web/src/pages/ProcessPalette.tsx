import { useDraggable } from '@dnd-kit/core'
import { Button, Flex, Typography } from 'antd'
import { useText } from '../platform/texts'
import { entryLabel, type PaletteEntry, paletteEntries } from './processPalette'

/**
 * The palette of the step types and the built-in operations that the process schema defines, with one entry for an
 * operation of an extension package. A click, Enter or Space on an entry calls `onInsert`. A pointer drag from an entry
 * to the canvas or to an edge is handled by the process designer around it.
 */
export function ProcessPalette({ onInsert }: { onInsert: (entry: PaletteEntry) => void }) {
  const t = useText()
  return (
    <div data-testid="studio-process-palette" role="group" aria-label={t('shell.studio.process.palette')}>
      <Flex vertical gap="small">
        <Typography.Text strong>{t('shell.studio.process.palette')}</Typography.Text>
        <Flex gap="small" wrap>
          {paletteEntries.map((entry) => (
            <PaletteType key={entry.key} entry={entry} onInsert={() => onInsert(entry)} />
          ))}
        </Flex>
      </Flex>
    </div>
  )
}

/** One entry of the palette. Only the library's listeners are on the button, so it keeps its own role. */
function PaletteType({ entry, onInsert }: { entry: PaletteEntry; onInsert: () => void }) {
  const t = useText()
  const { setNodeRef, listeners } = useDraggable({ id: `process-palette:${entry.key}`, data: { entry } })
  return (
    <Button ref={setNodeRef} {...listeners} data-testid={`studio-process-palette-${entry.key}`} onClick={onInsert}>
      {entryLabel(entry, t('shell.studio.process.packageOperation'))}
    </Button>
  )
}
