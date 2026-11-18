import { Flex, Input, Select, Typography } from 'antd'
import type { HTMLAttributes } from 'react'
import { useEffect, useMemo, useState } from 'react'
import { AxisButton } from '../platform/AxisButton'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
import { SectionPart } from '../platform/SectionParts'
import { parseNumberText, seedColumns, seedValuePointer, type SeedField } from '../platform/seedValues'
import type { FieldMetadata } from '../platform/site'
import { recordPageSize } from '../platform/tableQuery'
import { useText } from '../platform/texts'
import { useTableView } from '../platform/useTableView'
import { newUuid } from '../platform/uuid'
import { LayerMarkTag, RemovedRows, type DesignerLayer } from './LayerMarkViews'
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
  /** The id and description that `AxisField` gives its input. */
  id?: string
  'aria-describedby'?: string
}

/** A number cell keeps the text as typed, because `1.50` and `1.` are not the number the text stands for. */
function NumberCell({ field, value, problem, onChange, id, 'aria-describedby': describedBy }: CellProps) {
  const type = field.type === 'integer' ? 'integer' : 'decimal'
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
      id={id}
      aria-describedby={describedBy}
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

/** The seed grid keeps no data source parameter in the address, only its view. */
const noParameters: string[] = []

/** Joins the columns of the view into one text that tells whether they changed. A name never holds these. */
const nameSeparator = '\u0000'
const typeSeparator = '\u0001'

/**
 * The fields of the view: the Id, then the columns that `signature` lists as a name and a type each. A signature keeps
 * the list the same between draws, as `useTableView` needs.
 */
function viewFieldsOf(signature: string): Pick<FieldMetadata, 'name' | 'type'>[] {
  const columns = signature === '' ? [] : signature.split(nameSeparator)
  return [
    { name: 'id', type: 'text' },
    ...columns.map((column) => {
      const [name, type] = column.split(typeSeparator)
      return { name, type: type as FieldMetadata['type'] }
    }),
  ]
}

/** The value of a seed cell as a number, or null when it holds none. */
function numberOf(value: unknown): number | null {
  return typeof value === 'number' ? value : null
}

/** The value of a seed cell as text, or null when it holds none. A value of the wrong type is null. */
function textOf(value: unknown): string | null {
  return typeof value === 'string' ? value : null
}

/**
 * What a seed column filters on, by the field's type, see docs/reference/studio-form-editors.md#seed-editor. A value of the wrong
 * type is empty. A date-time filters as a date, on its first ten characters, as a timestamp does elsewhere.
 */
function seedFilter(field: SeedField, yes: string, no: string): NonNullable<AxisColumn<Row>['filter']> {
  const read = (row: Row) => (Object.hasOwn(row.values, field.name) ? row.values[field.name] : undefined)
  switch (field.type) {
    case 'integer':
    case 'decimal':
    case 'money':
      return { kind: 'number', value: (row) => numberOf(read(row)) }
    case 'boolean':
      return {
        kind: 'enum',
        value: (row) => {
          const value = read(row)
          return typeof value === 'boolean' ? String(value) : null
        },
        options: [
          { value: 'true', label: yes },
          { value: 'false', label: no },
        ],
      }
    case 'enum':
      return {
        kind: 'enum',
        value: (row) => textOf(read(row)),
        options: (field.values ?? []).map((value) => ({ value, label: value })),
      }
    case 'date':
      return { kind: 'date', value: (row) => textOf(read(row)) }
    case 'date-time':
      return { kind: 'date', value: (row) => textOf(read(row))?.slice(0, 10) ?? null }
    default:
      return { kind: 'text', value: (row) => textOf(read(row)) }
  }
}

/** Whether the cell of the field is the plain text input, which `FieldCell` draws for every type it has no input for. */
function isTextLike(field: SeedField): boolean {
  return !['integer', 'decimal', 'money', 'boolean', 'enum'].includes(field.type)
}

export function FieldCell(props: CellProps) {
  const { field, value, problem, onChange, id, 'aria-describedby': describedBy } = props
  const status = problem ? ('error' as const) : undefined
  switch (field.type) {
    case 'integer':
    case 'decimal':
    case 'money':
      return <NumberCell {...props} />
    case 'boolean':
      return (
        <Select
          virtual={false}
          id={id}
          aria-describedby={describedBy}
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
          showSearch
          optionFilterProp="label"
          virtual={false}
          id={id}
          aria-describedby={describedBy}
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
          id={id}
          aria-describedby={describedBy}
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
 * show, such as a child collection, is not touched. With a `layer`, a record that the layer added or changed shows its
 * tag with Reset to module, and the records it removed show as muted rows below the grid. The grid has a Filter on the
 * Id and every field column, kept in the address under `records.`. A filter only hides rows: every row shows on one page,
 * an edit to a hidden row stays in the draft, and a record whose id is not among `savedIds`, the ids as last saved, stays
 * shown, see docs/reference/studio-form-editors.md#seed-editor. Its Add record sits on the right of the part's heading, see
 * docs/reference/studio-resource-form.md#sections.
 */
export function SeedGrid({
  fields,
  records,
  problems,
  onChange,
  layer,
  savedIds,
}: {
  fields: SeedField[]
  records: unknown
  /** The ids of the records as last saved. */
  savedIds: readonly unknown[]
  problems: Map<string, string>
  onChange: ChangeHandler
  layer?: DesignerLayer
}) {
  const t = useText()
  const signature = seedColumns(fields)
    .map((field) => `${field.name}${typeSeparator}${field.type}`)
    .join(nameSeparator)
  const viewFields = useMemo(() => viewFieldsOf(signature), [signature])
  const { view, setView } = useTableView(viewFields, recordPageSize, noParameters, false, true, false, 'records.')
  const list = Array.isArray(records) ? records : []
  const rows: Row[] = list.map((record, index) => ({
    index,
    id: isObject(record) ? record.id : undefined,
    values: isObject(record) && isObject(record.values) ? record.values : {},
  }))

  return (
    <SectionPart
      testName="records"
      heading={{ title: t('shell.studio.field.seed.records'), line: t('shell.studio.field.seed.records.help') }}
      action={
        <AxisButton role="secondary" onClick={() => onChange('/records', [...list, { id: newUuid(), values: {} }])}>
          {t('shell.studio.seed.addRecord')}
        </AxisButton>
      }
    >
      <div data-testid="studio-field-/records">
        <Flex vertical gap="small" data-testid="studio-seed-grid">
          <AxisTable<Row>
            rowKey={(row) => String(row.index)}
            dataSource={rows}
            view={{ query: view, onChange: setView, paging: false }}
            keep={(row) => !savedIds.includes(row.id)}
            emptyText={t('shell.table.empty')}
            rowAttributes={(row) =>
              ({ 'data-testid': 'studio-seed-row', 'data-record-id': display(row.id) }) as HTMLAttributes<HTMLElement>
            }
            columns={[
              {
                key: 'id',
                kind: 'fixed',
                title: t('shell.studio.seed.recordId'),
                filter: { kind: 'text', value: (row) => display(row.id) || null },
                render: (_, row) => <Typography.Text code>{display(row.id)}</Typography.Text>,
              },
              ...(layer === undefined
                ? []
                : [
                    {
                      key: 'layer',
                      kind: 'fixed' as const,
                      render: (_: unknown, row: Row) => (
                        <LayerMarkTag layer={layer} list="/records" index={row.index} name={display(row.id)} />
                      ),
                    },
                  ]),
              ...seedColumns(fields).map((field): AxisColumn<Row> => ({
                key: field.name,
                kind: isTextLike(field) ? 'freeText' : 'fixed',
                title: field.name,
                filter: seedFilter(field, t('shell.table.yes'), t('shell.table.no')),
                render: (_, row) => {
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
                kind: 'fixed',
                render: (_, row) => (
                  <AxisButton role="destructive" onClick={() => onChange(`/records/${row.index}`, undefined)}>
                    {t('shell.studio.seed.removeRecord')}
                  </AxisButton>
                ),
              },
            ]}
          />
          <RemovedRows layer={layer} list="/records" />
        </Flex>
      </div>
    </SectionPart>
  )
}
