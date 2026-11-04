import { CloseOutlined, DeleteOutlined, FilterOutlined, PlusOutlined } from '@ant-design/icons'
import { Badge, Button, Flex, Input, Modal, Select, Tag, Typography } from 'antd'
import { useState } from 'react'
import { AxisButton } from './AxisButton'
import { dialogWidth } from './dialogButtons'
import {
  countRules,
  emptyFilter,
  operatorsOf,
  pruned,
  takesNoValue,
  type FilterField,
  type FilterGroup,
  type FilterRule,
} from './tableControls'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'

function newRule(fields: readonly FilterField[]): FilterRule {
  const field = fields[0]
  return {
    kind: 'rule',
    field: field.key,
    operator: operatorsOf[field.type][0],
    value: field.type === 'enum' ? [] : '',
  }
}

function RuleEditor({
  rule,
  fields,
  onChange,
  onRemove,
}: {
  rule: FilterRule
  fields: readonly FilterField[]
  onChange: (rule: FilterRule) => void
  onRemove: () => void
}) {
  const t = useText()
  const field = fields.find((candidate) => candidate.key === rule.field) ?? fields[0]
  return (
    <Flex gap="small" align="center" wrap data-testid="table-filter-rule">
      <Select
        aria-label={t('shell.filter.field')}
        style={{ width: 180 }}
        value={field.key}
        options={fields.map((candidate) => ({ value: candidate.key, label: candidate.label }))}
        onChange={(key) => {
          const next = fields.find((candidate) => candidate.key === key)!
          onChange({ ...newRule([next]) })
        }}
      />
      <Select
        aria-label={t('shell.filter.operator')}
        style={{ width: 170 }}
        value={rule.operator}
        options={operatorsOf[field.type].map((operator) => ({
          value: operator,
          label: t(`shell.filter.operator.${operator}`),
        }))}
        onChange={(operator) => onChange({ ...rule, operator })}
      />
      {!takesNoValue(rule.operator) &&
        (field.type === 'enum' ? (
          <Select
            mode="multiple"
            aria-label={t('shell.filter.value')}
            style={{ flex: 1, minWidth: 180 }}
            value={rule.value as string[]}
            options={field.options}
            onChange={(value) => onChange({ ...rule, value })}
          />
        ) : (
          <Input
            aria-label={t('shell.filter.value')}
            style={{ flex: 1, minWidth: 180 }}
            type={field.type === 'text' ? 'text' : field.type}
            value={rule.value as string}
            onChange={(event) => onChange({ ...rule, value: event.target.value })}
          />
        ))}
      <AxisButton role="icon" size="small" nameKey="shell.filter.remove" icon={<DeleteOutlined />} onClick={onRemove} />
    </Flex>
  )
}

