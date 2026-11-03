import { DeleteOutlined, EditOutlined } from '@ant-design/icons'
import { Button, Checkbox, Flex, Input, InputNumber, Modal, Select, Tag, Typography } from 'antd'
import { createContext, lazy, Suspense, useContext, useEffect, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { Link } from 'react-router'
import { AxisButton } from '../platform/AxisButton'
import { AxisTable } from '../platform/AxisTable'
import { dialogWidth } from '../platform/dialogButtons'
import { EmptyDash } from '../platform/EmptyValue'
import { getAt, setAt } from '../platform/jsonPatch'
import {
  fetchStudioRelaxed,
  relaxedPointWords,
  sortRelaxed,
  type StudioNames,
  type StudioRelaxedPoint,
} from '../platform/studio'
import { parameterType, referenceOptions, StudioNamesContext } from '../platform/studioNames'
import { resolveSchema, type JsonSchema, type ReferenceMark } from '../platform/studioSchemas'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { ExpressionCheckContext } from './expressionCheckContext'
import { expressionScope } from './expressionScope'
import { LabelTexts } from './LabelTexts'
import { LabelTextsContext } from './labelTextsContext'
import { ReferencePicker } from './ReferencePicker'
import {
  fieldControls,
  fieldTextKey,
  fitColumns,
  RemovedNoticeContext,
  setLists,
  identityProperties,
  SchemaFormKindContext,
  schemaFormSections,
  StudioCatalogContext,
} from './schemaFormSections'
import { StudioResourceTitle } from './studioResourceTitle'
import { nameCollator } from './studioViews'

const LazyExpressionEditor = lazy(() => import('./ExpressionEditor'))

/** Called with the JSON Pointer of the value that changed. `undefined` removes the value. */
export type ChangeHandler = (pointer: string, next: unknown) => void

/** Properties that are read-only at the root: the file's identity. */
const readOnlyRootProperties = new Set(['id', 'kind'])

/** The keywords of a conditional. A combination of only these is shown as a form, because the form does not enforce them. */
const conditionalKeywords = new Set(['if', 'then', 'else'])

/**
 * A form built from the JSON Schema of a resource kind, for the draft `value`. It shows text, number,
 * boolean and enum values, nested objects and arrays. A string that names a resource gets a picker of the
 * application's names, as does each key of a map whose keys are marked. Anything else points to the JSON view.
 */
export function SchemaForm({
  schema,
  value,
  onChange,
  kind,
}: {
  schema: JsonSchema
  value: Record<string, unknown>
  onChange: ChangeHandler
  /**
   * Proposed: the resource kind of a whole resource's form. With it, the form follows Studio's form standard: the
   * file's identity is hidden, the root properties sit in the kind's sections, and every field is named by its text.
   */
  kind?: string
}) {
  if (kind !== undefined) {
    return (
      <SchemaFormKindContext.Provider value={kind}>
        <SectionedForm kind={kind} schema={schema} value={value} onChange={onChange} />
      </SchemaFormKindContext.Provider>
    )
  }
  return (
    <Properties
      node={schema}
      root={schema}
      rootValue={value}
      pointer=""
      value={value}
      onChange={onChange}
      readOnlyNames={readOnlyRootProperties}
    />
  )
}

/**
 * Proposed: a resource's form as sections, each a panel on surface with a heading and a line that says what it holds,
 * the panel the Overview's blocks use. A section that holds one object shows that object's fields itself, with no
 * frame around them.
 */
function SectionedForm({
  kind,
  schema,
  value,
  onChange,
}: {
  kind: string
  schema: JsonSchema
  value: Record<string, unknown>
  onChange: ChangeHandler
}) {
  const t = useText()
  const { spacing } = useDesignTokens()
  const required = new Set(schema.required)
  const all = Object.keys(schema.properties ?? {}).filter((name) => !identityProperties.has(name))
  const declared = schemaFormSections[kind] ?? []
  const placed = new Set(declared.flatMap((section) => section.properties))
  const rest = all.filter((name) => !placed.has(name))
  const sections = [
    ...declared.map((section) => ({
      key: section.key,
      properties: section.properties.filter((name) => all.includes(name)),
    })),
    ...(rest.length > 0 ? [{ key: '', properties: rest }] : []),
  ]
  // Proposed: a section that holds one field names it in its heading, so the field shows no label of its own.
  const field = (name: string, only: boolean) => (
    <SchemaNode
      bare={only}
      key={name}
      node={schema.properties![name]}
      root={schema}
      rootValue={value}
      pointer={`/${escapeSegment(name)}`}
      name={name}
      value={value[name]}
      required={required.has(name)}
      onChange={onChange}
    />
  )
  return (
    <Flex vertical gap={spacing.md} data-testid="studio-form-sections">
      {sections.map((section) => {
        const titleKey = `shell.studio.section.${kind}.${section.key}`
        const title = section.key && t(titleKey) !== titleKey ? t(titleKey) : null
        const description = section.key ? textOrNull(t, `${titleKey}.description`) : null
        const only = section.properties.length === 1 ? schema.properties![section.properties[0]] : undefined
        const resolved = only ? resolveSchema(only, schema) : undefined
        const flatten = resolved?.type === 'object' && resolved.properties !== undefined && !isLabelSchema(resolved)
        return (
          <SectionPanel key={section.key || 'more'} title={title} description={description} testId={section.key}>
            {flatten ? (
              <Properties
                node={resolved}
                root={schema}
                rootValue={value}
                pointer={`/${escapeSegment(section.properties[0])}`}
                value={value[section.properties[0]]}
                onChange={onChange}
              />
            ) : (
              section.properties.map((name) => field(name, section.properties.length === 1 && section.key !== ''))
            )}
          </SectionPanel>
        )
      })}
    </Flex>
  )
}

function SectionPanel({
  title,
  description,
  testId,
  children,
}: {
  title: string | null
  description: string | null
  testId: string
  children: ReactNode
}) {
  const { colour, radius, spacing } = useDesignTokens()
  const [slot, setSlot] = useState<HTMLDivElement | null>(null)
  return (
    <section
      data-testid={`studio-form-section-${testId || 'more'}`}
      style={{
        background: colour.surface,
        border: `1px solid ${colour.borderSubtle}`,
        borderRadius: radius.base,
        padding: spacing.lg,
      }}
    >
      <Flex vertical gap={spacing.md}>
        {(title || description) && (
          // Proposed: a section's main action, such as Add module…, sits on the right of its heading.
          <Flex align="center" gap={spacing.md}>
            <Flex vertical gap={4} style={{ flex: 1 }}>
              {title && (
                <Typography.Title level={5} style={{ margin: 0 }}>
                  {title}
                </Typography.Title>
              )}
              {description && <Typography.Text style={{ color: colour.textMuted }}>{description}</Typography.Text>}
            </Flex>
            <div ref={setSlot} />
          </Flex>
        )}
        <SectionActionSlot.Provider value={slot}>{children}</SectionActionSlot.Provider>
      </Flex>
    </section>
  )
}

function textOrNull(t: (key: string) => string, key: string): string | null {
  const text = t(key)
  return text === key ? null : text
}

/**
 * Proposed: a field's label and help text, as the form widget draws them: the label in medium weight and textMuted,
 * the help in small textMuted text between the label and the input.
 */
function FieldHeading({ label, help, helpId }: { label: string; help: string | null; helpId: string }) {
  const { colour, type } = useDesignTokens()
  return (
    <div style={{ marginBottom: 6 }}>
      <div style={{ fontWeight: 500, color: colour.textMuted, fontSize: type.fontSize, lineHeight: '22px' }}>
        {label}
      </div>
      {help && (
        <div id={helpId} style={{ fontSize: 12, lineHeight: '20px', color: colour.textMuted }}>
          {help}
        </div>
      )}
    </div>
  )
}

/** Proposed: a time zone as a person reads it: its offset from UTC, then its place, such as "(UTC+07:00) Asia/Ho Chi Minh". */
function timeZoneLabel(zone: { id: string; offset: number }): string {
  const sign = zone.offset < 0 ? '-' : '+'
  const minutes = Math.abs(zone.offset)
  const offset = `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`
  return `(UTC${sign}${offset}) ${zone.id.replace(/_/g, ' ')}`
}

/** Proposed: the right side of the heading of the section a node sits in. */
const SectionActionSlot = createContext<HTMLElement | null>(null)

/** Proposed: whether a node is drawn inside a list's dialog, where a nested list is rows of inputs, never a dialog. */
const InListDialogContext = createContext(false)

/** The text of `key`, or of `fallback` when no text holds `key`. */
function textOr(t: (key: string) => string, key: string, fallback: string): string {
  const text = t(key)
  return text === key ? t(fallback) : text
}

/** Proposed: what a list's cell shows for a value: its label for a known kind of value, a count for a list or a map. */
function CellValue({
  schema,
  value,
  path,
  item,
}: {
  schema: JsonSchema
  value: unknown
  path: string
  item?: unknown
}) {
  const t = useText()
  const { colour } = useDesignTokens()
  const catalog = useContext(StudioCatalogContext)
  const names = useContext(StudioNamesContext)
  const titleOf = useContext(StudioResourceTitle)
  const control = fieldControls[path]
  // Proposed: a cell shows what a list or map holds, in words, never a bare count. A module's role or connector map is
  // one line per slot, "Budget approver: Department manager", and a slot no role plays says Not mapped in warningText.
  if (control === 'moduleRoles' || control === 'moduleConnectors') {
    const slots = control === 'moduleRoles' ? 'roles' : 'connectors'
    const id = (item as Record<string, unknown> | undefined)?.id
    const module = catalog?.modules.find((entry) => entry.id === id)
    const map = typeof value === 'object' && value !== null ? (value as Record<string, string>) : {}
    if (!module || module[slots].length === 0) {
      return <EmptyDash />
    }
    const locale = document.documentElement.lang || 'en'
    const resourcesOf = slots === 'roles' ? names?.roles : names?.connectors
    return (
      <Flex vertical>
        {module[slots].map((slot) => {
          const label = slot.titles[locale] ?? slot.titles.en ?? slot.name
          const target = map[slot.name]
          const resource = resourcesOf?.find((entry) => entry.name === target)
          return (
            <span key={slot.name}>
              {label}:{' '}
              {target ? (
                (resource && titleOf(resource.id)) || target
              ) : (
                <span style={{ color: colour.warningText }}>{t('shell.studio.slots.notMapped')}</span>
              )}
            </span>
          )
        })}
      </Flex>
    )
  }
  // Proposed: a module's relaxed points show as the Off-standard tag, as the Releases list marks a release; the points
  // themselves are in its dialog.
  if (control === 'relaxedPoints') {
    return Array.isArray(value) && value.length > 0 ? (
      <Tag color="warning">{t('shell.studio.offStandard')}</Tag>
    ) : (
      <EmptyDash />
    )
  }
  if (control === 'module' && typeof value === 'string') {
    return <>{catalog?.modules.find((module) => module.id === value)?.label ?? value}</>
  }
  if (control === 'package' && typeof value === 'string') {
    return <>{catalog?.packages.find((pkg) => pkg.id === value)?.label ?? value}</>
  }
  const count = Array.isArray(value)
    ? value.length
    : typeof value === 'object' && value !== null
      ? Object.keys(value).length
      : undefined
  if (count !== undefined || schema.type === 'array' || schema.type === 'object') {
    return count ? <>{count}</> : <EmptyDash />
  }
  if (typeof value === 'boolean') {
    return <>{t(value ? 'shell.table.yes' : 'shell.table.no')}</>
  }
  return value === undefined || value === null || value === '' ? <EmptyDash /> : <>{String(value)}</>
}

/**
 * Proposed: a list of objects in a resource's form is a table, as every list of the platform is: one column per
 * property of an item, named by its field text, a value of a known kind by its label and a nested list by its count.
 * Add… on the right of its toolbar adds an item and opens it in a dialog; each row has Edit… and Remove as icons.
 * The dialog edits the item in place, medium for an item of plain values and large for one that holds a list or a
 * map; Done keeps the edit, Cancel puts the item back as it was or, for a new one, removes it.
 */
function ListTable({
  node,
  root,
  rootValue,
  pointer,
  value,
  onChange,
  kind,
  inHeading = false,
}: NodeProps & { kind: string; inHeading?: boolean }) {
  const t = useText()
  const slot = useContext(SectionActionSlot)
  const notifyRemoved = useContext(RemovedNoticeContext)
  const catalog = useContext(StudioCatalogContext)
  const items = Array.isArray(value) ? (value as unknown[]) : []
  const itemSchema = resolveSchema(node.items!, root)
  // Proposed: a column that would be empty in every row is left out, such as Connectors when no module has any.
  const properties = Object.entries(itemSchema.properties ?? {}).filter(([name]) => {
    const control = fieldControls[fieldTextKey(kind, `${pointer}/0/${name}`).replace('shell.studio.field.', '')]
    if (control === 'moduleConnectors') {
      return items.some((item) => {
        const id = (item as Record<string, unknown> | undefined)?.id
        return (catalog?.modules.find((module) => module.id === id)?.connectors.length ?? 0) > 0
      })
    }
    return true
  })
  const listKey = fieldTextKey(kind, pointer)
  // Proposed: the dialog edits its own copy of the item. Done writes it to the list, as one step to undo; Cancel leaves
  // the list as it was, so Add… adds no row until Done.
  const [open, setOpen] = useState<{ index: number; isNew: boolean; item: unknown } | null>(null)
  const nested = properties.some(([, child]) => {
    const resolved = resolveSchema(child, root)
    return resolved.type === 'array' || (resolved.type === 'object' && resolved.properties === undefined)
  })
  const itemTitle = textOr(t, `${listKey}.item`, 'shell.studio.list.item')

  function add() {
    setOpen({ index: items.length, isNew: true, item: defaultFor(node.items!, root) })
  }

  function done() {
    if (open) {
      onChange(open.isNew ? pointer : `${pointer}/${open.index}`, open.isNew ? [...items, open.item] : open.item)
    }
    setOpen(null)
  }

  // A change inside the dialog lands on its copy: the pointer names the item's place in the list, as it will be.
  const itemPointer = open ? `${pointer}/${open.index}` : ''
  const changeItem: ChangeHandler = (target, next) => {
    if (!open || !target.startsWith(itemPointer)) {
      onChange(target, next)
      return
    }
    const rest = target.slice(itemPointer.length)
    // Each change builds on the copy as the one before left it, so two changes in one event both land.
    setOpen((current) => current && { ...current, item: rest === '' ? next : setAt(current.item ?? {}, rest, next) })
  }
  // The rest of the form sees the copy in its place, so a field that reads a sibling, such as a module's roles reading
  // its module, finds it.
  const dialogRoot = open
    ? setAt(
        rootValue,
        pointer,
        open.isNew ? [...items, open.item] : items.map((item, index) => (index === open.index ? open.item : item)),
      )
    : rootValue
  const missing = open
    ? (itemSchema.required ?? []).some((name) => {
        const value = (open.item as Record<string, unknown> | undefined)?.[name]
        return value === undefined || value === null || value === ''
      })
    : false

  // Proposed: a list that is a set shows by name, a module or package by its label; each row keeps its place in the
  // file, which Edit… and Remove use.
  const collator = nameCollator()
  const rowName = (item: unknown) => {
    const id = (item as Record<string, unknown> | undefined)?.id
    const label =
      typeof id === 'string'
        ? (catalog?.modules.find((entry) => entry.id === id)?.label ??
          catalog?.packages.find((entry) => entry.id === id)?.label ??
          id)
        : ''
    return label
  }
  const tableRows = items.map((item, index) => ({ index, item }))
  if (setLists.has(listKey.replace('shell.studio.field.', ''))) {
    tableRows.sort((a, b) => collator.compare(rowName(a.item), rowName(b.item)))
  }
  const addButton = (
    <AxisButton role="main" onClick={add} data-testid={`studio-list-add-${pointer}`}>
      {textOr(t, `${listKey}.add`, 'shell.studio.list.add')}
    </AxisButton>
  )
  return (
    <>
      {inHeading && slot && createPortal(addButton, slot)}
      <AxisTable<{ index: number; item: unknown }>
        rows={tableRows}
        rowKey={(row) => row.index}
        // Proposed: an empty list says No data yet., as every empty table does.
        emptyText=""
        pagination={false}
        testId={`studio-list-${pointer}`}
        toolbar={inHeading && slot ? undefined : { main: addButton }}
        columns={properties.map(([name, child]) => ({
          key: name,
          title: textOrNull(t, fieldTextKey(kind, `${pointer}/0/${name}`)) ?? name,
          kind: fitColumns.has(fieldTextKey(kind, `${pointer}/0/${name}`).replace('shell.studio.field.', ''))
            ? ('fit' as const)
            : ('text' as const),
          render: (row: { index: number; item: unknown }) => (
            <CellValue
              schema={resolveSchema(child, root)}
              item={row.item}
              value={(row.item as Record<string, unknown> | undefined)?.[name]}
              path={fieldTextKey(kind, `${pointer}/0/${name}`).replace('shell.studio.field.', '')}
            />
          ),
        }))}
        rowActions={{
          count: 2,
          of: (row) => [
            {
              key: 'edit',
              labelKey: 'shell.studio.list.edit',
              nameSuffix: `${itemTitle} ${row.index + 1}`,
              icon: <EditOutlined />,
              onClick: () => setOpen({ index: row.index, isNew: false, item: items[row.index] }),
            },
            {
              key: 'remove',
              labelKey: 'shell.studio.list.remove',
              nameSuffix: `${itemTitle} ${row.index + 1}`,
              icon: <DeleteOutlined />,
              destructive: true,
              onClick: () => {
                onChange(`${pointer}/${row.index}`, undefined)
                notifyRemoved?.(rowName(row.item) || itemTitle)
              },
            },
          ],
        }}
      />
      <Modal
        open={open !== null}
        width={dialogWidth(nested ? 'large' : 'medium')}
        title={
          // Proposed: a dialog's title is the verb of the button that opened it: Add module for Add module…, which adds an
          // installed module to the application and creates none, and Edit module for Edit.
          open?.isNew
            ? textOr(t, `${listKey}.addTitle`, 'shell.studio.list.addTitle').replace('{item}', itemTitle)
            : textOr(t, `${listKey}.editTitle`, 'shell.studio.list.editTitle').replace('{item}', itemTitle)
        }
        okText={t('shell.studio.list.done')}
        cancelText={t('shell.studio.cancel')}
        // Proposed: Done waits for the item's required fields, such as its module.
        okButtonProps={{ disabled: missing }}
        onOk={done}
        onCancel={() => setOpen(null)}
        destroyOnHidden
      >
        {open && (
          <InListDialogContext.Provider value={true}>
            <Flex vertical gap="middle" data-testid="studio-list-dialog">
              {Object.entries(itemSchema.properties ?? {}).map(([name, child]) => (
                <SchemaNode
                  key={name}
                  node={child}
                  root={root}
                  rootValue={dialogRoot}
                  pointer={`${itemPointer}/${escapeSegment(name)}`}
                  name={name}
                  value={(open.item as Record<string, unknown> | undefined)?.[name]}
                  required={new Set(itemSchema.required).has(name)}
                  onChange={changeItem}
                />
              ))}
            </Flex>
          </InListDialogContext.Provider>
        )}
      </Modal>
    </>
  )
}

/**
 * Proposed: a list inside a list's dialog: rows of inputs under one header of field texts, with Remove on each row and
 * Add row under them, as a dialog never opens another.
 */
function InlineRows({ node, root, rootValue, pointer, value, onChange, kind }: NodeProps & { kind: string }) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const items = Array.isArray(value) ? (value as unknown[]) : []
  const itemSchema = resolveSchema(node.items!, root)
  const properties = Object.entries(itemSchema.properties ?? {})
  const columns = `${properties.map(() => 'minmax(0, 1fr)').join(' ')} 32px`
  return (
    <Flex vertical gap={spacing.xs}>
      {items.length > 0 && (
        <div style={{ display: 'grid', gridTemplateColumns: columns, columnGap: spacing.xs, rowGap: spacing.xs }}>
          {properties.map(([name]) => (
            <Typography.Text key={name} style={{ color: colour.textMuted, fontWeight: 500 }}>
              {textOrNull(t, fieldTextKey(kind, `${pointer}/0/${name}`)) ?? name}
            </Typography.Text>
          ))}
          <span />
          {items.map((item, index) => [
            ...properties.map(([name, child]) => (
              <SchemaNode
                key={`${index}/${name}`}
                node={child}
                root={root}
                rootValue={rootValue}
                pointer={`${pointer}/${index}/${escapeSegment(name)}`}
                name={name}
                value={(item as Record<string, unknown> | undefined)?.[name]}
                required={new Set(itemSchema.required).has(name)}
                bare
                onChange={onChange}
              />
            )),
            <AxisButton
              key={`${index}/remove`}
              role="icon"
              nameKey="shell.studio.list.remove"
              destructive
              icon={<DeleteOutlined />}
              onClick={() => onChange(`${pointer}/${index}`, undefined)}
            />,
          ])}
        </div>
      )}
      <div>
        <AxisButton role="secondary" onClick={() => onChange(pointer, [...items, defaultFor(node.items!, root)])}>
          {t('shell.studio.list.addRow')}
        </AxisButton>
      </div>
    </Flex>
  )
}

