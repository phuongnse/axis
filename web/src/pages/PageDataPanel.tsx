import { useDraggable } from '@dnd-kit/core'
import { Button, Flex, Modal, Typography } from 'antd'
import { useContext } from 'react'
import { AxisFieldGroup } from '../platform/AxisFieldGroup'
import { EmptyState } from '../platform/EmptyState'
import { bindingTypes, paletteLimit, type DataBinding } from '../platform/pageWidgets'
import { StudioNamesContext } from '../platform/studioNames'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { paletteReasons } from './pagePaletteSupport'

/**
 * The data panel of the Design tab: the application's entities and data sources, in two titled lists, each name a
 * button. A list with no names says so in one line. A pointer drag from a name to the canvas is handled by the page
 * designer around it. A click, Enter or Space calls `onPick`, which is the keyboard way to bind. It shows nothing until
 * Studio's names load. Module entities and data sources are listed too, as the reference pickers list them.
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
        <DataList
          title={t('shell.studio.data.entities')}
          help={t('shell.studio.data.entities.help')}
          empty={t('shell.studio.data.noEntities')}
          kind="entity"
          items={names.entities}
          onPick={onPick}
        />
        <DataList
          title={t('shell.studio.data.dataSources')}
          help={t('shell.studio.data.dataSources.help')}
          empty={t('shell.studio.data.noDataSources')}
          kind="dataSource"
          items={names.dataSources}
          onPick={onPick}
        />
      </Flex>
    </div>
  )
}

function DataList({
  title,
  help,
  empty,
  kind,
  items,
  onPick,
}: {
  title: string
  help: string
  empty: string
  kind: DataBinding['kind']
  items: { id: string; name: string }[]
  onPick: (binding: DataBinding) => void
}) {
  return (
    <AxisFieldGroup title={title} help={help}>
      {items.length === 0 ? (
        <EmptyState text={empty} />
      ) : (
        <Flex gap="small" wrap>
          {items.map((item) => (
            <DataName key={item.id} binding={{ kind, name: item.name }} onPick={onPick} />
          ))}
        </Flex>
      )}
    </AxisFieldGroup>
  )
}

/** One name of the panel. Only the library's listeners are on the button, as on a palette type, so it keeps its role. */
function DataName({ binding, onPick }: { binding: DataBinding; onPick: (binding: DataBinding) => void }) {
  const { setNodeRef, listeners } = useDraggable({ id: `data:${binding.kind}:${binding.name}`, data: { binding } })
  return (
    // oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role.
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
  const { size } = useDesignTokens()
  const names = useContext(StudioNamesContext)
  return (
    <Modal
      open={binding !== undefined}
      width={size.dialog.medium}
      title={binding === undefined ? '' : `${t('shell.studio.data.bind')} ${binding.name}`}
      // oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role.
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
                {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
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
