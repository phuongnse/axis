import { ArrowDownOutlined, ArrowUpOutlined, TableOutlined } from '@ant-design/icons'
import { Checkbox, Flex, Modal } from 'antd'
import { useState } from 'react'
import { AxisActionRow, AxisButton } from './AxisButton'
import { columnsChoice } from './tableQuery'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'

/** A column of a table that can be shown, hidden and ordered. */
export interface ArrangeColumn {
  key: string
  title: string
}

/** One line of the dialog: a column and whether it shows. */
interface Line {
  key: string
  shown: boolean
}

/**
 * The Columns button of a table and the dialog it opens, see docs/reference/actions-and-controls.md#tables. The dialog
 * lists the shown columns in order, then the hidden ones. The first column identifies the row, so it stays first and
 * shown. Reset sets the dialog back to every column in its first order, and only Apply puts the choice in force.
 */
export function TableColumnsButton({
  columns,
  shown,
  onApply,
}: {
  columns: readonly ArrangeColumn[]
  shown: readonly string[]
  onApply: (next: string[] | undefined) => void
}) {
  const t = useText()
  const { size } = useDesignTokens()
  const [open, setOpen] = useState(false)
  const [draft, setDraft] = useState<Line[]>([])
  const keys = columns.map((column) => column.key)
  const titles = new Map(columns.map((column) => [column.key, column.title]))
  const lines = (): Line[] => [
    ...shown.map((key) => ({ key, shown: true })),
    ...keys.filter((key) => !shown.includes(key)).map((key) => ({ key, shown: false })),
  ]
  const swap = (index: number, other: number) =>
    setDraft((current) =>
      current.map((line, at) => (at === index ? current[other] : at === other ? current[index] : line)),
    )
  return (
    <>
      <AxisButton
        role="icon"
        nameKey="shell.table.columns"
        icon={<TableOutlined />}
        onClick={() => {
          setDraft(lines())
          setOpen(true)
        }}
      />
      <Modal
        open={open}
        width={size.dialog.medium}
        title={t('shell.table.columns')}
        okText={t('shell.table.columnsApply')}
        cancelText={t('shell.form.cancel')}
        onOk={() => {
          onApply(
            columnsChoice(
              keys,
              draft.filter((line) => line.shown).map((line) => line.key),
            ),
          )
          setOpen(false)
        }}
        onCancel={() => setOpen(false)}
        footer={(_origin, { OkBtn, CancelBtn }) => (
          <AxisActionRow justify="end">
            <AxisButton role="secondary" onClick={() => setDraft(keys.map((key) => ({ key, shown: true })))}>
              {t('shell.table.columnsReset')}
            </AxisButton>
            <CancelBtn />
            <OkBtn />
          </AxisActionRow>
        )}
        destroyOnHidden
      >
        <Flex vertical gap="small">
          {draft.map((line, index) => (
            <Flex key={line.key} align="center" justify="space-between" gap="small">
              <Checkbox
                checked={line.shown}
                disabled={index === 0}
                onChange={(event) =>
                  setDraft((current) =>
                    current.map((other, at) => (at === index ? { ...other, shown: event.target.checked } : other)),
                  )
                }
              >
                {titles.get(line.key)}
              </Checkbox>
              {index > 0 && (
                <Flex gap="small">
                  <AxisButton
                    role="icon"
                    nameKey="shell.table.columnsMoveUp"
                    nameSuffix={titles.get(line.key)}
                    icon={<ArrowUpOutlined />}
                    disabled={index === 1}
                    onClick={() => swap(index, index - 1)}
                  />
                  <AxisButton
                    role="icon"
                    nameKey="shell.table.columnsMoveDown"
                    nameSuffix={titles.get(line.key)}
                    icon={<ArrowDownOutlined />}
                    disabled={index === draft.length - 1}
                    onClick={() => swap(index, index + 1)}
                  />
                </Flex>
              )}
            </Flex>
          ))}
        </Flex>
      </Modal>
    </>
  )
}
