import { Flex, Select } from 'antd'
import type { ReactNode } from 'react'
import { escapePointerSegment } from '../platform/jsonPatch'
import type { StudioCatalogSlot } from '../platform/studio'
import { slotTitle, timeZoneText, type RangeOperator } from '../platform/studioCatalog'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { ReferencePicker } from './ReferencePicker'

/** An installed module or package that the control chooses from. */
interface Installed {
  id: string
  label: string
  versions: string[]
}

/**
 * The choice of an installed module or package, by label, see
 * docs/reference/studio-resource-form.md#controls-that-know-what-they-hold. Choosing one writes the whole item once, with
 * an empty version range filled with `^` and the newest version, so it is one step to undo. An id that is not installed
 * stays selected under its own text.
 */
export function CatalogChoice({
  name,
  id,
  'aria-describedby': describedBy,
  installed,
  value,
  item,
  itemPointer,
  onChange,
}: {
  name: string
  /** The id and description that `AxisField` gives its input. */
  id?: string
  'aria-describedby'?: string
  installed: Installed[]
  value: string | undefined
  item: Record<string, unknown>
  itemPointer: string
  onChange: (pointer: string, next: unknown) => void
}) {
  const current = value === undefined || value === '' ? undefined : value
  const options = [
    ...(current !== undefined && !installed.some((entry) => entry.id === current)
      ? [{ value: current, label: current }]
      : []),
    ...installed.map((entry) => ({ value: entry.id, label: entry.label })),
  ]
  return (
    <Select
      aria-label={name}
      id={id}
      aria-describedby={describedBy}
      showSearch
      optionFilterProp="label"
      virtual={false}
      style={{ minWidth: 240 }}
      value={current}
      options={options}
      onChange={(id: string) => {
        const newest = installed.find((entry) => entry.id === id)?.versions[0]
        const empty = typeof item.version !== 'string' || item.version === ''
        onChange(itemPointer, { ...item, id, ...(empty && newest !== undefined ? { version: `^${newest}` } : {}) })
      }}
    />
  )
}

/** A line in `warningText` under a control, for a value that the control keeps shown but that will not pass. */
export function WarningLine({ testId, children }: { testId: string; children: ReactNode }) {
  const { colour } = useDesignTokens()
  return (
    <div data-testid={testId} style={{ color: colour.warningText }}>
      {children}
    </div>
  )
}

/**
 * A version range as two selects, because a range has three forms. Version lists the installed versions, newest first, and
 * Updates says which later versions the range takes. A version that is no longer installed stays selected, with a line in
 * `warningText` that asks the author to choose an installed one.
 */
export function VersionRangeInput({
  range,
  versions,
  onChange,
}: {
  range: { operator: RangeOperator; version: string }
  versions: string[]
  onChange: (next: string) => void
}) {
  const t = useText()
  const { spacing } = useDesignTokens()
  const installed = versions.includes(range.version)
  const [major, minor] = range.version.split('.')
  const choices = [
    ...(installed ? [] : [{ value: range.version, label: range.version }]),
    ...versions.map((version, index) => ({
      value: version,
      label: index === 0 ? t('shell.studio.versionRange.newest').replace('{version}', version) : version,
    })),
  ]
  const updates: { value: RangeOperator; label: string }[] = [
    { value: '^', label: t('shell.studio.versionRange.compatible').replace('{major}', major) },
    { value: '~', label: t('shell.studio.versionRange.fixes').replace('{major}', major).replace('{minor}', minor) },
    { value: '', label: t('shell.studio.versionRange.exact') },
  ]
  return (
    <Flex vertical gap={spacing.xs}>
      <Flex gap="small" wrap>
        <Select
          aria-label={t('shell.studio.versionRange.version')}
          virtual={false}
          style={{ minWidth: 160 }}
          value={range.version}
          options={choices}
          onChange={(version: string) => onChange(`${range.operator}${version}`)}
        />
        <Select
          aria-label={t('shell.studio.versionRange.updates')}
          virtual={false}
          style={{ minWidth: 240 }}
          value={range.operator}
          options={updates}
          onChange={(operator: RangeOperator) => onChange(`${operator}${range.version}`)}
        />
      </Flex>
      {!installed && (
        <WarningLine testId="studio-version-not-installed">{t('shell.studio.versionRange.notInstalled')}</WarningLine>
      )}
    </Flex>
  )
}

