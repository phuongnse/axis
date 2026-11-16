import { Flex, Input, Tabs, Tag, Typography } from 'antd'
import { useContext, useState } from 'react'
import { AxisButton } from '../platform/AxisButton'
import { PlatformContext } from './context'
import { EmptyState } from '../platform/EmptyState'
import type { ExpressionScope, StudioNames } from '../platform/studio'
import { labelText } from '../platform/studioNames'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { expressionOperators } from './expressionOperators'
import type { EntryKind, PanelEntry } from './expressionPanelEntries'
import { rankEntries, signatureOf } from './expressionPanelEntries'
import type { ScopeOption } from './expressionScope'
import { readExpression, type ReadingSources } from './expressionReading'
import { readingInline } from './readingInline'
import type { ExpressionStart } from './expressionStarts'

const kinds: readonly EntryKind[] = ['field', 'operator', 'function']
const kindTexts: Record<EntryKind, string> = {
  field: 'shell.studio.expression.fields',
  operator: 'shell.studio.expression.operators',
  function: 'shell.studio.expression.functions',
}

/**
 * The fields, operators and functions that the place of an expression allows, in three tabs, with one search over all
 * three. A click on an entry calls `onPick` with the text to insert, and whether the cursor goes inside it. The
 * functions are followed by the application's `rules`.
 */
export function ExpressionPanel({
  starts = [],
  sources,
  onStart,
  scope,
  rules,
  names,
  onPick,
}: {
  /** Proposed (#3265): the starting points of the place, in a first tab, each read in words. */
  starts?: ExpressionStart[]
  sources?: ReadingSources
  onStart?: (start: ExpressionStart) => void
  scope: ExpressionScope | null
  rules: ScopeOption[]
  names: StudioNames | null | undefined
  onPick: (text: string, cursorInside: boolean) => void
}) {
  const t = useText()
  const { spacing } = useDesignTokens()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const [search, setSearch] = useState('')

  const entries: PanelEntry[] = [
    ...(scope?.fields ?? []).map((field): PanelEntry => ({
      kind: 'field',
      name: field.name,
      insert: field.name,
      cursorInside: false,
      description: (names ? labelText(names, field.labelKey, locale) : undefined) ?? '',
    })),
    ...expressionOperators.map((item): PanelEntry => ({
      kind: 'operator',
      name: item.name,
      insert: item.insert,
      cursorInside: item.cursorInside,
      description: t(`shell.studio.expression.op.${item.id}`),
      example: item.example,
    })),
    ...(scope?.functions ?? []).map((item): PanelEntry => ({
      kind: 'function',
      name: `${item.name}()`,
      insert: `${item.name}()`,
      cursorInside: item.parameters.length > 0,
      description: `${signatureOf(item.name, item.parameters)} ${item.description}`,
      example: item.example,
    })),
    ...rules.map((rule): PanelEntry => ({
      kind: 'function',
      name: rule.label,
      insert: rule.label,
      cursorInside: true,
      description: '',
    })),
  ]
  const searching = search.trim() !== ''

  return (
    <Flex vertical gap={spacing.xs}>
      <Input
        allowClear
        value={search}
        onChange={(event) => setSearch(event.target.value)}
        aria-label={t('shell.studio.expression.search')}
        placeholder={t('shell.studio.expression.search')}
      />
      {searching ? (
        <EntryList
          label={t('shell.studio.expression.search')}
          entries={rankEntries(entries, search)}
          tagged
          onPick={onPick}
        />
      ) : (
        <Tabs
          items={[
            ...(starts.length > 0 && sources && onStart
              ? [
                  {
                    key: 'start',
                    label: t('shell.studio.expression.startTab'),
                    children: <StartList starts={starts} sources={sources} onStart={onStart} />,
                  },
                ]
              : []),
            ...kinds.map((kind) => ({
              key: kind,
              label: t(kindTexts[kind]),
              children: (
                <EntryList
                  label={t(kindTexts[kind])}
                  entries={entries.filter((entry) => entry.kind === kind)}
                  onPick={onPick}
                />
              ),
            })),
          ]}
        />
      )}
    </Flex>
  )
}

function EntryList({
  label,
  entries,
  tagged = false,
  onPick,
}: {
  label: string
  entries: PanelEntry[]
  /** Tags each entry with the word of its tab, for a list that mixes the three. */
  tagged?: boolean
  onPick: (text: string, cursorInside: boolean) => void
}) {
  const t = useText()
  const { colour, spacing, type, size } = useDesignTokens()
  if (entries.length === 0) {
    return <EmptyState text={t('shell.studio.expression.noMatch')} />
  }
  return (
    <ul
      aria-label={label}
      style={{ listStyle: 'none', margin: 0, padding: 0, maxHeight: size.tableRow * 7, overflowY: 'auto' }}
    >
      {entries.map((entry, index) => (
        <li
          key={`${entry.kind}:${entry.name}:${index}`}
          style={{ padding: `${spacing.xs}px 0`, borderBottom: `1px solid ${colour.borderSubtle}` }}
        >
          <Flex vertical>
            <Flex gap={spacing.xs} align="center">
              <AxisButton role="link" onClick={() => onPick(entry.insert, entry.cursorInside)}>
                <code style={{ fontFamily: type.codeFontFamily }}>{entry.name}</code>
              </AxisButton>
              {tagged && <Tag>{t(kindTexts[entry.kind])}</Tag>}
            </Flex>
            {entry.description !== '' && <Typography.Text type="secondary">{entry.description}</Typography.Text>}
            {entry.example !== undefined && (
              <Typography.Text type="secondary">
                <code style={{ fontFamily: type.codeFontFamily }}>{entry.example}</code>
              </Typography.Text>
            )}
          </Flex>
        </li>
      ))}
    </ul>
  )
}

/** Proposed (#3265): the starting points, each read in words, with its expression under it. */
function StartList({
  starts,
  sources,
  onStart,
}: {
  starts: ExpressionStart[]
  sources: ReadingSources
  onStart: (start: ExpressionStart) => void
}) {
  const t = useText()
  const { colour, spacing, type } = useDesignTokens()
  return (
    <ul aria-label={t('shell.studio.expression.startTab')} style={{ listStyle: 'none', margin: 0, padding: 0 }}>
      {starts.map((start) => {
        const words = readExpression(start.text, sources)
        return (
          <li
            key={start.text}
            style={{ padding: `${spacing.xs}px 0`, borderBottom: `1px solid ${colour.borderSubtle}` }}
          >
            <Flex vertical>
              <AxisButton
                role="link"
                style={{ height: 'auto', whiteSpace: 'normal', textAlign: 'start', alignSelf: 'flex-start' }}
                onClick={() => onStart(start)}
              >
                <span>{words ? readingInline(words, sources.locale) : start.text}</span>
              </AxisButton>
              <Typography.Text type="secondary">
                <code style={{ fontFamily: type.codeFontFamily }}>{start.text}</code>
              </Typography.Text>
            </Flex>
          </li>
        )
      })}
    </ul>
  )
}
