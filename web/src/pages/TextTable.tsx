import { Button, Flex, Input, Tag, Typography } from 'antd'
import type { HTMLAttributes } from 'react'
import { useMemo, useState } from 'react'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
import { escapePointerSegment } from '../platform/jsonPatch'
import { recordPageSize } from '../platform/tableQuery'
import { useText } from '../platform/texts'
import { useTableView } from '../platform/useTableView'
import { LayerMarkTag, type DesignerLayer } from './LayerMarkViews'
import type { ChangeHandler } from './SchemaForm'

/** One locale of the table: the `texts` of a text resource, and whether the table edits it. */
export interface TextColumn {
  id: string
  locale: string
  texts: Record<string, unknown>
  editable: boolean
}

interface Row {
  key: string
}

function textOf(column: TextColumn, key: string): string {
  const value = column.texts[key]
  return typeof value === 'string' ? value : ''
}

/**
 * The text keys of an application as a table: a row per key and a column per locale, in the order of `columns`. Only
 * an editable column takes input, and it edits the draft through `onChange`. A key that a column lacks shows as
 * missing, and typing in an editable cell adds it. The search is AxisTable's own, see
 * docs/reference/actions-and-controls.md#tables: it keeps the rows whose key or text in any locale contains the
 * search, ignoring letter case, and lives in the address as `search`. Filter is there too, on the key and on each
 * locale's text, where a missing or empty text is empty, and lives in the address as `filter`. The table pages, so a key past page 1 needs a
 * search. `onAdd` writes a new key in every locale and answers how that went. With a `layer`, each key that the
 * layer changed or added shows its mark and Reset to module after the key.
 */
export function TextTable({
  columns,
  onChange,
  onAdd,
  layer,
  tableKey,
}: {
  columns: TextColumn[]
  onChange: ChangeHandler
  onAdd: (key: string, texts: Record<string, string>) => Promise<'added' | 'invalid' | 'failed'>
  layer?: DesignerLayer
  /** The table's key, see docs/reference/frontend.md#table-views-and-columns. */
  tableKey: string
}) {
  const t = useText()
  const [newKey, setNewKey] = useState('')
  const [newTexts, setNewTexts] = useState<Record<string, string>>({})
  const [adding, setAdding] = useState(false)
  const [problem, setProblem] = useState<string>()

  const keys = [...new Set(columns.flatMap((column) => Object.keys(column.texts)))].sort()
  // One text field for the key and one for each locale's column. The ids are joined so that the fields stay stable between renders.
  const ids = columns.map((column) => column.id).join('\n')
  const fields = useMemo(
    () => [
      { name: 'key', type: 'text' as const },
      ...(ids === '' ? [] : ids.split('\n')).map((name) => ({ name, type: 'text' as const })),
    ],
    [ids],
  )
  const { view, setView } = useTableView(fields, recordPageSize, [], true, true, false, '', tableKey)

  async function add() {
    const key = newKey.trim()
    if (key === '' || key.includes('/')) {
      setProblem(t('shell.studio.texts.keyInvalid'))
      return
    }
    if (keys.includes(key)) {
      setProblem(t('shell.studio.texts.keyExists'))
      return
    }
    setProblem(undefined)
    setAdding(true)
    try {
      const result = await onAdd(
        key,
        Object.fromEntries(columns.map((column) => [column.locale, newTexts[column.locale] ?? ''])),
      )
      if (result === 'added') {
        setNewKey('')
        setNewTexts({})
      } else {
        setProblem(t('shell.studio.texts.addFailed'))
      }
    } finally {
      setAdding(false)
    }
  }

  return (
    <Flex vertical gap="small">
      <AxisTable<Row>
        testId="studio-text-table"
        rowKey={(row) => row.key}
        dataSource={keys.map((key) => ({ key }))}
        view={{ query: view, onChange: setView }}
        emptyText={t('shell.table.empty')}
        rowAttributes={(row) =>
          ({ 'data-testid': 'studio-text-row', 'data-key': row.key }) as HTMLAttributes<HTMLElement>
        }
        columns={[
          {
            key: 'key',
            kind: 'fixed',
            title: t('shell.studio.texts.key'),
            searchText: (row) => row.key,
            filter: { kind: 'text', value: (row) => row.key },
            render: (_, row) => <Typography.Text code>{row.key}</Typography.Text>,
          },
          ...(layer
            ? [
                {
                  key: 'layer',
                  kind: 'fixed' as const,
                  render: (_: unknown, row: Row, index: number) => (
                    <LayerMarkTag layer={layer} list="/texts" index={index} name={row.key} />
                  ),
                },
              ]
            : []),
          ...columns.map((column): AxisColumn<Row> => ({
            key: column.id,
            kind: 'freeText',
            title: column.locale,
            searchText: (row) => textOf(column, row.key),
            filter: { kind: 'text', value: (row) => textOf(column, row.key) || null },
            render: (_, row) => {
              const missing = !Object.hasOwn(column.texts, row.key)
              if (!column.editable) {
                return missing ? (
                  <Tag>{t('shell.studio.texts.missing')}</Tag>
                ) : (
                  <Typography.Text>{textOf(column, row.key)}</Typography.Text>
                )
              }
              return (
                <Flex gap="small" align="center">
                  <Input
                    aria-label={column.locale}
                    value={textOf(column, row.key)}
                    onChange={(event) => onChange(`/texts/${escapePointerSegment(row.key)}`, event.target.value)}
                  />
                  {missing && <Tag>{t('shell.studio.texts.missing')}</Tag>}
                </Flex>
              )
            },
          })),
        ]}
      />
      <Flex gap="small" align="center" wrap data-testid="studio-text-add">
        <Input
          aria-label={t('shell.studio.texts.newKey')}
          placeholder={t('shell.studio.texts.newKey')}
          style={{ width: 240 }}
          value={newKey}
          onChange={(event) => setNewKey(event.target.value)}
        />
        {columns.map((column) => (
          <Input
            key={column.id}
            aria-label={column.locale}
            placeholder={column.locale}
            style={{ width: 240 }}
            value={newTexts[column.locale] ?? ''}
            onChange={(event) => setNewTexts((current) => ({ ...current, [column.locale]: event.target.value }))}
          />
        ))}
        {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
        <Button loading={adding} disabled={adding} onClick={add}>
          {t('shell.studio.texts.addKey')}
        </Button>
        {problem && (
          <Typography.Text type="danger" data-testid="studio-text-add-problem">
            {problem}
          </Typography.Text>
        )}
      </Flex>
    </Flex>
  )
}
