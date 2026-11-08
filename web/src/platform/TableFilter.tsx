import { CloseOutlined, FilterOutlined } from '@ant-design/icons'
import { Badge, DatePicker, Flex, Input, InputNumber, Modal, Segmented, Select, Tag } from 'antd'
import dayjs from 'dayjs'
import { useState } from 'react'
import { AxisButton } from './AxisButton'
import {
  countConditions,
  filterOps,
  groupMode,
  groupNodes,
  isGroup,
  isUnary,
  makeGroup,
  pruneFilter,
  type FilterCondition,
  type FilterGroup,
  type FilterKind,
  type FilterNode,
  type FilterValue,
} from './tableFilter'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'

/** A column of a table that can be filtered on. */
export interface FilterColumn {
  key: string
  title: string
  kind: FilterKind
  /** The values an enum column offers, with their labels. */
  options?: { value: string; label: string }[]
}

const dateFormat = 'YYYY-MM-DD'

/** A group of all its conditions with one blank condition on the first column. */
function blankFilter(columns: readonly FilterColumn[]): FilterGroup {
  return { all: [blankCondition(columns[0])] }
}

function blankCondition(column: FilterColumn): FilterCondition {
  return { field: column.key, op: filterOps[column.kind][0] }
}

/**
 * The Filter button of a table and the builder it opens, see docs/reference/actions-and-controls.md#tables. The button
 * carries the number of conditions in force, and Apply puts the builder's filter in force without the conditions that
 * have no value.
 */
export function TableFilterButton({
  columns,
  filter,
  onApply,
}: {
  columns: readonly FilterColumn[]
  filter: FilterGroup | undefined
  onApply: (next: FilterGroup | undefined) => void
}) {
  const t = useText()
  const { size } = useDesignTokens()
  const [open, setOpen] = useState(false)
  const [draft, setDraft] = useState<FilterGroup>(() => filter ?? blankFilter(columns))
  const count = countConditions(filter)
  return (
    <>
      <Badge count={count} size="small">
        <AxisButton
          role="icon"
          nameKey="shell.table.filter"
          icon={<FilterOutlined />}
          nameSuffix={count > 0 ? String(count) : undefined}
          onClick={() => {
            setDraft(filter ?? blankFilter(columns))
            setOpen(true)
          }}
        />
      </Badge>
      <Modal
        open={open}
        width={size.dialog.medium}
        title={t('shell.table.filter')}
        okText={t('shell.table.filterApply')}
        cancelText={t('shell.form.cancel')}
        onOk={() => {
          onApply(pruneFilter(draft))
          setOpen(false)
        }}
        onCancel={() => setOpen(false)}
        destroyOnHidden
      >
        <GroupEditor columns={columns} group={draft} onChange={setDraft} />
      </Modal>
    </>
  )
}

/** The builder of one group: its All or Any switch, its conditions and groups, and what adds more. */
function GroupEditor({
  columns,
  group,
  onChange,
  onRemove,
}: {
  columns: readonly FilterColumn[]
  group: FilterGroup
  onChange: (next: FilterGroup) => void
  onRemove?: () => void
}) {
  const t = useText()
  const nodes = groupNodes(group)
  const mode = groupMode(group)
  const replace = (index: number, node: FilterNode) =>
    onChange(
      makeGroup(
        mode,
        nodes.map((current, at) => (at === index ? node : current)),
      ),
    )
  const remove = (index: number) =>
    onChange(
      makeGroup(
        mode,
        nodes.filter((_, at) => at !== index),
      ),
    )
  return (
    <Flex vertical gap="small">
      <Flex gap="small" align="center">
        <Segmented<'all' | 'any'>
          value={mode}
          onChange={(next) => onChange(makeGroup(next, nodes))}
          options={[
            { value: 'all', label: t('shell.table.filterAll') },
            { value: 'any', label: t('shell.table.filterAny') },
          ]}
        />
        {onRemove && (
          <AxisButton role="icon" nameKey="shell.table.filterRemove" icon={<CloseOutlined />} onClick={onRemove} />
        )}
      </Flex>
      <Flex vertical gap="small" style={{ paddingInlineStart: 16 }}>
        {nodes.map((node, index) =>
          isGroup(node) ? (
            <GroupEditor
              key={index}
              columns={columns}
              group={node}
              onChange={(next) => replace(index, next)}
              onRemove={() => remove(index)}
            />
          ) : (
            <ConditionEditor
              key={index}
              columns={columns}
              condition={node}
              onChange={(next) => replace(index, next)}
              onRemove={() => remove(index)}
            />
          ),
        )}
        <Flex gap="small">
          <AxisButton role="link" onClick={() => onChange(makeGroup(mode, [...nodes, blankCondition(columns[0])]))}>
            {t('shell.table.filterAddCondition')}
          </AxisButton>
          <AxisButton
            role="link"
            onClick={() => onChange(makeGroup(mode, [...nodes, makeGroup('all', [blankCondition(columns[0])])]))}
          >
            {t('shell.table.filterAddGroup')}
          </AxisButton>
        </Flex>
      </Flex>
    </Flex>
  )
}

