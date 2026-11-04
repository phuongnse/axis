import { ArrowDownOutlined, ArrowUpOutlined } from '@ant-design/icons'
import { Button, Checkbox, Flex, Modal, Typography } from 'antd'
import { useState } from 'react'
import { AxisButton } from './AxisButton'
import { dialogWidth } from './dialogButtons'
import { useText } from './texts'

/** Proposed: the columns a person chose: their keys in order, and which of them they hid. */
export interface ColumnChoice {
  order: string[]
  hidden: string[]
}

/**
 * Proposed: reads a choice from the address, as `order` with a leading `-` on a hidden column. A key the table no longer has
 * is dropped, and a column the address does not name keeps its place at the end, so a new column always shows.
 */
export function parseColumnChoice(value: string | null, keys: readonly string[]): ColumnChoice {
  const entries = (value ?? '').split(',').filter(Boolean)
  const named = entries.map((entry) => entry.replace(/^-/, '')).filter((key) => keys.includes(key))
  return {
    order: [...named, ...keys.filter((key) => !named.includes(key))],
    hidden: entries.filter((entry) => entry.startsWith('-')).map((entry) => entry.slice(1)),
  }
}

export const writeColumnChoice = ({ order, hidden }: ColumnChoice) =>
  order.map((key) => (hidden.includes(key) ? `-${key}` : key)).join(',')

/** Proposed: the columns to draw: the identifying one first, then the others as chosen. */
export function chosenColumns<T extends { key: string }>(columns: readonly T[], choice: ColumnChoice): T[] {
  const [first, ...others] = columns
  const shown = choice.order
    .filter((key) => key !== first.key && !choice.hidden.includes(key))
    .flatMap((key) => others.filter((column) => column.key === key))
  return [first, ...shown]
}

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