/**
 * Proposed: a module's relaxed points in Settings, read-only: each point in words, as the release page names it, linked
 * to the contract page, the one place a point is relaxed or its relax undone, and its reason. With none, the table
 * says the application follows every contract of the module.
 */
function RelaxedPointsView({ pointer, rootValue }: { pointer: string; rootValue: unknown }) {
  const t = useText()
  const { colour } = useDesignTokens()
  const resource = useContext(LabelTextsContext)
  const locale = document.documentElement.lang || 'en'
  const [points, setPoints] = useState<StudioRelaxedPoint[] | null>(null)
  const moduleId = getAt(rootValue, `${pointer.slice(0, pointer.lastIndexOf('/'))}/id`)
  const application = resource?.application
  useEffect(() => {
    if (!application) {
      return
    }
    const controller = new AbortController()
    fetchStudioRelaxed(application, controller.signal).then(setPoints, (error: unknown) => {
      if (!controller.signal.aborted) {
        console.warn('Loading the relaxed points failed', error)
        setPoints([])
      }
    })
    return () => controller.abort()
  }, [application])
  const rows = sortRelaxed(
    (points ?? []).filter((point) => point.module === moduleId),
    locale,
    t,
  )
  return (
    <Flex vertical gap="small">
      <Typography.Text style={{ color: colour.textMuted }}>
        {t('shell.studio.relaxed.changeOnContract')}
      </Typography.Text>
      <AxisTable<StudioRelaxedPoint>
        rows={points === null ? undefined : rows}
        loading={points === null}
        rowKey={(point) => point.point}
        emptyText={t('shell.studio.relaxed.noneForModule')}
        pagination={false}
        columns={[
          {
            key: 'point',
            title: t('shell.studio.relaxed.point'),
            kind: 'text',
            render: (point) => {
              const words = relaxedPointWords(point, locale, t)
              return (
                <Flex vertical>
                  {point.contractId && application ? (
                    <Link
                      to={`/studio?app=${encodeURIComponent(application)}&resource=${encodeURIComponent(point.contractId)}`}
                      style={{ color: colour.primaryText }}
                    >
                      {words.title}
                    </Link>
                  ) : (
                    <span>{words.title}</span>
                  )}
                  {words.kind && <span style={{ color: colour.textMuted }}>{words.kind}</span>}
                </Flex>
              )
            },
          },
          { key: 'reason', title: t('shell.studio.relaxed.reason'), kind: 'text', render: (point) => point.reason },
        ]}
      />
    </Flex>
  )
}

