import { Button, Flex, Input, Select, Table, Typography } from 'antd'
import type { HTMLAttributes } from 'react'
import { useEffect, useState } from 'react'
import { parseNumberText, seedColumns, seedValuePointer, type SeedField } from '../platform/seedValues'
import { useText } from '../platform/texts'
import { newUuid } from '../platform/uuid'
import type { ChangeHandler } from './SchemaForm'

interface Row {
  index: number
  id: unknown
  values: Record<string, unknown>
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

/** What a cell shows for a stored value. A value of the wrong type shows as written, so the author can see it. */
function display(value: unknown): string {
  if (value === undefined || value === null) {
    return ''
  }
  return typeof value === 'string' ? value : JSON.stringify(value)
}

const placeholders: Record<string, string> = {
  date: 'yyyy-MM-dd',
  'date-time': 'yyyy-MM-ddTHH:mm:ssZ',
  reference: '00000000-0000-0000-0000-000000000000',
}

interface CellProps {
  field: SeedField
  value: unknown
  problem: string | undefined
  onChange: (next: unknown) => void
}

/** A number cell keeps the text as typed, because `1.50` and `1.` are not the number the text stands for. */
function NumberCell({ field, value, problem, onChange }: CellProps) {
  const type = field.type as 'integer' | 'decimal'
  const [text, setText] = useState(display(value))

  useEffect(() => {
    // The value changed from elsewhere, for example in the JSON view or by a removed row above.
    const typed = text.trim() === '' ? undefined : parseNumberText(text, type)
    if (!Object.is(typed, value ?? undefined)) {
      setText(display(value))
    }
    // Only a new value matters, not the text that produced it.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [value])

  return (
    <Input
      aria-label={field.name}
      aria-invalid={problem !== undefined}
      status={problem ? 'error' : undefined}
      inputMode={type === 'integer' ? 'numeric' : 'decimal'}
      value={text}
      onChange={(event) => {
        setText(event.target.value)
        onChange(event.target.value.trim() === '' ? undefined : parseNumberText(event.target.value, type))
      }}
    />
  )
}

export function FieldCell(props: CellProps) {
  const { field, value, problem, onChange } = props
  const status = problem ? ('error' as const) : undefined
  switch (field.type) {
    case 'integer':
    case 'decimal':
      return <NumberCell {...props} />
    case 'boolean':
      return (
        <Select
          aria-label={field.name}
          aria-invalid={problem !== undefined}
          status={status}
          style={{ minWidth: 100 }}
          allowClear={!field.required}
          value={typeof value === 'boolean' ? String(value) : value == null ? undefined : display(value)}
          options={[
            { value: 'true', label: 'true' },
            { value: 'false', label: 'false' },
          ]}
          onChange={(next: string | undefined) => onChange(next === undefined ? undefined : next === 'true')}
        />
      )
    case 'enum':
      return (
        <Select
          aria-label={field.name}
          aria-invalid={problem !== undefined}
          status={status}
          style={{ minWidth: 140 }}
          allowClear={!field.required}
          value={value == null ? undefined : display(value)}
          options={(field.values ?? []).map((option) => ({ value: option, label: option }))}
          onChange={(next: string | undefined) => onChange(next)}
        />
      )
    default:
      return (
        <Input
          aria-label={field.name}
          aria-invalid={problem !== undefined}
          status={status}
          placeholder={placeholders[field.type]}
          value={display(value)}
          onChange={(event) => onChange(event.target.value === '' ? undefined : event.target.value)}
        />
      )
  }
}

/**
 * The records of a seed as a grid: a row per record and a column per field of the seed's entity that a seed may set.
 * `problems` holds the text key of each value that would fail seeding, by the pointer of the value. The grid edits
 * the draft through `onChange`. An empty cell removes the key from the record's `values`, and a value it does not
 * show, such as a child collection, is not touched.
 */
export function SeedGrid({
  fields,
  records,
  problems,
  onChange,
}: {
  fields: SeedField[]
  records: unknown
  problems: Map<string, string>
  onChange: ChangeHandler
}) {
  const t = useText()
  const list = Array.isArray(records) ? records : []
  const rows: Row[] = list.map((record, index) => ({
    index,
    id: isObject(record) ? record.id : undefined,
    values: isObject(record) && isObject(record.values) ? record.values : {},
  }))

  return (
    <div data-testid="studio-field-/records">
      <Flex vertical gap="small" data-testid="studio-seed-grid">
        <Typography.Text strong>{t('shell.studio.seed.records')}</Typography.Text>
        <Table<Row>
          size="small"
          pagination={false}
          scroll={{ x: 'max-content' }}
          dataSource={rows}
          rowKey="index"
          onRow={(row) =>
            ({ 'data-testid': 'studio-seed-row', 'data-record-id': display(row.id) }) as HTMLAttributes<HTMLElement>
          }
          columns={[
            {
              key: 'id',
              title: t('shell.studio.seed.recordId'),
              render: (_, row) => <Typography.Text code>{display(row.id)}</Typography.Text>,
            },
            ...seedColumns(fields).map((field) => ({
              key: field.name,
              title: field.name,
              render: (_: unknown, row: Row) => {
                const pointer = seedValuePointer(row.index, field.name)
                const problem = problems.get(pointer)
                return (
                  <div data-testid={`studio-field-${pointer}`}>
                    <FieldCell
                      field={field}
                      value={Object.hasOwn(row.values, field.name) ? row.values[field.name] : undefined}
                      problem={problem}
                      onChange={(next) => onChange(pointer, next)}
                    />
                    {problem && (
                      <Typography.Text type="danger" data-testid="studio-seed-problem">
                        {t(problem)}
                      </Typography.Text>
                    )}
                  </div>
                )
              },
            })),
            {
              key: 'remove',
              render: (_, row) => (
                <Button onClick={() => onChange(`/records/${row.index}`, undefined)}>
                  {t('shell.studio.seed.removeRecord')}
                </Button>
              ),
            },
          ]}
        />
        <div>
          <Button onClick={() => onChange('/records', [...list, { id: newUuid(), values: {} }])}>
            {t('shell.studio.seed.addRecord')}
          </Button>
        </div>
      </Flex>
    </div>
  )
}
