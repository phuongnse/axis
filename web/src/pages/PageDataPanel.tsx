import { useDraggable } from '@dnd-kit/core'
import { Button, Flex, Modal, Typography } from 'antd'
import { useContext } from 'react'
import { bindingTypes, paletteLimit, type DataBinding } from '../platform/pageWidgets'
import { StudioNamesContext } from '../platform/studioNames'
import { useText } from '../platform/texts'
import { paletteReasons } from './pagePaletteSupport'

/**
 * The data panel of the Design tab: the application's entities and data sources, in two lists, each name a button. A
 * pointer drag from a name to the canvas is handled by the page designer around it. A click, Enter or Space calls
 * `onPick`, which is the keyboard way to bind. It shows nothing until Studio's names load. Module entities and data
 * sources are listed too, as the reference pickers list them.
 */
export function PageDataPanel({ onPick }: { onPick: (binding: DataBinding) => void }) {
  const t = useText()
  const names = useContext(StudioNamesContext)
  if (names === undefined) {
    return null
  }
  return (
    <div data-testid="studio-data-panel" role="group" aria-label={t('shell.studio.data')}>
      <Flex vertical gap="small">
        <Typography.Text strong>{t('shell.studio.data')}</Typography.Text>
        <DataList heading={t('shell.studio.data.entities')} kind="entity" items={names.entities} onPick={onPick} />
        <DataList
          heading={t('shell.studio.data.dataSources')}
          kind="dataSource"
          items={names.dataSources}
          onPick={onPick}
        />
      </Flex>
    </div>
  )
}

function DataList({
  heading,
  kind,
  items,
  onPick,
}: {
  heading: string
  kind: DataBinding['kind']
  items: { id: string; name: string }[]
  onPick: (binding: DataBinding) => void
}) {
  return (
    <Flex vertical gap={4}>
      <Typography.Text type="secondary">{heading}</Typography.Text>
      <Flex gap="small" wrap>
        {items.map((item) => (
          <DataName key={item.id} binding={{ kind, name: item.name }} onPick={onPick} />
        ))}
      </Flex>
    </Flex>
  )
}

/** One name of the panel. Only the library's listeners are on the button, as on a palette type, so it keeps its role. */
function DataName({ binding, onPick }: { binding: DataBinding; onPick: (binding: DataBinding) => void }) {
  const { setNodeRef, listeners } = useDraggable({ id: `data:${binding.kind}:${binding.name}`, data: { binding } })
  return (
    <Button
      ref={setNodeRef}
      {...listeners}
      data-testid={`studio-data-${binding.kind}-${binding.name}`}
      onClick={() => onPick(binding)}
    >
      {binding.name}
    </Button>
  )
}

/**
 * The choice of the widgets that can show a binding: one button per type, and Cancel. A type that a page limit forbids
 * in the list where the widget would land is disabled, with the palette's reason as its description. The dialog is
 * closed while `binding` is undefined.
 */
export function BindingChoice({
  binding,
  widgets,
  list,
  onChoose,
  onCancel,
}: {
  binding: DataBinding | undefined
  widgets: unknown[]
  /** The list where the widget would land. */
  list: string
  onChoose: (type: string) => void
  onCancel: () => void
}) {
  const t = useText()
  const names = useContext(StudioNamesContext)
  return (
    <Modal
      open={binding !== undefined}
      centered
      title={binding === undefined ? '' : `${t('shell.studio.data.bind')} ${binding.name}`}
      footer={<Button onClick={onCancel}>{t('shell.studio.data.cancel')}</Button>}
      onCancel={onCancel}
    >
      {binding !== undefined && names !== undefined && (
        <Flex gap="small" wrap data-testid="studio-binding-choice">
          {bindingTypes(names, binding).map((type) => {
            const limit = paletteLimit(widgets, list, type)
            const reasonId = `studio-binding-reason-${type}`
            return (
              <Flex vertical key={type}>
                <Button
                  disabled={limit !== undefined}
                  aria-describedby={limit === undefined ? undefined : reasonId}
                  data-testid={`studio-binding-${type}`}
                  onClick={() => onChoose(type)}
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
          })}
        </Flex>
      )}
    </Modal>
  )
}
