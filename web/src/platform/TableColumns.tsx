import { ArrowDownOutlined, ArrowUpOutlined } from '@ant-design/icons'
import { Button, Checkbox, Flex, Modal, Typography } from 'antd'
import { useState } from 'react'
import { AxisButton } from './AxisButton'
import { dialogWidth } from './dialogButtons'
import type { ColumnChoice } from './tableControls'
import { useText } from './texts'

/**
 * Proposed: a table's Columns… button and its dialog, where a person shows, hides and orders the columns. The identifying
 * column stays first and always shows, so a row can still be told and opened.
 */
export function TableColumnsButton({
  columns,
  choice,
  onApply,
}: {
  columns: readonly { key: string; label: string }[]
  choice: ColumnChoice
  onApply: (choice: ColumnChoice | null) => void
}) {
  const t = useText()
  const [draft, setDraft] = useState<ColumnChoice>()
  const [first, ...others] = columns
  const labelOf = (key: string) => columns.find((column) => column.key === key)?.label ?? key
  const order = (draft?.order ?? []).filter((key) => key !== first.key)
  const move = (index: number, by: number) => {
    const next = [...order]
    const [key] = next.splice(index, 1)
    next.splice(index + by, 0, key)
    setDraft({ ...draft!, order: [first.key, ...next] })
  }
  return (
    <>
      <Button data-testid="table-columns" onClick={() => setDraft(choice)}>
        {t('shell.table.columns')}
      </Button>
      <Modal
        open={draft !== undefined}
        width={dialogWidth('small')}
        title={t('shell.table.columns.title')}
        okText={t('shell.filter.apply')}
        onCancel={() => setDraft(undefined)}
        onOk={() => {
          onApply(draft!)
          setDraft(undefined)
        }}
        footer={(buttons) => (
          <Flex justify="space-between">
            <Button
              type="text"
              data-testid="table-columns-reset"
              onClick={() => {
                onApply(null)
                setDraft(undefined)
              }}
            >
              {t('shell.table.columns.reset')}
            </Button>
            <Flex gap="small">{buttons}</Flex>
          </Flex>
        )}
      >
        {draft && (
          <Flex vertical gap="small" data-testid="table-columns-list">
            <Checkbox checked disabled>
              {first.label}
            </Checkbox>
            {order.map((key, index) => (
              <Flex key={key} justify="space-between" align="center">
                <Checkbox
                  checked={!draft.hidden.includes(key)}
                  onChange={(event) =>
                    setDraft({
                      ...draft,
                      hidden: event.target.checked
                        ? draft.hidden.filter((hidden) => hidden !== key)
                        : [...draft.hidden, key],
                    })
                  }
                >
                  {labelOf(key)}
                </Checkbox>
                <Flex gap="small">
                  <AxisButton
                    role="icon"
                    size="small"
                    nameKey="shell.table.columns.up"
                    nameSuffix={labelOf(key)}
                    icon={<ArrowUpOutlined />}
                    disabled={index === 0}
                    onClick={() => move(index, -1)}
                  />
                  <AxisButton
                    role="icon"
                    size="small"
                    nameKey="shell.table.columns.down"
                    nameSuffix={labelOf(key)}
                    icon={<ArrowDownOutlined />}
                    disabled={index === others.length - 1}
                    onClick={() => move(index, 1)}
                  />
                </Flex>
              </Flex>
            ))}
            <Typography.Text type="secondary">{t('shell.table.columns.help')}</Typography.Text>
          </Flex>
        )}
      </Modal>
    </>
  )
}