function GroupEditor({
  group,
  fields,
  onChange,
  onRemove,
}: {
  group: FilterGroup
  fields: readonly FilterField[]
  onChange: (group: FilterGroup) => void
  onRemove?: () => void
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const replace = (index: number, item: FilterRule | FilterGroup) =>
    onChange({ ...group, items: group.items.map((current, place) => (place === index ? item : current)) })
  const remove = (index: number) => onChange({ ...group, items: group.items.filter((_, place) => place !== index) })
  return (
    <Flex
      vertical
      gap="small"
      data-testid="table-filter-group"
      style={
        onRemove ? { paddingInlineStart: spacing.md, borderInlineStart: `2px solid ${colour.borderSubtle}` } : undefined
      }
    >
      <Flex gap="small" align="center">
        <Typography.Text>{t('shell.filter.match')}</Typography.Text>
        <Select
          aria-label={t('shell.filter.match')}
          style={{ width: 110 }}
          value={group.join}
          options={[
            { value: 'and', label: t('shell.filter.all') },
            { value: 'or', label: t('shell.filter.any') },
          ]}
          onChange={(join) => onChange({ ...group, join })}
        />
        {onRemove && (
          <AxisButton
            role="icon"
            size="small"
            nameKey="shell.filter.removeGroup"
            icon={<DeleteOutlined />}
            onClick={onRemove}
          />
        )}
      </Flex>
      {group.items.map((item, index) =>
        item.kind === 'rule' ? (
          <RuleEditor
            key={index}
            rule={item}
            fields={fields}
            onChange={(next) => replace(index, next)}
            onRemove={() => remove(index)}
          />
        ) : (
          <GroupEditor
            key={index}
            group={item}
            fields={fields}
            onChange={(next) => replace(index, next)}
            onRemove={() => remove(index)}
          />
        ),
      )}
      <Flex gap="small">
        <Button
          type="text"
          size="small"
          icon={<PlusOutlined />}
          onClick={() => onChange({ ...group, items: [...group.items, newRule(fields)] })}
        >
          {t('shell.filter.addCondition')}
        </Button>
        <Button
          type="text"
          size="small"
          icon={<PlusOutlined />}
          onClick={() =>
            onChange({
              ...group,
              items: [...group.items, { kind: 'group', join: 'or', items: [newRule(fields)] }],
            })
          }
        >
          {t('shell.filter.addGroup')}
        </Button>
      </Flex>
    </Flex>
  )
}

/**
 * Proposed: a table's Filter button and its dialog, a builder of conditions in groups of And or Or, to any depth. Any column
 * can be filtered on. Apply puts the filter in force, and a condition with no value yet is left out.
 */
export function TableFilterButton({
  fields,
  filter,
  onApply,
}: {
  fields: readonly FilterField[]
  filter: FilterGroup
  onApply: (filter: FilterGroup) => void
}) {
  const t = useText()
  const { colour } = useDesignTokens()
  const [draft, setDraft] = useState<FilterGroup>()
  const count = countRules(filter)
  return (
    <>
      {/* Proposed: a small toolbar action is an icon, as Refresh is, with the count of conditions in force on it. */}
      <Badge count={count} size="small" color={colour.primary}>
        <AxisButton
          role="icon"
          nameKey="shell.filter.button"
          nameSuffix={count > 0 ? t('shell.filter.buttonCount').replace('{count}', String(count)) : undefined}
          icon={<FilterOutlined />}
          data-testid="table-filter"
          onClick={() => setDraft(filter.items.length > 0 ? filter : { ...emptyFilter, items: [newRule(fields)] })}
        />
      </Badge>
      <Modal
        open={draft !== undefined}
        width={dialogWidth('large')}
        title={t('shell.filter.title')}
        okText={t('shell.filter.apply')}
        onCancel={() => setDraft(undefined)}
        onOk={() => {
          onApply(pruned(draft!))
          setDraft(undefined)
        }}
      >
        {draft && <GroupEditor group={draft} fields={fields} onChange={setDraft} />}
      </Modal>
    </>
  )
}

/** Proposed: what a condition says in words, for its chip. */
function useRuleWords(fields: readonly FilterField[]) {
  const t = useText()
  return (rule: FilterRule) => {
    const field = fields.find((candidate) => candidate.key === rule.field)
    const values = Array.isArray(rule.value)
      ? rule.value.map((value) => field?.options?.find((option) => option.value === value)?.label ?? value).join(', ')
      : rule.value
    const operator = t(`shell.filter.operator.${rule.operator}`)
    return [field?.label ?? rule.field, operator, takesNoValue(rule.operator) ? '' : values].filter(Boolean).join(' ')
  }
}

/**
 * Proposed: the filter in force, under the toolbar: a chip for each condition, and one for each group, and Clear all. With an
 * Or at the top, the conditions stand together in one chip, as taking one away would change what the others mean.
 */
export function TableFilterChips({
  fields,
  filter,
  onChange,
}: {
  fields: readonly FilterField[]
  filter: FilterGroup
  onChange: (filter: FilterGroup) => void
}) {
  const t = useText()
  const words = useRuleWords(fields)
  if (filter.items.length === 0) {
    return null
  }
  const remove = (index: number) => onChange({ ...filter, items: filter.items.filter((_, place) => place !== index) })
  return (
    <Flex gap="small" align="center" wrap data-testid="table-filter-chips">
      {filter.join === 'or' ? (
        <Tag closable closeIcon={<CloseOutlined />} onClose={() => onChange(emptyFilter)}>
          {t('shell.filter.group').replace('{count}', String(countRules(filter)))}
        </Tag>
      ) : (
        filter.items.map((item, index) => (
          <Tag key={index} closable closeIcon={<CloseOutlined />} onClose={() => remove(index)}>
            {item.kind === 'rule' ? words(item) : t('shell.filter.group').replace('{count}', String(countRules(item)))}
          </Tag>
        ))
      )}
      <Button type="link" size="small" data-testid="table-filter-clear" onClick={() => onChange(emptyFilter)}>
        {t('shell.filter.clear')}
      </Button>
    </Flex>
  )
}