/**
 * Proposed: the map of a module's role or connector slots: one row per slot of the chosen module, its label, then the
 * application's role or connector that plays it, to choose. A module not yet chosen has no slots to map.
 */
function SlotMap({
  pointer,
  value,
  rootValue,
  slots,
  onChange,
}: {
  pointer: string
  value: unknown
  rootValue: unknown
  slots: 'roles' | 'connectors'
  onChange: ChangeHandler
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const catalog = useContext(StudioCatalogContext)
  const names = useContext(StudioNamesContext)
  const titleOf = useContext(StudioResourceTitle)
  const locale = document.documentElement.lang || 'en'
  const moduleId = getAt(rootValue, `${pointer.slice(0, pointer.lastIndexOf('/'))}/id`)
  const module = catalog?.modules.find((entry) => entry.id === moduleId)
  const map = typeof value === 'object' && value !== null ? (value as Record<string, string>) : {}
  const options = (slots === 'roles' ? names?.roles : names?.connectors)?.map((entry) => ({
    value: entry.name,
    label: titleOf?.(entry.id) ?? entry.name,
  }))
  if (!module || module[slots].length === 0) {
    return (
      <Typography.Text style={{ color: colour.textMuted }}>
        {module
          ? t(`shell.studio.slots.${slots}.empty`).replace('{module}', module.label)
          : t(`shell.studio.slots.${slots}.none`)}
      </Typography.Text>
    )
  }
  return (
    <div
      style={{
        display: 'grid',
        gridTemplateColumns: 'max-content minmax(0, 1fr)',
        columnGap: spacing.md,
        rowGap: spacing.xs,
        alignItems: 'center',
      }}
    >
      {module[slots].map((slot) => {
        const label = slot.titles[locale] ?? slot.titles.en ?? slot.name
        return [
          <Typography.Text key={`${slot.name}/label`}>{label}</Typography.Text>,
          <Select
            key={`${slot.name}/value`}
            aria-label={label}
            allowClear
            showSearch
            optionFilterProp="label"
            value={map[slot.name]}
            options={options}
            onChange={(next) => {
              const rest = Object.fromEntries(Object.entries(map).filter(([key]) => key !== slot.name))
              const updated = next === undefined ? rest : { ...rest, [slot.name]: next }
              onChange(pointer, Object.keys(updated).length > 0 ? updated : undefined)
            }}
          />,
        ]
      })}
    </div>
  )
}

interface NodeProps {
  node: JsonSchema
  root: JsonSchema
  rootValue: unknown
  pointer: string
  value: unknown
  onChange: ChangeHandler
}

function Properties({
  node,
  root,
  rootValue,
  pointer,
  value,
  onChange,
  readOnlyNames,
}: NodeProps & { readOnlyNames?: Set<string> }) {
  const required = new Set(node.required)
  return (
    <Flex vertical gap="small">
      {Object.entries(node.properties ?? {}).map(([name, child]) => (
        <SchemaNode
          key={name}
          node={child}
          root={root}
          rootValue={rootValue}
          pointer={`${pointer}/${escapeSegment(name)}`}
          name={name}
          value={(value as Record<string, unknown> | undefined)?.[name]}
          required={required.has(name)}
          readOnly={readOnlyNames?.has(name) ?? false}
          onChange={onChange}
        />
      ))}
    </Flex>
  )
}

function SchemaNode({
  node,
  root,
  rootValue,
  pointer,
  name,
  value,
  required,
  readOnly = false,
  item = false,
  onChange,
  bare = false,
}: NodeProps & { name: string; required: boolean; readOnly?: boolean; item?: boolean; bare?: boolean }) {
  const t = useText()
  const { colour, radius, spacing } = useDesignTokens()
  const kind = useContext(SchemaFormKindContext)
  // Proposed: under a kind, a field is named by its text and has its help; without one, by its name as written.
  const textKey = kind !== null && !item ? fieldTextKey(kind, pointer) : null
  const title = textKey ? (textOrNull(t, textKey) ?? name) : name
  const help = textKey ? textOrNull(t, `${textKey}.help`) : null
  const helpId = `studio-help-${pointer}`
  const names = useContext(StudioNamesContext)
  const check = useContext(ExpressionCheckContext)
  const label = useContext(LabelTextsContext)
  const schema = textAlternative(resolveSchema(node, root), root, value)
  const reference = node['x-axis-reference'] ?? schema['x-axis-reference']
  const isObject = schema.type === 'object' && schema.properties !== undefined
  const isArray = schema.type === 'array' && schema.items !== undefined
  // A label is an object whose only property is `textKey`. It needs the names and the resource to show its texts.
  const isLabel = isObject && label !== null && names !== undefined && isLabelSchema(schema)
  const keyMark = schema.propertyNames?.['x-axis-reference']
  const isMarkedMap =
    schema.type === 'object' && schema.properties === undefined && keyMark !== undefined && names !== undefined
  const unsupported = !isMarkedMap && !isSupported(schema)

  const inDialog = useContext(InListDialogContext)
  const catalog = useContext(StudioCatalogContext)
  const control =
    kind !== null ? fieldControls[fieldTextKey(kind, pointer).replace('shell.studio.field.', '')] : undefined

  let input
  if (control === 'module' || control === 'package') {
    // Proposed: a module or package is chosen from those installed, by its label.
    const entries = control === 'module' ? catalog?.modules : catalog?.packages
    input = (
      <Select
        aria-label={title}
        showSearch
        optionFilterProp="label"
        style={{ width: '100%' }}
        value={typeof value === 'string' ? value : undefined}
        options={entries?.map((entry) => ({ value: entry.id, label: entry.label ?? entry.id }))}
        onChange={(next) => {
          onChange(pointer, next)
          // Proposed: choosing a module or package fills an empty version range with its newest version, "^1.2.0", so
          // the item is complete and the author changes it only to pin another.
          const versionPointer = `${pointer.slice(0, pointer.lastIndexOf('/'))}/version`
          const newest = entries?.find((entry) => entry.id === next)?.versions[0]
          if (newest && !getAt(rootValue, versionPointer)) {
            onChange(versionPointer, `^${newest}`)
          }
        }}
      />
    )
  } else if (control === 'timeZone') {
    // Proposed: a time zone is chosen from those the server knows, searched by any part of its name, each shown with
    // its offset from UTC now and listed by offset, as calendars list them. Cleared, the application's dates are UTC.
    input = (
      <Select
        aria-label={title}
        showSearch
        allowClear={!required}
        placeholder={t('shell.studio.timeZone.none')}
        optionFilterProp="label"
        style={{ width: '100%' }}
        value={typeof value === 'string' ? value : undefined}
        options={catalog?.timeZones?.map((zone) => ({ value: zone.id, label: timeZoneLabel(zone) }))}
        onChange={(next) => onChange(pointer, next ?? undefined)}
      />
    )
  } else if (control === 'relaxedPoints') {
    input = <RelaxedPointsView pointer={pointer} rootValue={rootValue} />
  } else if (control === 'moduleRoles' || control === 'moduleConnectors') {
    input = (
      <SlotMap
        pointer={pointer}
        value={value}
        rootValue={rootValue}
        slots={control === 'moduleRoles' ? 'roles' : 'connectors'}
        onChange={onChange}
      />
    )
  } else if (
    kind !== null &&
    !item &&
    schema.type === 'array' &&
    schema.items !== undefined &&
    resolveSchema(schema.items, root).type === 'object'
  ) {
    input = inDialog ? (
      <InlineRows
        node={schema}
        root={root}
        rootValue={rootValue}
        pointer={pointer}
        value={value}
        onChange={onChange}
        kind={kind}
      />
    ) : (
      <ListTable
        node={schema}
        root={root}
        rootValue={rootValue}
        pointer={pointer}
        value={value}
        onChange={onChange}
        kind={kind}
        inHeading={bare}
      />
    )
  } else if (unsupported) {
    input = (
      <Typography.Text type="secondary" data-testid="studio-json-only">
        {t('shell.studio.editInJson')}
      </Typography.Text>
    )
  } else if (schema.const !== undefined || readOnly) {
    input = <Typography.Text>{String(schema.const ?? value ?? '')}</Typography.Text>
  } else if (schema.enum !== undefined) {
    input = (
      <Select
        aria-label={title}
        showSearch
        optionFilterProp="label"
        virtual={false}
        style={{ minWidth: 240 }}
        allowClear={!required}
        value={value as string | number | undefined}
        options={schema.enum.map((option) => ({ value: option as string | number, label: String(option) }))}
        onChange={(next) => onChange(pointer, next)}
      />
    )
  } else if (reference !== undefined && schema.type === 'string' && names !== undefined) {
    input = (
      <ReferencePicker
        name={name}
        value={typeof value === 'string' ? value : undefined}
        required={required}
        options={referenceOptions(names, reference, rootValue, pointer)}
        onChange={(next) => onChange(pointer, next)}
      />
    )
  } else if (schema.type === 'string' && schema['x-axis-expression'] === true && check) {
    const text = typeof value === 'string' ? value : ''
    input = (
      <Suspense fallback={<Input aria-label={title} value={text} readOnly />}>
        <LazyExpressionEditor
          application={check.application}
          resourceId={check.resourceId}
          path={pointer}
          name={name}
          value={text}
          required={required}
          options={expressionScope(check.kind, check.draft, pointer, check.names)}
          onChange={onChange}
        />
      </Suspense>
    )
  } else if (schema.type === 'string') {
    input = (
      <Input
        aria-label={title}
        value={typeof value === 'string' ? value : ''}
        onChange={(event) => onChange(pointer, event.target.value === '' && !required ? undefined : event.target.value)}
      />
    )
  } else if (schema.type === 'integer' || schema.type === 'number') {
    input = (
      <InputNumber
        aria-label={title}
        precision={schema.type === 'integer' ? 0 : undefined}
        min={schema.minimum}
        max={schema.maximum}
        value={typeof value === 'number' ? value : null}
        onChange={(next) => {
          // An empty array item stays, because clearing it would remove the item.
          if (typeof next === 'number' || !item) {
            onChange(pointer, typeof next === 'number' ? next : undefined)
          }
        }}
      />
    )
  } else if (schema.type === 'boolean') {
    input = (
      <Checkbox
        aria-label={title}
        checked={value === true}
        onChange={(event) => onChange(pointer, event.target.checked)}
      />
    )
  } else if (isMarkedMap) {
    input = (
      <MarkedMap
        schema={schema}
        keyMark={keyMark}
        names={names}
        root={root}
        rootValue={rootValue}
        pointer={pointer}
        name={name}
        value={value}
        onChange={onChange}
      />
    )
  } else if (isLabel) {
    input =
      kind !== null ? (
        <LabelTexts pointer={pointer} value={value} onChange={onChange} />
      ) : (
        <fieldset style={{ margin: 0 }}>
          <legend>{name}</legend>
          <LabelTexts pointer={pointer} value={value} onChange={onChange} />
        </fieldset>
      )
  } else if (isObject) {
    input = (
      <fieldset style={{ margin: 0 }}>
        <legend>{title}</legend>
        <Properties
          node={schema}
          root={root}
          rootValue={rootValue}
          pointer={pointer}
          value={value}
          onChange={onChange}
        />
      </fieldset>
    )
  } else if (isArray) {
    const items = Array.isArray(value) ? value : []
    const itemSchema = schema.items!
    input = (
      <Flex vertical gap="small">
        {items.map((item, index) => (
          <Flex
            key={index}
            gap="small"
            align="flex-start"
            style={{ border: `1px solid ${colour.border}`, borderRadius: radius.base, padding: spacing.xs }}
          >
            <div style={{ flex: 1 }}>
              <SchemaNode
                node={itemSchema}
                root={root}
                rootValue={rootValue}
                pointer={`${pointer}/${index}`}
                name={`${name} ${index + 1}`}
                value={item}
                required
                item
                onChange={onChange}
              />
            </div>
            <Button onClick={() => onChange(`${pointer}/${index}`, undefined)}>{t('shell.studio.removeItem')}</Button>
          </Flex>
        ))}
        <div>
          <Button onClick={() => onChange(pointer, [...items, defaultFor(itemSchema, root)])}>
            {t('shell.studio.addItem')}
          </Button>
        </div>
      </Flex>
    )
  }

  const showsLabel = !bare && (!isObject || (isLabel && kind !== null))
  return (
    <div data-testid={`studio-field-${pointer}`}>
      {showsLabel &&
        (kind !== null && !item ? <FieldHeading label={title} help={help} helpId={helpId} /> : <div>{name}</div>)}
      {input}
    </div>
  )
}

/**
 * A map whose keys name a part, such as the parameters of a data source. Each row has a picker for the key, a box
 * for the value and Remove. Add appends the first name that no row uses.
 */
function MarkedMap({
  schema,
  keyMark,
  names,
  root,
  rootValue,
  pointer,
  name,
  value,
  onChange,
}: {
  schema: JsonSchema
  keyMark: ReferenceMark
  names: StudioNames
  root: JsonSchema
  rootValue: unknown
  pointer: string
  name: string
  value: unknown
  onChange: ChangeHandler
}) {
  const t = useText()
  const { colour, radius, spacing } = useDesignTokens()
  const map = isRecord(value) ? value : {}
  const entries = Object.entries(map)
  const options = (key: string) => referenceOptions(names, keyMark, rootValue, `${pointer}/${escapeSegment(key)}`)
  // A parameter's value follows its type unless the schema gives the values a schema.
  const valueSchema = (key: string): JsonSchema => {
    if (typeof schema.additionalProperties === 'object') {
      return schema.additionalProperties
    }
    const type = parameterType(names, keyMark, rootValue, `${pointer}/${escapeSegment(key)}`, key)
    return {
      type: type === 'integer' ? 'integer' : type === 'decimal' ? 'number' : type === 'boolean' ? 'boolean' : 'string',
    }
  }
  const unused = (key: string) => {
    const used = new Set(entries.map(([other]) => other.toLowerCase()))
    return options(key).filter((option) => !used.has(option.toLowerCase()))
  }
  const next = unused('')[0]

  const rename = (from: string, to: string | undefined) => {
    if (to === undefined || to === '' || to === from) {
      return
    }
    const fresh = defaultFor(valueSchema(to), root)
    onChange(
      pointer,
      Object.fromEntries(
        entries.map(([key, entry]) =>
          key === from ? [to, typeof entry === typeof fresh ? entry : fresh] : [key, entry],
        ),
      ),
    )
  }

  return (
    <Flex vertical gap="small">
      {entries.map(([key, entry], index) => (
        <Flex
          key={key}
          gap="small"
          align="flex-start"
          style={{ border: `1px solid ${colour.border}`, borderRadius: radius.base, padding: spacing.xs }}
        >
          <ReferencePicker
            name={`${name} ${index + 1}`}
            value={key}
            required
            options={options(key).filter(
              (option) => option === key || !entries.some(([other]) => other.toLowerCase() === option.toLowerCase()),
            )}
            onChange={(to) => rename(key, to)}
          />
          <div style={{ flex: 1 }}>
            <SchemaNode
              node={valueSchema(key)}
              root={root}
              rootValue={rootValue}
              pointer={`${pointer}/${escapeSegment(key)}`}
              name={key}
              value={entry}
              required
              item
              onChange={onChange}
            />
          </div>
          <Button onClick={() => onChange(`${pointer}/${escapeSegment(key)}`, undefined)}>
            {t('shell.studio.removeItem')}
          </Button>
        </Flex>
      ))}
      <div>
        <Button
          disabled={next === undefined}
          onClick={() =>
            next !== undefined && onChange(pointer, { ...map, [next]: defaultFor(valueSchema(next), root) })
          }
        >
          {t('shell.studio.addItem')}
        </Button>
      </div>
    </Flex>
  )
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function escapeSegment(segment: string): string {
  return segment.replaceAll('~', '~0').replaceAll('/', '~1')
}

/** Whether the schema is a label: an object whose only property is `textKey`. */
function isLabelSchema(schema: JsonSchema): boolean {
  const keys = Object.keys(schema.properties ?? {})
  return keys.length === 1 && keys[0] === 'textKey'
}

/**
 * The schema the form shows for a node. A node with alternatives whose value is text, or empty, is shown through
 * its first text alternative. An object value keeps the alternatives, so it goes to the JSON view.
 */
function textAlternative(schema: JsonSchema, root: JsonSchema, value: unknown): JsonSchema {
  if (schema.oneOf === undefined || (value !== undefined && typeof value !== 'string')) {
    return schema
  }
  return schema.oneOf.map((option) => resolveSchema(option, root)).find((option) => option.type === 'string') ?? schema
}

/**
 * Whether the form can show a node. Alternatives, maps and nodes without a type go to the JSON view. A combination
 * of only conditionals is shown, because the form does not enforce its conditions.
 */
function isSupported(schema: JsonSchema): boolean {
  if (schema.oneOf !== undefined || schema.anyOf !== undefined) {
    return false
  }
  if (
    schema.allOf !== undefined &&
    !schema.allOf.every((part) => Object.keys(part).every((key) => conditionalKeywords.has(key)))
  ) {
    return false
  }
  if (schema.const !== undefined || schema.enum !== undefined) {
    return true
  }
  switch (schema.type) {
    case 'object':
      return schema.properties !== undefined
    case 'array':
      return schema.items !== undefined
    case undefined:
      return false
    default:
      return true
  }
}

/** The value Add appends to an array: an empty value of the item's type, or its first enum value. */
function defaultFor(node: JsonSchema, root: JsonSchema): unknown {
  const resolved = resolveSchema(node, root)
  const schema = resolved.oneOf?.[0] === undefined ? resolved : resolveSchema(resolved.oneOf[0], root)
  if (schema.enum !== undefined) {
    return schema.enum[0]
  }
  switch (schema.type) {
    case 'object':
      return {}
    case 'array':
      return []
    case 'integer':
    case 'number':
      return 0
    case 'boolean':
      return false
    default:
      return ''
  }
}