/**
 * One select for each slot of the chosen module: the slot's label above a select of the application's names. A module
 * with no slots at all says so, on its roles field only, so the line shows once.
 */
export function ModuleSlots({
  slots,
  noSlots,
  names,
  value,
  pointer,
  locale,
  onChange,
}: {
  slots: StudioCatalogSlot[]
  /** Whether to say that the module has no roles or connectors. */
  noSlots: boolean
  /** The application's roles or connectors that the slots take. */
  names: string[]
  value: unknown
  pointer: string
  locale: string
  onChange: (pointer: string, next: unknown) => void
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const map =
    typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as Record<string, unknown>) : {}
  return (
    <Flex vertical gap="small">
      {noSlots && <span style={{ color: colour.textMuted }}>{t('shell.studio.module.noSlots')}</span>}
      {slots.map((slot) => {
        const title = slotTitle(slot, locale)
        const mapped = map[slot.name]
        return (
          <div key={slot.name} data-testid={`studio-slot-${slot.name}`}>
            <div style={{ color: colour.textMuted, marginBottom: spacing.xs }}>{title}</div>
            <ReferencePicker
              name={title}
              value={typeof mapped === 'string' ? mapped : undefined}
              required={false}
              options={names}
              onChange={(next) =>
                onChange(`${pointer}/${escapePointerSegment(slot.name)}`, next === '' ? undefined : next)
              }
            />
          </div>
        )
      })}
    </Flex>
  )
}

/** A searchable, clearable choice of the time zones the server knows, in the catalog's order, by offset then id. */
export function TimeZoneSelect({
  name,
  id,
  'aria-describedby': describedBy,
  zones,
  value,
  onChange,
}: {
  name: string
  /** The id and description that `AxisField` gives its input. */
  id?: string
  'aria-describedby'?: string
  zones: { id: string; offset: number }[]
  value: string | undefined
  onChange: (next: string | undefined) => void
}) {
  const current = value === undefined || value === '' ? undefined : value
  const options = [
    ...(current !== undefined && !zones.some((zone) => zone.id === current)
      ? [{ value: current, label: current }]
      : []),
    ...zones.map((zone) => ({ value: zone.id, label: timeZoneText(zone) })),
  ]
  return (
    <Select
      aria-label={name}
      id={id}
      aria-describedby={describedBy}
      showSearch
      allowClear
      optionFilterProp="label"
      virtual={false}
      style={{ minWidth: 320 }}
      value={current}
      options={options}
      onChange={(next: string | undefined) => onChange(next)}
    />
  )
}

/**
 * A choice of the tenant's binding names of one kind, see
 * docs/reference/studio-resource-form.md#controls-that-know-what-they-hold. A name that the tenant lacks, or has only as
 * another kind, stays selected, with a line that says activation will refuse it. With `noBinding`, the first option is
 * "The tenant's database, with no binding", which is selected while `value` is `undefined` and calls `onChange` with
 * `undefined`.
 */
export function BindingSelect({
  name,
  id,
  'aria-describedby': describedBy,
  names,
  value,
  noBinding = false,
  onChange,
}: {
  name: string
  /** The id and description that `AxisField` gives its input. */
  id?: string
  'aria-describedby'?: string
  names: string[]
  /** The saved name, `''` for an entry without a name, and `undefined` when the entry is absent. */
  value: string | undefined
  noBinding?: boolean
  onChange: (next: string | undefined) => void
}) {
  const t = useText()
  const { spacing } = useDesignTokens()
  const current = value === undefined || value === '' ? undefined : value
  const missing = current !== undefined && !names.includes(current)
  const options = [
    ...(noBinding ? [{ value: '', label: t('shell.studio.binding.noBinding') }] : []),
    ...(missing ? [{ value: current, label: current }] : []),
    ...names.map((entry) => ({ value: entry, label: entry })),
  ]
  const selected = current ?? (noBinding && value === undefined ? '' : undefined)
  return (
    <Flex vertical gap={spacing.xs}>
      <Select
        aria-label={name}
        id={id}
        aria-describedby={describedBy}
        showSearch
        optionFilterProp="label"
        virtual={false}
        style={{ minWidth: 240 }}
        value={selected}
        options={options}
        onChange={(next: string) => onChange(next === '' ? undefined : next)}
      />
      {missing && (
        <WarningLine testId="studio-tenant-binding-missing">{t('shell.studio.binding.notInTenant')}</WarningLine>
      )}
    </Flex>
  )
}