/** One condition: a column, an operator that fits its kind and, unless the operator takes none, a value. */
function ConditionEditor({
  columns,
  condition,
  onChange,
  onRemove,
}: {
  columns: readonly FilterColumn[]
  condition: FilterCondition
  onChange: (next: FilterCondition) => void
  onRemove: () => void
}) {
  const t = useText()
  const column = columns.find((candidate) => candidate.key === condition.field) ?? columns[0]
  return (
    <Flex gap="small" align="center" wrap>
      <Select
        aria-label={t('shell.table.filterColumn')}
        style={{ minWidth: 140 }}
        value={column.key}
        options={columns.map((candidate) => ({ value: candidate.key, label: candidate.title }))}
        onChange={(key) => {
          const next = columns.find((candidate) => candidate.key === key)
          if (next) {
            onChange(blankCondition(next))
          }
        }}
      />
      <Select
        aria-label={t('shell.table.filterOperator')}
        style={{ minWidth: 140 }}
        value={condition.op}
        options={filterOps[column.kind].map((op) => ({ value: op, label: t(`shell.table.filterOp.${op}`) }))}
        onChange={(op) => onChange(isUnary(op) ? { field: condition.field, op } : { ...condition, op })}
      />
      {!isUnary(condition.op) && (
        <ValueInput column={column} value={condition.value} onChange={(value) => onChange({ ...condition, value })} />
      )}
      <AxisButton role="icon" nameKey="shell.table.filterRemove" icon={<CloseOutlined />} onClick={onRemove} />
    </Flex>
  )
}

function ValueInput({
  column,
  value,
  onChange,
}: {
  column: FilterColumn
  value: FilterValue | undefined
  onChange: (next: FilterValue | undefined) => void
}) {
  const t = useText()
  const label = t('shell.table.filterValue')
  switch (column.kind) {
    case 'number':
      return (
        <InputNumber
          aria-label={label}
          value={typeof value === 'number' ? value : null}
          onChange={(next) => onChange(next ?? undefined)}
        />
      )
    case 'date':
      return (
        <DatePicker
          aria-label={label}
          format={dateFormat}
          value={typeof value === 'string' && value ? dayjs(value) : null}
          onChange={(_date, text) => onChange(typeof text === 'string' && text ? text : undefined)}
        />
      )
    case 'enum':
      return (
        <Select
          aria-label={label}
          style={{ minWidth: 140 }}
          value={typeof value === 'string' ? value : undefined}
          options={column.options ?? []}
          onChange={onChange}
        />
      )
    case 'text':
      return (
        <Input
          aria-label={label}
          value={typeof value === 'string' ? value : ''}
          onChange={(event) => onChange(event.target.value)}
        />
      )
  }
}

/**
 * The chips of the filter in force: one for each condition and group of its root, which closes to remove it, and Clear
 * all. `locale` formats a number.
 */
export function TableFilterChips({
  columns,
  filter,
  locale,
  onChange,
}: {
  columns: readonly FilterColumn[]
  filter: FilterGroup
  locale: string
  onChange: (next: FilterGroup | undefined) => void
}) {
  const t = useText()
  const nodes = groupNodes(filter)
  const number = new Intl.NumberFormat(locale)
  const text = (node: FilterNode): string => {
    if (isGroup(node)) {
      return t(groupMode(node) === 'all' ? 'shell.table.filterGroupAll' : 'shell.table.filterGroupAny').replace(
        '{count}',
        String(countConditions(node)),
      )
    }
    const column = columns.find((candidate) => candidate.key === node.field)
    const operator = t(`shell.table.filterOp.${node.op}`)
    const given =
      node.value === undefined || isUnary(node.op)
        ? undefined
        : typeof node.value === 'number'
          ? number.format(node.value)
          : (column?.options?.find((option) => option.value === node.value)?.label ?? node.value)
    return [column?.title ?? node.field, operator, given].filter((part) => part !== undefined).join(' ')
  }
  return (
    <Flex gap="small" align="center" wrap>
      {nodes.map((node, index) => (
        <Tag
          key={index}
          closable={{ 'aria-label': t('shell.table.filterRemove') }}
          onClose={(event) => {
            // The chip leaves with the filter, so Ant Design need not hide it.
            event.preventDefault()
            const rest = nodes.filter((_, at) => at !== index)
            onChange(rest.length > 0 ? makeGroup(groupMode(filter), rest) : undefined)
          }}
        >
          {text(node)}
        </Tag>
      ))}
      <AxisButton role="link" onClick={() => onChange(undefined)}>
        {t('shell.table.filterClearAll')}
      </AxisButton>
    </Flex>
  )
}
