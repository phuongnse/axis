import { Button, Card, Flex, Input, Select, Typography } from 'antd'
import { useContext, useState } from 'react'
import { getAt } from '../platform/jsonPatch'
import {
  addableTypes,
  childLists,
  containerTypes,
  type ListPart,
  moveTo,
  newWidget,
  type PathStep,
  typeChanges,
  widgetLists,
  widgetSummary,
  widgetTypes,
} from '../platform/pageWidgets'
import { referenceOptions, StudioNamesContext } from '../platform/studioNames'
import { useText } from '../platform/texts'
import { LabelTexts } from './LabelTexts'
import { LabelTextsContext } from './labelTextsContext'
import { MappingRows } from './MappingRows'
import { ReferencePicker } from './ReferencePicker'
import type { ChangeHandler } from './SchemaForm'

type Content = Record<string, unknown>

function isContent(value: unknown): value is Content {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function text(value: unknown): string {
  return typeof value === 'string' ? value : ''
}

/** A placeholder for what the editor cannot show, which points to the JSON view. */
function JsonOnly() {
  const t = useText()
  return <span data-testid="studio-json-only">{t('shell.studio.editInJson')}</span>
}

/**
 * The editor of a page. It shows the page's name and title, its widgets as a tree, and a panel for the selected
 * widget. The tree nests the widgets inside `columns`, `tabs` and `section`. Every list has an Add widget picker,
 * and every widget has Move up, Move down, Remove and a Move to picker that lists the other widget lists. A new
 * container starts with two columns of width 6, one tab, or an empty section. A container's type is fixed, and its
 * widths, labels and title are edited in the JSON view. The panel of a `table`, `form`, `taskInbox` or
 * `processInspector` widget shows its type with every property that type takes. A `form` panel also lists its
 * `actions`, which are `startProcess` or `navigate`, and a `table` panel lists its `rowActions` and `toolbarActions`,
 * which are `navigate`. A navigate action has a target page, a label, a `record` expression and parameter
 * expressions. A property is a name picked from the application's names, and a table's data source and
 * entity, like a form's entity and form, exclude each other, so a pick clears the other. The editor edits the
 * draft through `onChange`, so a key it does not show, on the page or on a widget, stays as it is. A widget of a
 * type it has no panel for shows its type and points to the JSON view.
 */
export function PageEditor({ value, onChange }: { value: Content; onChange: ChangeHandler }) {
  const t = useText()
  const names = useContext(StudioNamesContext)
  const label = useContext(LabelTextsContext)
  const [picked, setPicked] = useState('/widgets/0')
  const widgets = Array.isArray(value.widgets) ? value.widgets : undefined
  const selected = getAt(value, picked) === undefined ? '/widgets/0' : picked

  return (
    <Flex vertical gap="small" data-testid="studio-page-editor">
      <div data-testid="studio-field-/name">
        <div>{t('shell.studio.page.name')}</div>
        <Input
          aria-label={t('shell.studio.page.name')}
          value={text(value.name)}
          onChange={(event) => onChange('/name', event.target.value)}
        />
      </div>
      <div data-testid="studio-field-/title">
        <div>{t('shell.studio.page.title')}</div>
        {names !== undefined && label !== null ? (
          <LabelTexts pointer="/title" value={value.title} onChange={onChange} />
        ) : (
          <div data-testid="studio-field-/title/textKey">
            <Input
              aria-label={t('shell.studio.labelKey')}
              value={text(isContent(value.title) ? value.title.textKey : undefined)}
              onChange={(event) => onChange('/title/textKey', event.target.value)}
            />
          </div>
        )}
      </div>
      <div data-testid="studio-field-/widgets">
        <Flex vertical gap="small">
          <Typography.Text strong>{t('shell.studio.page.widgets')}</Typography.Text>
          {widgets === undefined ? (
            <JsonOnly />
          ) : (
            <>
              <WidgetList
                pointer="/widgets"
                widgets={widgets}
                tree={{ widgets, selected, onSelect: setPicked, onChange }}
              />
              {widgets.length > 0 && (
                <WidgetPanel base={selected} widget={getAt(value, selected)} onChange={onChange} />
              )}
            </>
          )}
        </Flex>
      </div>
    </Flex>
  )
}

/** What every list and row of the widget tree needs: the page's widgets, the selected pointer and the edits. */
interface Tree {
  widgets: unknown[]
  selected: string
  onSelect: (pointer: string) => void
  onChange: ChangeHandler
}

/** The widgets of one list as rows, then the picker that adds a widget at the end of the list. */
function WidgetList({ pointer, widgets, tree }: { pointer: string; widgets: unknown[]; tree: Tree }) {
  const t = useText()

  return (
    <Flex vertical gap="small">
      <Flex vertical gap="small" role="list" data-testid={`studio-widget-list-${pointer}`}>
        {widgets.map((widget, index) => (
          <WidgetRow key={index} listPointer={pointer} index={index} widgets={widgets} widget={widget} tree={tree} />
        ))}
      </Flex>
      <div>
        <Select
          aria-label={t('shell.studio.page.addWidget')}
          placeholder={t('shell.studio.page.addWidget')}
          showSearch
          optionFilterProp="label"
          virtual={false}
          style={{ minWidth: 240 }}
          value={null}
          options={addableTypes.map((option) => ({ value: option, label: option }))}
          onChange={(type: string) => tree.onChange(pointer, [...widgets, newWidget(type)])}
        />
      </div>
    </Flex>
  )
}

/** The words that name a part of a container, such as `Column 2` or `Tab 1`. A section has none. */
function usePartLabel() {
  const t = useText()
  return (part: ListPart): string | undefined => {
    if (part.kind === 'section') {
      return undefined
    }
    return `${t(part.kind === 'column' ? 'shell.studio.page.column' : 'shell.studio.page.tab')} ${part.index + 1}`
  }
}

/** One widget: the button that selects it, its move and remove buttons, and the lists inside a container. */
function WidgetRow({
  listPointer,
  index,
  widgets,
  widget,
  tree,
}: {
  listPointer: string
  index: number
  widgets: unknown[]
  widget: unknown
  tree: Tree
}) {
  const t = useText()
  const partLabel = usePartLabel()
  const pointer = `${listPointer}/${index}`
  const content = isContent(widget) ? widget : undefined
  const container =
    content !== undefined && typeof content.type === 'string' && containerTypes.includes(content.type)
      ? content
      : undefined
  const children = container === undefined ? [] : childLists(container, pointer)
  const pageList = t('shell.studio.page.pageList')
  const targets = widgetLists(tree.widgets)
    .filter((list) => list.pointer !== listPointer && !list.pointer.startsWith(`${pointer}/`))
    .map((list) => ({
      value: list.pointer,
      label: [pageList, ...list.path.flatMap((step) => stepLabels(step, partLabel))].join(' › '),
    }))

  function move(offset: number) {
    const next = [...widgets]
    ;[next[index], next[index + offset]] = [next[index + offset], next[index]]
    tree.onChange(listPointer, next)
  }

  function caption(list: { part: ListPart }): string | undefined {
    const name = partLabel(list.part)
    if (name !== undefined && list.part.kind === 'column' && Array.isArray(container?.columns)) {
      const column = container.columns[list.part.index]
      return isContent(column) && typeof column.width === 'number' ? `${name} · ${column.width}` : name
    }
    return name
  }

  return (
    <div role="listitem">
      <Flex vertical gap="small">
        <Flex gap="small" wrap align="center" data-testid={`studio-widget-row-${pointer}`}>
          <Button
            type={pointer === tree.selected ? 'primary' : 'default'}
            aria-current={pointer === tree.selected}
            data-testid={`studio-widget-${pointer}`}
            style={{ textAlign: 'left' }}
            onClick={() => tree.onSelect(pointer)}
          >
            {content !== undefined ? widgetSummary(content) : ''}
          </Button>
          <Button disabled={index === 0} onClick={() => move(-1)}>
            {t('shell.studio.page.moveUp')}
          </Button>
          <Button disabled={index === widgets.length - 1} onClick={() => move(1)}>
            {t('shell.studio.page.moveDown')}
          </Button>
          <Button
            danger
            onClick={() =>
              tree.onChange(
                listPointer,
                widgets.filter((_, at) => at !== index),
              )
            }
          >
            {t('shell.studio.page.removeWidget')}
          </Button>
          <Select
            aria-label={t('shell.studio.page.moveTo')}
            placeholder={t('shell.studio.page.moveTo')}
            showSearch
            optionFilterProp="label"
            virtual={false}
            style={{ minWidth: 200 }}
            value={null}
            options={targets}
            onChange={(target: string) => {
              const moved = moveTo(tree.widgets, pointer, target)
              if (moved !== undefined) {
                tree.onChange('/widgets', moved.widgets)
                tree.onSelect(moved.pointer)
              }
            }}
          />
        </Flex>
        {container !== undefined && (
          <Flex vertical gap="small" style={{ marginInlineStart: 24 }}>
            {children.map((list) => (
              <Flex vertical gap="small" key={list.pointer}>
                {caption(list) !== undefined && <Typography.Text type="secondary">{caption(list)}</Typography.Text>}
                <WidgetList
                  pointer={list.pointer}
                  widgets={getAt({ widgets: tree.widgets }, list.pointer) as unknown[]}
                  tree={tree}
                />
              </Flex>
            ))}
            {children.length !== partCount(container) && <JsonOnly />}
          </Flex>
        )}
      </Flex>
    </div>
  )
}

/** How many widget lists a container should hold, or one when it has no list of parts the editor can count. */
function partCount(widget: Content): number {
  if (widget.type === 'section') {
    return 1
  }
  const parts = widget.type === 'columns' ? widget.columns : widget.tabs
  return Array.isArray(parts) ? parts.length : -1
}

/** The names a step adds to a list's path, such as `columns 1` and `Column 2`. */
function stepLabels(step: PathStep, partLabel: (part: ListPart) => string | undefined): string[] {
  const part = partLabel(step.part)
  return part === undefined ? [`${step.type} ${step.position}`] : [`${step.type} ${step.position}`, part]
}

/** The picker of a name, or a plain box while the application's names have not loaded. */
function NameField({
  pointer,
  label,
  kind,
  current,
  required,
  without,
  onPick,
}: {
  pointer: string
  label: string
  kind: string
  current: unknown
  required: boolean
  /** A name the picker leaves out, ignoring letter case. */
  without?: string
  onPick: (next: string | undefined) => void
}) {
  const names = useContext(StudioNamesContext)
  const value = typeof current === 'string' ? current : undefined
  const options =
    names === undefined
      ? []
      : referenceOptions(names, kind, undefined, pointer).filter(
          (option) => without === undefined || without === '' || option.toLowerCase() !== without.toLowerCase(),
        )

  return (
    <div data-testid={`studio-field-${pointer}`}>
      <div>{label}</div>
      {names === undefined ? (
        <Input
          aria-label={label}
          value={text(value)}
          onChange={(event) => onPick(event.target.value === '' && !required ? undefined : event.target.value)}
        />
      ) : (
        <ReferencePicker name={label} value={value} required={required} options={options} onChange={onPick} />
      )}
    </div>
  )
}

function WidgetPanel({
  base,
  widget,
  onChange,
}: {
  /** The pointer of the selected widget. */
  base: string
  widget: unknown
  onChange: ChangeHandler
}) {
  const t = useText()
  const type = isContent(widget) && typeof widget.type === 'string' ? widget.type : undefined

  if (type !== undefined && containerTypes.includes(type)) {
    return (
      <Flex vertical gap="small" data-testid={`studio-field-${base}`}>
        <div data-testid={`studio-field-${base}/type`}>
          <div>{t('shell.studio.page.widgetType')}</div>
          <Typography.Text>{type}</Typography.Text>
        </div>
        <JsonOnly />
      </Flex>
    )
  }

  if (!isContent(widget) || type === undefined || !widgetTypes.includes(type)) {
    return (
      <div data-testid={`studio-field-${base}`}>
        <JsonOnly />
      </div>
    )
  }

  /** Writes a pick and clears the property it excludes, because the compiler accepts exactly one of the two. */
  function pick(key: string, next: string | undefined, other?: string) {
    onChange(`${base}/${key}`, next)
    if (next !== undefined && other !== undefined && (widget as Content)[other] !== undefined) {
      onChange(`${base}/${other}`, undefined)
    }
  }

  return (
    <Flex vertical gap="small" data-testid={`studio-field-${base}`}>
      <div data-testid={`studio-field-${base}/type`}>
        <div>{t('shell.studio.page.widgetType')}</div>
        <Select
          aria-label={t('shell.studio.page.widgetType')}
          virtual={false}
          style={{ minWidth: 240 }}
          value={type}
          options={widgetTypes.map((option) => ({ value: option, label: option }))}
          onChange={(next: string) => {
            for (const [key, change] of typeChanges(widget, next)) {
              onChange(`${base}${key}`, change)
            }
          }}
        />
      </div>
      {type === 'table' && (
        <>
          <NameField
            pointer={`${base}/entity`}
            label={t('shell.studio.page.entity')}
            kind="entity"
            current={widget.entity}
            required={false}
            onPick={(next) => pick('entity', next, 'dataSource')}
          />
          <NameField
            pointer={`${base}/dataSource`}
            label={t('shell.studio.page.dataSource')}
            kind="dataSource"
            current={widget.dataSource}
            required={false}
            onPick={(next) => pick('dataSource', next, 'entity')}
          />
          <Actions
            pointer={`${base}/rowActions`}
            title={t('shell.studio.page.rowActions')}
            actions={widget.rowActions}
            startProcess={false}
            onChange={onChange}
          />
          <Actions
            pointer={`${base}/toolbarActions`}
            title={t('shell.studio.page.toolbarActions')}
            actions={widget.toolbarActions}
            startProcess={false}
            onChange={onChange}
          />
        </>
      )}
      {type === 'form' && (
        <>
          <NameField
            pointer={`${base}/entity`}
            label={t('shell.studio.page.entity')}
            kind="entity"
            current={widget.entity}
            required={false}
            onPick={(next) => pick('entity', next, 'form')}
          />
          <NameField
            pointer={`${base}/form`}
            label={t('shell.studio.page.form')}
            kind="form"
            current={widget.form}
            required={false}
            onPick={(next) => pick('form', next, 'entity')}
          />
          <Actions
            pointer={`${base}/actions`}
            title={t('shell.studio.page.actions')}
            actions={widget.actions}
            startProcess
            onChange={onChange}
          />
        </>
      )}
      {type === 'processInspector' && (
        <NameField
          pointer={`${base}/process`}
          label={t('shell.studio.page.process')}
          kind="process"
          current={widget.process}
          required
          onPick={(next) => pick('process', next)}
        />
      )}
    </Flex>
  )
}

/** The label of an action, with a box for each locale, or a plain key box while the names have not loaded. */
function ActionLabel({ pointer, value, onChange }: { pointer: string; value: unknown; onChange: ChangeHandler }) {
  const t = useText()
  const names = useContext(StudioNamesContext)
  const label = useContext(LabelTextsContext)

  return (
    <div data-testid={`studio-field-${pointer}`}>
      <div>{t('shell.studio.page.actionLabel')}</div>
      {names !== undefined && label !== null ? (
        <LabelTexts pointer={pointer} value={value} onChange={onChange} />
      ) : (
        <Input
          aria-label={t('shell.studio.labelKey')}
          value={text(isContent(value) ? value.textKey : undefined)}
          onChange={(event) => onChange(`${pointer}/textKey`, event.target.value)}
        />
      )}
    </div>
  )
}

/** The fields of a `navigate` action: its page, label, optional `record` expression and parameter expressions. */
function NavigateFields({ pointer, action, onChange }: { pointer: string; action: Content; onChange: ChangeHandler }) {
  const t = useText()

  return (
    <>
      <NameField
        pointer={`${pointer}/page`}
        label={t('shell.studio.page.actionPage')}
        kind="page"
        current={action.page}
        required
        onPick={(next) => onChange(`${pointer}/page`, next)}
      />
      <ActionLabel pointer={`${pointer}/label`} value={action.label} onChange={onChange} />
      <div data-testid={`studio-field-${pointer}/record`}>
        <div>{t('shell.studio.page.actionRecord')}</div>
        <Input
          aria-label={t('shell.studio.page.actionRecord')}
          value={text(action.record)}
          onChange={(event) =>
            onChange(`${pointer}/record`, event.target.value === '' ? undefined : event.target.value)
          }
        />
      </div>
      <MappingRows
        pointer={`${pointer}/parameters`}
        title={t('shell.studio.page.actionParameters')}
        value={action.parameters}
        keyLabel={t('shell.studio.page.parameterName')}
        target={{ kind: 'expression' }}
        onChange={onChange}
      />
    </>
  )
}

/**
 * A list of actions of a widget, each with its fields. A `navigate` action has a page, a label, a `record` and
 * parameters. Any other action is a `startProcess` action with a process and a label. The list always offers Add
 * navigate action, and offers Add action for `startProcess` only when `startProcess` is set.
 */
function Actions({
  pointer,
  title,
  actions,
  startProcess,
  onChange,
}: {
  pointer: string
  title: string
  actions: unknown
  startProcess: boolean
  onChange: ChangeHandler
}) {
  const t = useText()
  const list = Array.isArray(actions) ? actions : []

  return (
    <div data-testid={`studio-field-${pointer}`}>
      <Flex vertical gap="small">
        <Typography.Text strong>{title}</Typography.Text>
        {actions !== undefined && !Array.isArray(actions) ? (
          <JsonOnly />
        ) : (
          <>
            {list.map((action, position) => {
              const actionPointer = `${pointer}/${position}`
              if (!isContent(action)) {
                return (
                  <Card size="small" key={position} data-testid={`studio-action-${position}`}>
                    <div data-testid={`studio-field-${actionPointer}`}>
                      <JsonOnly />
                    </div>
                  </Card>
                )
              }
              return (
                <Card size="small" key={position} data-testid={`studio-action-${position}`}>
                  <Flex vertical gap="small">
                    <Typography.Text>{text(action.type)}</Typography.Text>
                    {action.type === 'navigate' ? (
                      <NavigateFields pointer={actionPointer} action={action} onChange={onChange} />
                    ) : (
                      <>
                        <NameField
                          pointer={`${actionPointer}/process`}
                          label={t('shell.studio.page.actionProcess')}
                          kind="process"
                          current={action.process}
                          required
                          onPick={(next) => onChange(`${actionPointer}/process`, next)}
                        />
                        <ActionLabel pointer={`${actionPointer}/label`} value={action.label} onChange={onChange} />
                      </>
                    )}
                    <div>
                      <Button danger onClick={() => onChange(actionPointer, undefined)}>
                        {t('shell.studio.page.removeAction')}
                      </Button>
                    </div>
                  </Flex>
                </Card>
              )
            })}
            <Flex gap="small" wrap>
              {startProcess && (
                <Button
                  onClick={() =>
                    onChange(pointer, [...list, { type: 'startProcess', process: '', label: { textKey: '' } }])
                  }
                >
                  {t('shell.studio.page.addAction')}
                </Button>
              )}
              <Button
                onClick={() => onChange(pointer, [...list, { type: 'navigate', page: '', label: { textKey: '' } }])}
              >
                {t('shell.studio.page.addNavigateAction')}
              </Button>
            </Flex>
          </>
        )}
      </Flex>
    </div>
  )
}
