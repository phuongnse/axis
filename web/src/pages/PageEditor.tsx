import { Flex, Input, InputNumber, Select, Typography } from 'antd'
import { useContext, useEffect, useRef, useState, type ReactNode } from 'react'
import { AxisButton, AxisButtonGroup } from '../platform/AxisButton'
import { AxisCheckboxField } from '../platform/AxisCheckboxField'
import { AxisField, type AxisFieldProps } from '../platform/AxisField'
import { AxisFieldGroup } from '../platform/AxisFieldGroup'
import { RowActions } from '../platform/AxisTable'
import { EmptyState } from '../platform/EmptyState'
import { fillText } from '../platform/fillText'
import { getAt } from '../platform/jsonPatch'
import {
  actionKeyOf,
  addableTypes,
  childLists,
  containerTypes,
  dataSourceColumns,
  dataSourceParameters,
  isNumberType,
  type ListPart,
  moveTo,
  namedWidget,
  newColumn,
  newTab,
  newWidget,
  pageFieldPath,
  typeChanges,
  widgetLists,
  widgetPointerOf,
  widgetSummary,
  widgetTypes,
} from '../platform/pageWidgets'
import { referenceOptions, StudioNamesContext, type ReferenceOption } from '../platform/studioNames'
import type { StudioNamePage, StudioNames } from '../platform/studio'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { WarningLine } from '../platform/WarningLine'
import { LabelTexts } from './LabelTexts'
import { LayerMarkTag, RemovedRows, type DesignerLayer } from './LayerMarkViews'
import { useFieldTexts } from './fieldTexts'
import { MappingRows } from './MappingRows'
import { stepLabels, usePartLabel } from './pagePaletteSupport'
import { Expression } from './ProcessFields'
import { uniqueStepName } from './processPalette'
import { ReferencePicker } from './ReferencePicker'
import { SectionPart, SectionParts } from '../platform/SectionParts'
import type { ChangeHandler } from './SchemaForm'

type Content = Record<string, unknown>

/** The types a page parameter takes. */
const parameterTypes = ['text', 'integer', 'decimal', 'boolean', 'date', 'date-time', 'enum', 'reference']

function escapeSegment(segment: string): string {
  return segment.replaceAll('~', '~0').replaceAll('/', '~1')
}

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
 * The editor of a page. Its General section shows the page's name, title and description, its Parameters section lists the
 * parameters, and its Widgets section shows the widgets as a tree, and a panel for the selected widget. Each parameter
 * is a row with a name, a type, a Required switch and a "…" menu that holds Remove parameter. A `reference` parameter
 * also has a target entity and an `enum` parameter has its values, and a type change removes the one the new type does
 * not take. The tree nests the widgets inside `columns`, `tabs` and `section`. Every list has an Add widget picker, and
 * every widget has a Move to picker that lists the other widget lists and a "…" menu with Move up, Move down and Delete.
 * A new container starts with two columns of width 6, one tab, or an empty section. A container's type is fixed. Its panel
 * edits the width of each column, the label of each tab and the title and collapsed state of a section, and adds
 * or removes columns and tabs. The panel of a `table`, `form`, `taskInbox`, `processInspector`, `kpi`, `chart` or `detail` widget
 * shows its type with every property that type takes. A `detail` panel has a record picker over the page's required
 * `reference` parameters, narrowed to the widget's entity when it names one. A `form` and a `detail` panel also list
 * their `actions`, which are `startProcess` or `navigate`, and a `table` panel lists its `rowActions`, which are
 * `navigate` or `delete`, and its `toolbarActions`, which are `navigate`. A navigate action has a target page, a label, a `record` expression and
 * parameter expressions. When the names list the target page, the action shows one expression for each parameter the
 * page declares, and an empty expression leaves the parameter out. Picking another page removes the values the new
 * page does not declare. A `kpi` panel has a data source with a total row, a measure, a label, and an optional comparison
 * and navigate action. A `chart` panel has a data source with one group field, a kind, a category, a title and a list of
 * measures. Their column pickers list only what the chosen data source projects, and a chart's measures only the
 * numbers. A `kpi` and a `chart` panel also list the parameters of their data source, each with a picker over the
 * page's parameters of the same type, which sets the widget's `parameters`. A property is a name picked from the
 * application's names, and a table's data source and entity, like a form's or a detail's entity and form, exclude
 * each other, so a pick clears the other. The editor edits the draft through `onChange`, so a key it does not show,
 * on the page or on a widget, stays as it is. A widget of a type it has no panel for shows its type and points to the
 * JSON view. The caller can keep the selected widget and pass it as `selected` and `onSelect`. The selection can also
 * name an action of a widget, such as `/widgets/0/rowActions/1`, or `/widgets/0/navigate` for a KPI. The tree and the
 * panel are then those of the widget, and the panel marks the action's row with `aria-current`, scrolls it into view
 * and, when the action is new, selects it. Removing an action selects its widget.
 *
 * The editor does not mark a customization layer. A caller that shows one uses {@link PageWidgets}.
 */
export function PageEditor({
  value,
  onChange,
  selected,
  onSelect,
}: {
  value: Content
  onChange: ChangeHandler
  /** The pointer of the selected widget or action, when the caller keeps the selection. Otherwise the editor keeps it. */
  selected?: string
  onSelect?: (pointer: string) => void
}) {
  const t = useText()
  const { field, group } = useFieldTexts('page')
  const [picked, setPicked] = useState('/widgets/0')
  const heading = (name: string) => ({
    title: t(`shell.studio.section.page.${name}`),
    line: t(`shell.studio.section.page.${name}.line`),
  })

  return (
    <div data-testid="studio-page-editor">
      <SectionParts>
        <SectionPart testName="general" heading={heading('general')}>
          <Flex vertical gap="small">
            <div data-testid="studio-field-/name">
              <AxisField {...field('name')}>
                <Input value={text(value.name)} onChange={(event) => onChange('/name', event.target.value)} />
              </AxisField>
            </div>
            <div data-testid="studio-field-/title">
              <AxisFieldGroup {...group('title')}>
                <LabelTexts pointer="/title" value={value.title} onChange={onChange} />
              </AxisFieldGroup>
            </div>
            <div data-testid="studio-field-/description">
              <AxisFieldGroup {...group('description')}>
                <LabelTexts pointer="/description" value={value.description} onChange={onChange} />
              </AxisFieldGroup>
            </div>
          </Flex>
        </SectionPart>
        <PageParameters value={value.parameters} onChange={onChange} />
        <SectionPart testName="widgets" heading={heading('widgets')}>
          <PageWidgets
            named
            value={value}
            selected={selected ?? picked}
            onSelect={onSelect ?? setPicked}
            onChange={onChange}
          />
        </SectionPart>
      </SectionParts>
    </div>
  )
}

/**
 * The widgets of a page as a tree, and the panel of the selected widget below it. The selection is a pointer that the
 * caller keeps, so that other views of the page can share it. A pointer that names nothing selects the first widget.
 * A pointer to an action of a widget selects the widget, and its panel marks the action.
 *
 * With `named`, the caller titles the widgets, as the editor's section does, so the tree draws no group of its own. Without
 * it, the tree is a titled group with its help, as the designer's side pane shows it.
 *
 * With `layer`, the tree shows a module page with the application's customization layer applied. A widget the layer
 * added or changed is marked Added or Changed on its row, and so are the actions, row actions and toolbar actions on
 * their rows and the columns and tabs on their rows in the panel, each with Reset to module. An element the layer
 * removed is a muted row marked Removed under the list that held it, with Reset to module.
 */
export function PageWidgets({
  value,
  selected: pointer,
  onSelect,
  onChange,
  layer,
  named = false,
}: {
  value: Content
  selected: string
  onSelect: (pointer: string) => void
  onChange: ChangeHandler
  layer?: DesignerLayer
  named?: boolean
}) {
  const { group } = useFieldTexts('page')
  const widgets = Array.isArray(value.widgets) ? value.widgets : undefined
  const widgetPointer = widgetPointerOf(pointer)
  const selected = getAt(value, widgetPointer) === undefined ? '/widgets/0' : widgetPointer
  const selectedAction =
    selected === widgetPointer && actionKeyOf(pointer) !== undefined && getAt(value, pointer) !== undefined
      ? pointer
      : undefined
  const body =
    widgets === undefined ? (
      <JsonOnly />
    ) : (
      <>
        <WidgetList pointer="/widgets" widgets={widgets} tree={{ widgets, selected, onSelect, onChange, layer }} />
        {widgets.length > 0 && (
          <WidgetPanel
            key={selected}
            base={selected}
            widget={getAt(value, selected)}
            parameters={value.parameters}
            selectedAction={selectedAction}
            layer={layer}
            onSelect={onSelect}
            onChange={onChange}
          />
        )}
      </>
    )

  return (
    <div data-testid="studio-field-/widgets">
      {named ? (
        <Flex vertical gap="small">
          {body}
        </Flex>
      ) : (
        <AxisFieldGroup {...group('widgets')}>
          <Flex vertical gap="small">
            {body}
          </Flex>
        </AxisFieldGroup>
      )}
    </div>
  )
}

/**
 * The parameters a page declares, each as a row with no box: a name, a type, a Required switch and the fields its type
 * takes, with Remove parameter in the row's "…" menu, see docs/reference/studio-editors-by-kind.md#page-parameters.
 */
function PageParameters({ value, onChange }: { value: unknown; onChange: ChangeHandler }) {
  const t = useText()
  const list = Array.isArray(value) ? value : []
  const editable = value === undefined || Array.isArray(value)

  return (
    <SectionPart
      testName="parameters"
      heading={{
        title: t('shell.studio.section.page.parameters'),
        line: t('shell.studio.section.page.parameters.line'),
      }}
      action={
        editable ? (
          <AxisButton role="secondary" onClick={() => onChange('/parameters', [...list, { name: '', type: 'text' }])}>
            {t('shell.studio.page.addParameter')}
          </AxisButton>
        ) : undefined
      }
    >
      <div data-testid="studio-field-/parameters">
        {!editable ? (
          <JsonOnly />
        ) : (
          <Flex vertical gap="small">
            {list.length === 0 && <EmptyState text={t('shell.studio.page.noParameters')} />}
            {list.map((parameter, index) => {
              const base = `/parameters/${index}`
              if (!isContent(parameter)) {
                // A parameter that is not an object has no Remove, as before, so it keeps its line only.
                return (
                  <div key={index} data-testid={`studio-field-${base}`}>
                    <JsonOnly />
                  </div>
                )
              }
              return (
                <Flex key={index} gap="small" align="flex-start" data-testid={`studio-field-${base}`}>
                  <div style={{ flex: 1 }}>
                    <PageParameter base={base} parameter={parameter} onChange={onChange} />
                  </div>
                  <RowActions
                    fold
                    actions={[
                      {
                        key: 'remove',
                        labelKey: 'shell.studio.page.removeParameter',
                        destructive: true,
                        onClick: () => onChange(base, undefined),
                      },
                    ]}
                  />
                </Flex>
              )
            })}
          </Flex>
        )}
      </div>
    </SectionPart>
  )
}

function PageParameter({ base, parameter, onChange }: { base: string; parameter: Content; onChange: ChangeHandler }) {
  const { field } = useFieldTexts('page')
  const type = typeof parameter.type === 'string' ? parameter.type : undefined
  const values = Array.isArray(parameter.values) ? parameter.values.filter((item) => typeof item === 'string') : []

  return (
    <Flex vertical gap="small">
      <div data-testid={`studio-field-${base}/name`}>
        <AxisField {...field('parameters.name')}>
          <Input value={text(parameter.name)} onChange={(event) => onChange(`${base}/name`, event.target.value)} />
        </AxisField>
      </div>
      <div data-testid={`studio-field-${base}/type`}>
        <AxisField {...field('parameters.type')}>
          <Select
            virtual={false}
            value={type}
            options={parameterTypes.map((option) => ({ value: option, label: option }))}
            onChange={(next: string) => {
              onChange(`${base}/type`, next)
              if (next !== 'reference' && parameter.target !== undefined) {
                onChange(`${base}/target`, undefined)
              }
              if (next !== 'enum' && parameter.values !== undefined) {
                onChange(`${base}/values`, undefined)
              }
            }}
          />
        </AxisField>
      </div>
      <div data-testid={`studio-field-${base}/required`}>
        <AxisCheckboxField
          kind="switch"
          {...field('parameters.required')}
          checked={parameter.required === true}
          onChange={(checked) => onChange(`${base}/required`, checked ? true : undefined)}
        />
      </div>
      {type === 'reference' && (
        <NameField
          pointer={`${base}/target`}
          kind="entity"
          current={parameter.target}
          required={false}
          onPick={(next) => onChange(`${base}/target`, next)}
        />
      )}
      {type === 'enum' && (
        <div data-testid={`studio-field-${base}/values`}>
          <AxisField {...field('parameters.values')}>
            <Select
              mode="tags"
              open={false}
              value={values}
              onChange={(next: string[]) => onChange(`${base}/values`, next.length === 0 ? undefined : next)}
            />
          </AxisField>
        </div>
      )}
    </Flex>
  )
}

/** What every list and row of the widget tree needs: the page's widgets, the selected pointer and the edits. */
interface Tree {
  widgets: unknown[]
  selected: string
  onSelect: (pointer: string) => void
  onChange: ChangeHandler
  layer?: DesignerLayer
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
      <RemovedRows layer={tree.layer} list={pointer} />
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
          onChange={(type: string) => tree.onChange(pointer, [...widgets, namedWidget(tree.widgets, newWidget(type))])}
        />
      </div>
    </Flex>
  )
}

/**
 * One widget: the button that selects it, a Move to picker, a "…" menu with Move up, Move down and Delete, and the lists
 * inside a container. See docs/reference/studio-editors-by-kind.md#widget-tree.
 */
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
      return isContent(column) && typeof column.width === 'number'
        ? fillText(t('shell.studio.page.columnWidth'), { number: list.part.index + 1, width: column.width })
        : name
    }
    return name
  }

  return (
    <div role="listitem">
      <Flex vertical gap="small">
        <Flex gap="small" wrap align="center" data-testid={`studio-widget-row-${pointer}`}>
          <AxisButton
            role={pointer === tree.selected ? 'main' : 'secondary'}
            aria-current={pointer === tree.selected}
            data-testid={`studio-widget-${pointer}`}
            style={{ textAlign: 'left' }}
            onClick={() => tree.onSelect(pointer)}
          >
            {content !== undefined ? widgetSummary(content) : ''}
          </AxisButton>
          {tree.layer !== undefined && typeof content?.name === 'string' && (
            <LayerMarkTag layer={tree.layer} list={listPointer} index={index} name={content.name} />
          )}
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
          <RowActions
            fold
            actions={[
              {
                key: 'moveUp',
                labelKey: 'shell.studio.page.moveUp',
                disabled: index === 0,
                onClick: () => move(-1),
              },
              {
                key: 'moveDown',
                labelKey: 'shell.studio.page.moveDown',
                disabled: index === widgets.length - 1,
                onClick: () => move(1),
              },
              {
                key: 'delete',
                labelKey: 'shell.studio.delete',
                destructive: true,
                onClick: () =>
                  tree.onChange(
                    listPointer,
                    widgets.filter((_, at) => at !== index),
                  ),
              },
            ]}
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

/** A field of the page property at `pointer`, labelled and helped by its path, see {@link pageFieldPath}. */
function PageField({ pointer, children }: { pointer: string; children: AxisFieldProps['children'] }) {
  const { field } = useFieldTexts('page')
  return (
    <div data-testid={`studio-field-${pointer}`}>
      <AxisField {...field(pageFieldPath(pointer))}>{children}</AxisField>
    </div>
  )
}

/**
 * A group of the page property at `pointer`, titled and helped by its path, see {@link pageFieldPath}. An `action` sits
 * on its title row as a light text button.
 */
function PageFieldGroup({ pointer, action, children }: { pointer: string; action?: ReactNode; children: ReactNode }) {
  const { group } = useFieldTexts('page')
  return (
    <div data-testid={`studio-field-${pointer}`}>
      <AxisFieldGroup {...group(pageFieldPath(pointer))} action={action}>
        {children}
      </AxisFieldGroup>
    </div>
  )
}

/** The picker of a name, or a plain box while the application's names have not loaded. */
function NameField({
  pointer,
  kind,
  current,
  required,
  without,
  choices,
  onPick,
}: {
  pointer: string
  kind: string
  current: unknown
  required: boolean
  /** A name the picker leaves out, ignoring letter case. */
  without?: string
  /** The names the picker lists instead of every name of the kind. */
  choices?: (string | ReferenceOption)[]
  onPick: (next: string | undefined) => void
}) {
  const { field } = useFieldTexts('page')
  const { label } = field(pageFieldPath(pointer))
  const names = useContext(StudioNamesContext)
  const value = typeof current === 'string' ? current : undefined
  const options =
    names === undefined
      ? []
      : (choices ?? referenceOptions(names, kind, undefined, pointer)).filter(
          (option) =>
            without === undefined ||
            without === '' ||
            (typeof option === 'string' ? option : option.name).toLowerCase() !== without.toLowerCase(),
        )

  return (
    <PageField pointer={pointer}>
      {names === undefined ? (
        <Input
          value={text(value)}
          onChange={(event) => onPick(event.target.value === '' && !required ? undefined : event.target.value)}
        />
      ) : (
        <ReferencePicker name={label} value={value} required={required} options={options} onChange={onPick} />
      )}
    </PageField>
  )
}

function WidgetPanel({
  base,
  widget,
  parameters,
  selectedAction,
  layer,
  onSelect,
  onChange,
}: {
  /** The pointer of the selected widget. */
  base: string
  widget: unknown
  /** The page's parameters, which a detail's record names and a KPI or chart binds. */
  parameters: unknown
  /** The pointer of the selected action of the widget, which its row marks. */
  selectedAction: string | undefined
  /** The customization layer to mark, when the page is a module page. */
  layer?: DesignerLayer
  onSelect: (pointer: string) => void
  onChange: ChangeHandler
}) {
  const type = isContent(widget) && typeof widget.type === 'string' ? widget.type : undefined

  if (type !== undefined && containerTypes.includes(type)) {
    return (
      <Flex vertical gap="small" data-testid={`studio-field-${base}`}>
        <PageField pointer={`${base}/type`}>
          <Input readOnly value={type} />
        </PageField>
        {isContent(widget) && type === 'columns' && (
          <ColumnsPanel base={base} columns={widget.columns} layer={layer} onChange={onChange} />
        )}
        {isContent(widget) && type === 'tabs' && (
          <TabsPanel base={base} tabs={widget.tabs} layer={layer} onChange={onChange} />
        )}
        {isContent(widget) && type === 'section' && <SectionPanel base={base} widget={widget} onChange={onChange} />}
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
      <PageField pointer={`${base}/type`}>
        <Select
          virtual={false}
          value={type}
          options={widgetTypes.map((option) => ({ value: option, label: option }))}
          onChange={(next: string) => {
            for (const [key, change] of typeChanges(widget, next)) {
              onChange(`${base}${key}`, change)
            }
          }}
        />
      </PageField>
      {type === 'table' && (
        <>
          <NameField
            pointer={`${base}/entity`}
            kind="entity"
            current={widget.entity}
            required={false}
            onPick={(next) => pick('entity', next, 'dataSource')}
          />
          <NameField
            pointer={`${base}/dataSource`}
            kind="dataSource"
            current={widget.dataSource}
            required={false}
            onPick={(next) => pick('dataSource', next, 'entity')}
          />
          <Actions
            pointer={`${base}/rowActions`}
            actions={widget.rowActions}
            startProcess={false}
            deleteAction
            widget={base}
            selected={selectedAction}
            layer={layer}
            onSelect={onSelect}
            onChange={onChange}
          />
          <Actions
            pointer={`${base}/toolbarActions`}
            actions={widget.toolbarActions}
            startProcess={false}
            deleteAction={false}
            widget={base}
            selected={selectedAction}
            layer={layer}
            onSelect={onSelect}
            onChange={onChange}
          />
        </>
      )}
      {(type === 'form' || type === 'detail') && (
        <>
          <NameField
            pointer={`${base}/entity`}
            kind="entity"
            current={widget.entity}
            required={false}
            onPick={(next) => pick('entity', next, 'form')}
          />
          <NameField
            pointer={`${base}/form`}
            kind="form"
            current={widget.form}
            required={false}
            onPick={(next) => pick('form', next, 'entity')}
          />
          {type === 'detail' && (
            <RecordField
              pointer={`${base}/record`}
              current={widget.record}
              entity={widget.entity}
              parameters={parameters}
              onPick={(next) => onChange(`${base}/record`, next)}
            />
          )}
          <Actions
            pointer={`${base}/actions`}
            actions={widget.actions}
            startProcess
            deleteAction={false}
            widget={base}
            selected={selectedAction}
            layer={layer}
            onSelect={onSelect}
            onChange={onChange}
          />
        </>
      )}
      {type === 'processInspector' && (
        <NameField
          pointer={`${base}/process`}
          kind="process"
          current={widget.process}
          required
          onPick={(next) => pick('process', next)}
        />
      )}
      {type === 'kpi' && (
        <KpiPanel
          base={base}
          widget={widget}
          parameters={parameters}
          selectedAction={selectedAction}
          onSelect={onSelect}
          onChange={onChange}
        />
      )}
      {type === 'chart' && <ChartPanel base={base} widget={widget} parameters={parameters} onChange={onChange} />}
    </Flex>
  )
}

/**
 * The picker of a column of the data source a widget names, or a plain box while the names have not loaded. A value
 * the data source does not project stays visible and is tagged unknown.
 */
function ColumnField({
  pointer,
  current,
  options,
  onPick,
}: {
  pointer: string
  current: unknown
  /** The columns the picker lists, or `undefined` while the application's names have not loaded. */
  options: string[] | undefined
  onPick: (next: string) => void
}) {
  const { field } = useFieldTexts('page')
  const { label } = field(pageFieldPath(pointer))
  const value = typeof current === 'string' ? current : undefined

  return (
    <PageField pointer={pointer}>
      {options === undefined ? (
        <Input value={text(value)} onChange={(event) => onPick(event.target.value)} />
      ) : (
        <ReferencePicker
          name={label}
          value={value}
          required
          options={options}
          onChange={(next) => onPick(next ?? '')}
        />
      )}
    </PageField>
  )
}

/** The names of the data sources whose `groupBy` has the given length, or `undefined` while the names have not loaded. */
function dataSourcesGroupedBy(names: StudioNames | undefined, count: number): string[] | undefined {
  return names?.dataSources.filter((item) => item.groupBy?.length === count).map((item) => item.name)
}

/**
 * The parameter bindings of a KPI or chart: a picker for each parameter of its data source, over the page's parameters
 * of the same type. A binding to a page parameter that is missing or of another type stays visible and is tagged
 * unknown. A key that matches no data source parameter is not shown and stays in the widget.
 */
function ParameterBindings({
  base,
  dataSource,
  value,
  pageParameters,
  onChange,
}: {
  base: string
  dataSource: unknown
  value: unknown
  pageParameters: unknown
  onChange: ChangeHandler
}) {
  const t = useText()
  const names = useContext(StudioNamesContext)
  const parameters = names === undefined ? undefined : dataSourceParameters(names, dataSource)
  if (parameters === undefined || parameters.length === 0) {
    return null
  }

  const pointer = `${base}/parameters`
  if (value !== undefined && !isContent(value)) {
    return (
      <PageFieldGroup pointer={pointer}>
        <JsonOnly />
      </PageFieldGroup>
    )
  }

  const bound: Content = isContent(value) ? value : {}
  const candidates = Array.isArray(pageParameters) ? pageParameters : []

  return (
    <PageFieldGroup pointer={pointer}>
      <Flex vertical gap="small">
        {parameters.map((parameter) => {
          const key =
            Object.keys(bound).find((name) => name.toLowerCase() === parameter.name.toLowerCase()) ?? parameter.name
          const rowPointer = `${pointer}/${escapeSegment(key)}`
          const current = bound[key]
          const options = candidates.flatMap((candidate) =>
            isContent(candidate) && typeof candidate.name === 'string' && candidate.type === parameter.type
              ? [candidate.name]
              : [],
          )
          return (
            <div key={parameter.name} data-testid={`studio-field-${rowPointer}`}>
              <AxisField
                label={parameter.name}
                help={fillText(t('shell.studio.page.parameterBinding.help'), { type: parameter.type })}
              >
                <ReferencePicker
                  name={parameter.name}
                  value={typeof current === 'string' ? current : undefined}
                  required={false}
                  options={options}
                  onChange={(next) => onChange(rowPointer, next)}
                />
              </AxisField>
            </div>
          )
        })}
      </Flex>
    </PageFieldGroup>
  )
}

/**
 * The panel of a KPI: a data source with a total row, a measure of it and a label. It can add a comparison with its
 * own measure and label, and a navigate action. Picking another data source keeps the names already set, which the
 * pickers tag unknown when the new data source does not project them. The navigate action is marked and scrolled into
 * view when it is the selected action. Adding it selects it and removing it selects the widget.
 */
function KpiPanel({
  base,
  widget,
  parameters,
  selectedAction,
  onSelect,
  onChange,
}: {
  base: string
  widget: Content
  parameters: unknown
  selectedAction: string | undefined
  onSelect: (pointer: string) => void
  onChange: ChangeHandler
}) {
  const t = useText()
  const { group } = useFieldTexts('page')
  const names = useContext(StudioNamesContext)
  const measures =
    names === undefined
      ? undefined
      : (dataSourceColumns(names, widget.dataSource)?.measures.map((measure) => measure.name) ?? [])
  const { comparison, navigate } = widget
  const navigateSelected = selectedAction === `${base}/navigate`

  return (
    <>
      <NameField
        pointer={`${base}/dataSource`}
        kind="dataSource"
        choices={dataSourcesGroupedBy(names, 0)}
        current={widget.dataSource}
        required
        onPick={(next) => onChange(`${base}/dataSource`, next)}
      />
      <ParameterBindings
        base={base}
        dataSource={widget.dataSource}
        value={widget.parameters}
        pageParameters={parameters}
        onChange={onChange}
      />
      <ColumnField
        pointer={`${base}/measure`}
        current={widget.measure}
        options={measures}
        onPick={(next) => onChange(`${base}/measure`, next)}
      />
      <PageFieldGroup pointer={`${base}/label`}>
        <LabelTexts pointer={`${base}/label`} value={widget.label} onChange={onChange} />
      </PageFieldGroup>
      <PageFieldGroup pointer={`${base}/comparison`}>
        <Flex vertical gap="small">
          {comparison === undefined ? (
            <div>
              <AxisButton
                role="secondary"
                onClick={() => onChange(`${base}/comparison`, { measure: '', label: { textKey: '' } })}
              >
                {t('shell.studio.page.addComparison')}
              </AxisButton>
            </div>
          ) : isContent(comparison) ? (
            <>
              <ColumnField
                pointer={`${base}/comparison/measure`}
                current={comparison.measure}
                options={measures}
                onPick={(next) => onChange(`${base}/comparison/measure`, next)}
              />
              <PageFieldGroup pointer={`${base}/comparison/label`}>
                <LabelTexts pointer={`${base}/comparison/label`} value={comparison.label} onChange={onChange} />
              </PageFieldGroup>
              <div>
                <AxisButton role="destructive" onClick={() => onChange(`${base}/comparison`, undefined)}>
                  {t('shell.studio.page.removeComparison')}
                </AxisButton>
              </div>
            </>
          ) : (
            <JsonOnly />
          )}
        </Flex>
      </PageFieldGroup>
      <SelectedMark testId={`studio-field-${base}/navigate`} selected={navigateSelected}>
        <AxisFieldGroup {...group(pageFieldPath(`${base}/navigate`))}>
          <Flex vertical gap="small">
            {navigate === undefined ? (
              <div>
                <AxisButton
                  role="secondary"
                  onClick={() => {
                    onChange(`${base}/navigate`, { type: 'navigate', page: '', label: { textKey: '' } })
                    onSelect(`${base}/navigate`)
                  }}
                >
                  {t('shell.studio.page.addNavigateAction')}
                </AxisButton>
              </div>
            ) : isContent(navigate) ? (
              <>
                <NavigateFields pointer={`${base}/navigate`} action={navigate} onChange={onChange} />
                <div>
                  <AxisButton
                    role="destructive"
                    onClick={() => {
                      onChange(`${base}/navigate`, undefined)
                      onSelect(base)
                    }}
                  >
                    {t('shell.studio.page.removeNavigate')}
                  </AxisButton>
                </div>
              </>
            ) : (
              <JsonOnly />
            )}
          </Flex>
        </AxisFieldGroup>
      </SelectedMark>
    </>
  )
}

/** The kinds of a chart. */
const chartKinds = ['bar', 'line', 'pie']

/**
 * The panel of a chart: a data source with one group field, a kind, the category from that field, a title and a list
 * of measures of the data source that are numbers, each with a label. The list keeps at least one measure.
 */
function ChartPanel({
  base,
  widget,
  parameters,
  onChange,
}: {
  base: string
  widget: Content
  parameters: unknown
  onChange: ChangeHandler
}) {
  const t = useText()
  const names = useContext(StudioNamesContext)
  const columns = names === undefined ? undefined : dataSourceColumns(names, widget.dataSource)
  const categories = names === undefined ? undefined : (columns?.groupFields ?? [])
  const numbers =
    names === undefined
      ? undefined
      : (columns?.measures.filter((measure) => isNumberType(measure.type)).map((measure) => measure.name) ?? [])
  const { measures } = widget

  return (
    <>
      <NameField
        pointer={`${base}/dataSource`}
        kind="dataSource"
        choices={dataSourcesGroupedBy(names, 1)}
        current={widget.dataSource}
        required
        onPick={(next) => onChange(`${base}/dataSource`, next)}
      />
      <ParameterBindings
        base={base}
        dataSource={widget.dataSource}
        value={widget.parameters}
        pageParameters={parameters}
        onChange={onChange}
      />
      <PageField pointer={`${base}/kind`}>
        <Select
          virtual={false}
          value={typeof widget.kind === 'string' ? widget.kind : undefined}
          options={chartKinds.map((kind) => ({ value: kind, label: kind }))}
          onChange={(next: string) => onChange(`${base}/kind`, next)}
        />
      </PageField>
      <ColumnField
        pointer={`${base}/category`}
        current={widget.category}
        options={categories}
        onPick={(next) => onChange(`${base}/category`, next)}
      />
      <PageFieldGroup pointer={`${base}/title`}>
        <LabelTexts pointer={`${base}/title`} value={widget.title} onChange={onChange} />
      </PageFieldGroup>
      <PageFieldGroup
        pointer={`${base}/measures`}
        action={
          Array.isArray(measures) ? (
            <AxisButton
              role="secondary"
              onClick={() => onChange(`${base}/measures`, [...measures, { name: '', label: { textKey: '' } }])}
            >
              {t('shell.studio.page.addMeasure')}
            </AxisButton>
          ) : undefined
        }
      >
        {!Array.isArray(measures) ? (
          <JsonOnly />
        ) : (
          <Flex vertical gap="small">
            {measures.length === 0 && <EmptyState text={t('shell.studio.page.noMeasures')} />}
            {measures.map((measure, index) => (
              <Flex key={index} gap="small" align="flex-start" data-testid={`studio-measure-${index}`}>
                <Flex vertical gap="small" style={{ flex: 1, minWidth: 0 }}>
                  {isContent(measure) ? (
                    <>
                      <ColumnField
                        pointer={`${base}/measures/${index}/name`}
                        current={measure.name}
                        options={numbers}
                        onPick={(next) => onChange(`${base}/measures/${index}/name`, next)}
                      />
                      <PageFieldGroup pointer={`${base}/measures/${index}/label`}>
                        <LabelTexts
                          // The label editors keep their state by position, so they remount when a measure is
                          // added or removed.
                          key={`${index}:${measures.length}`}
                          pointer={`${base}/measures/${index}/label`}
                          value={measure.label}
                          onChange={onChange}
                        />
                      </PageFieldGroup>
                    </>
                  ) : (
                    <JsonOnly />
                  )}
                </Flex>
                <RowActions
                  fold
                  actions={[
                    {
                      key: 'remove',
                      labelKey: 'shell.studio.page.removeMeasure',
                      destructive: true,
                      disabled: measures.length === 1,
                      onClick: () =>
                        onChange(
                          `${base}/measures`,
                          measures.filter((_, at) => at !== index),
                        ),
                    },
                  ]}
                />
              </Flex>
            ))}
          </Flex>
        )}
      </PageFieldGroup>
    </>
  )
}

/**
 * The picker of a detail's record, which lists the page's required `reference` parameters. When the widget names an
 * entity, it lists only the parameters whose target is that entity, ignoring letter case.
 */
function RecordField({
  pointer,
  current,
  entity,
  parameters,
  onPick,
}: {
  pointer: string
  current: unknown
  entity: unknown
  parameters: unknown
  onPick: (next: string | undefined) => void
}) {
  const { field } = useFieldTexts('page')
  const wanted = typeof entity === 'string' && entity !== '' ? entity.toLowerCase() : undefined
  const options = (Array.isArray(parameters) ? parameters : []).flatMap((parameter) => {
    if (!isContent(parameter) || typeof parameter.name !== 'string') {
      return []
    }
    const target = typeof parameter.target === 'string' ? parameter.target.toLowerCase() : undefined
    const fits =
      parameter.required === true && parameter.type === 'reference' && (wanted === undefined || target === wanted)
    return fits ? [parameter.name] : []
  })

  return (
    <PageField pointer={pointer}>
      <ReferencePicker
        name={field(pageFieldPath(pointer)).label}
        value={typeof current === 'string' ? current : undefined}
        required
        options={options}
        onChange={onPick}
      />
    </PageField>
  )
}

/**
 * The width of each column of a columns widget. Each column is a row with no box, and its Remove column is in the row's
 * "…" menu, off when one column is left. Add column is a light text button on the Columns title row, see
 * docs/reference/studio-editors-by-kind.md#container-panels.
 */
function ColumnsPanel({
  base,
  columns,
  layer,
  onChange,
}: {
  base: string
  columns: unknown
  layer?: DesignerLayer
  onChange: ChangeHandler
}) {
  const t = useText()
  if (!Array.isArray(columns)) {
    return <JsonOnly />
  }

  return (
    <PageFieldGroup
      pointer={`${base}/columns`}
      action={
        <AxisButton role="secondary" onClick={() => onChange(`${base}/columns`, [...columns, newColumn(columns)])}>
          {t('shell.studio.page.addColumn')}
        </AxisButton>
      }
    >
      <Flex vertical gap="small">
        {columns.map((column, index) => (
          <Flex key={index} gap="small" align="flex-start" data-testid={`studio-column-${index}`}>
            <Flex vertical gap="small" style={{ flex: 1, minWidth: 0 }}>
              <Typography.Text>{fillText(t('shell.studio.page.column'), { number: index + 1 })}</Typography.Text>
              {layer !== undefined && isContent(column) && typeof column.name === 'string' && (
                <LayerMarkTag layer={layer} list={`${base}/columns`} index={index} name={column.name} />
              )}
              {isContent(column) ? (
                <PageField pointer={`${base}/columns/${index}/width`}>
                  <InputNumber
                    min={1}
                    max={12}
                    precision={0}
                    value={typeof column.width === 'number' ? column.width : null}
                    onChange={(next) => {
                      if (typeof next === 'number') {
                        onChange(`${base}/columns/${index}/width`, next)
                      }
                    }}
                  />
                </PageField>
              ) : (
                <JsonOnly />
              )}
            </Flex>
            <RowActions
              fold
              actions={[
                {
                  key: 'remove',
                  labelKey: 'shell.studio.page.removeColumn',
                  destructive: true,
                  disabled: columns.length === 1,
                  onClick: () =>
                    onChange(
                      `${base}/columns`,
                      columns.filter((_, at) => at !== index),
                    ),
                },
              ]}
            />
          </Flex>
        ))}
        <RemovedRows layer={layer} list={`${base}/columns`} />
      </Flex>
    </PageFieldGroup>
  )
}

/**
 * The label of each tab of a tabs widget. Each tab is a row with no box, and its Remove tab is in the row's "…" menu, off
 * when one tab is left. Add tab is a light text button on the Tabs title row.
 */
function TabsPanel({
  base,
  tabs,
  layer,
  onChange,
}: {
  base: string
  tabs: unknown
  layer?: DesignerLayer
  onChange: ChangeHandler
}) {
  const t = useText()
  if (!Array.isArray(tabs)) {
    return <JsonOnly />
  }

  return (
    <PageFieldGroup
      pointer={`${base}/tabs`}
      action={
        <AxisButton role="secondary" onClick={() => onChange(`${base}/tabs`, [...tabs, newTab(tabs)])}>
          {t('shell.studio.page.addTab')}
        </AxisButton>
      }
    >
      <Flex vertical gap="small">
        {tabs.map((tab, index) => (
          <Flex key={index} gap="small" align="flex-start" data-testid={`studio-tab-${index}`}>
            <Flex vertical gap="small" style={{ flex: 1, minWidth: 0 }}>
              <Typography.Text>{fillText(t('shell.studio.page.tab'), { number: index + 1 })}</Typography.Text>
              {layer !== undefined && isContent(tab) && typeof tab.name === 'string' && (
                <LayerMarkTag layer={layer} list={`${base}/tabs`} index={index} name={tab.name} />
              )}
              {isContent(tab) ? (
                <PageFieldGroup pointer={`${base}/tabs/${index}/label`}>
                  <LabelTexts
                    // The label editors keep their state by position, so they remount when a tab is added or removed.
                    key={`${index}:${tabs.length}`}
                    pointer={`${base}/tabs/${index}/label`}
                    value={tab.label}
                    onChange={onChange}
                  />
                </PageFieldGroup>
              ) : (
                <JsonOnly />
              )}
            </Flex>
            <RowActions
              fold
              actions={[
                {
                  key: 'remove',
                  labelKey: 'shell.studio.page.removeTab',
                  destructive: true,
                  disabled: tabs.length === 1,
                  onClick: () =>
                    onChange(
                      `${base}/tabs`,
                      tabs.filter((_, at) => at !== index),
                    ),
                },
              ]}
            />
          </Flex>
        ))}
        <RemovedRows layer={layer} list={`${base}/tabs`} />
      </Flex>
    </PageFieldGroup>
  )
}

/** The title of a section and its Collapsed switch. Switching it off removes the key, because a section starts expanded. */
function SectionPanel({ base, widget, onChange }: { base: string; widget: Content; onChange: ChangeHandler }) {
  const { field } = useFieldTexts('page')
  return (
    <Flex vertical gap="small">
      <PageFieldGroup pointer={`${base}/title`}>
        <LabelTexts pointer={`${base}/title`} value={widget.title} onChange={onChange} />
      </PageFieldGroup>
      <div data-testid={`studio-field-${base}/collapsed`}>
        <AxisCheckboxField
          kind="switch"
          {...field(pageFieldPath(`${base}/collapsed`))}
          checked={widget.collapsed === true}
          onChange={(checked) => onChange(`${base}/collapsed`, checked ? true : undefined)}
        />
      </div>
    </Flex>
  )
}

/** The label of an action. */
function ActionLabel({ pointer, value, onChange }: { pointer: string; value: unknown; onChange: ChangeHandler }) {
  return (
    <PageFieldGroup pointer={pointer}>
      <LabelTexts pointer={pointer} value={value} onChange={onChange} />
    </PageFieldGroup>
  )
}

/** The fields of a `navigate` action: its page, label, optional `record` expression and parameter expressions. */
function NavigateFields({ pointer, action, onChange }: { pointer: string; action: Content; onChange: ChangeHandler }) {
  const t = useText()
  const { group } = useFieldTexts('page')
  const names = useContext(StudioNamesContext)
  const page = pageNamed(names, action.page)

  function pick(next: string | undefined) {
    onChange(`${pointer}/page`, next)
    const picked = pageNamed(names, next)
    if (picked !== undefined && isContent(action.parameters)) {
      const declared = picked.parameters.map((parameter) => parameter.name.toLowerCase())
      const kept = Object.entries(action.parameters).filter(([key]) => declared.includes(key.toLowerCase()))
      onChange(`${pointer}/parameters`, kept.length === 0 ? undefined : Object.fromEntries(kept))
    }
  }

  return (
    <>
      <NameField pointer={`${pointer}/page`} kind="page" current={action.page} required onPick={pick} />
      <ActionLabel pointer={`${pointer}/label`} value={action.label} onChange={onChange} />
      <PageField pointer={`${pointer}/record`}>
        <Input
          value={text(action.record)}
          onChange={(event) =>
            onChange(`${pointer}/record`, event.target.value === '' ? undefined : event.target.value)
          }
        />
      </PageField>
      {page === undefined ? (
        <MappingRows
          pointer={`${pointer}/parameters`}
          {...group(pageFieldPath(`${pointer}/parameters`))}
          value={action.parameters}
          keyLabel={t('shell.studio.page.parameterName')}
          target={{ kind: 'expression' }}
          onChange={onChange}
        />
      ) : (
        <ParameterExpressions
          pointer={`${pointer}/parameters`}
          page={page}
          value={action.parameters}
          onChange={onChange}
        />
      )}
    </>
  )
}

/** The page a name picks from the names, ignoring letter case, or `undefined` while the names are unknown. */
function pageNamed(names: StudioNames | undefined, name: unknown): StudioNamePage | undefined {
  if (names === undefined || typeof name !== 'string') {
    return undefined
  }
  return names.pages.find((page) => page.name.toLowerCase() === name.toLowerCase())
}

/**
 * One expression for each parameter the target page declares, labelled by the parameter's name, and nothing for a
 * parameter left empty. A value the page does not declare stays visible with a note under it and Remove, because the
 * compiler refuses it.
 */
function ParameterExpressions({
  pointer,
  page,
  value,
  onChange,
}: {
  pointer: string
  page: StudioNamePage
  value: unknown
  onChange: ChangeHandler
}) {
  const t = useText()

  if (value !== undefined && !isContent(value)) {
    return (
      <PageFieldGroup pointer={pointer}>
        <JsonOnly />
      </PageFieldGroup>
    )
  }

  const entries = Object.entries(value ?? {})
  const declared = page.parameters.map((parameter) => parameter.name.toLowerCase())
  const undeclared = entries.filter(([key]) => !declared.includes(key.toLowerCase()))
  const expressionName = t('shell.studio.process.expression')

  return (
    <PageFieldGroup pointer={pointer}>
      <Flex vertical gap="small">
        {page.parameters.map((parameter) => {
          const key =
            entries.find(([name]) => name.toLowerCase() === parameter.name.toLowerCase())?.[0] ?? parameter.name
          const rowPointer = `${pointer}/${escapeSegment(key)}`
          const current = value?.[key]
          const help = fillText(
            t(
              parameter.required
                ? 'shell.studio.page.requiredParameterExpression.help'
                : 'shell.studio.page.parameterExpression.help',
            ),
            { type: parameter.type },
          )
          return (
            <div key={parameter.name} data-testid={`studio-field-${rowPointer}`}>
              <AxisField label={parameter.name} help={help}>
                <Expression
                  pointer={rowPointer}
                  name={expressionName}
                  value={typeof current === 'string' ? current : ''}
                  required={false}
                  onChange={onChange}
                />
              </AxisField>
            </div>
          )
        })}
        {undeclared.map(([key, entry]) => {
          const rowPointer = `${pointer}/${escapeSegment(key)}`
          return (
            <div key={key} data-testid={`studio-field-${rowPointer}`}>
              <Flex gap="small" align="flex-end" wrap>
                <div style={{ flex: 1, minWidth: 240 }}>
                  <AxisField label={key} help={t('shell.studio.page.undeclaredParameter.help')}>
                    <Expression
                      pointer={rowPointer}
                      name={expressionName}
                      value={typeof entry === 'string' ? entry : ''}
                      required={false}
                      onChange={onChange}
                    />
                  </AxisField>
                </div>
                <AxisButton role="destructive" onClick={() => onChange(rowPointer, undefined)}>
                  {t('shell.studio.process.removeMapping')}
                </AxisButton>
              </Flex>
              <WarningLine testId="studio-parameter-undeclared">
                {t('shell.studio.page.undeclaredParameter')}
              </WarningLine>
            </div>
          )
        })}
      </Flex>
    </PageFieldGroup>
  )
}

/**
 * A list of actions of a widget, each with its fields. A `navigate` action has a page, a label, a `record` and
 * parameters. A `startProcess` action has a process and a label. A `delete` action has a label and a `visibleWhen`.
 * Any other action is JSON only, and can be removed. The Adds are one group of light text buttons on the list's title
 * row: Add navigate action always, Add action for `startProcess` only when `startProcess` is set, and Add delete action
 * only when `deleteAction` is set. Each action is a row with no box, and Remove action is in its "…" menu. The row of
 * the action at `selected` has `aria-current` and a border in the primary colour, and scrolls into view. An Add
 * selects the new action, and Remove selects the `widget`. With `layer`, a row of an action that the layer added or
 * changed is marked, and a removed action is a muted row under the rows.
 */
function Actions({
  pointer,
  actions,
  startProcess,
  deleteAction,
  widget,
  selected,
  layer,
  onSelect,
  onChange,
}: {
  pointer: string
  actions: unknown
  startProcess: boolean
  deleteAction: boolean
  /** The pointer of the widget that holds the list. */
  widget: string
  /** The pointer of the selected action. */
  selected: string | undefined
  layer?: DesignerLayer
  onSelect: (pointer: string) => void
  onChange: ChangeHandler
}) {
  const t = useText()
  const list = Array.isArray(actions) ? actions : []

  function add(action: Content) {
    onChange(pointer, [...list, { type: action.type, name: uniqueStepName(list, action.type as string), ...action }])
    onSelect(`${pointer}/${list.length}`)
  }

  const adds = [
    ...(startProcess
      ? [
          {
            key: 'startProcess',
            label: t('shell.studio.page.addAction'),
            onClick: () => add({ type: 'startProcess', process: '', label: { textKey: '' } }),
          },
        ]
      : []),
    {
      key: 'navigate',
      label: t('shell.studio.page.addNavigateAction'),
      onClick: () => add({ type: 'navigate', page: '', label: { textKey: '' } }),
    },
    ...(deleteAction
      ? [
          {
            key: 'delete',
            label: t('shell.studio.page.addDeleteAction'),
            onClick: () => add({ type: 'delete', label: { textKey: '' } }),
          },
        ]
      : []),
  ]

  /** Remove action in the row's "…" menu, which selects the widget. */
  function removeMenu(actionPointer: string) {
    return (
      <RowActions
        fold
        actions={[
          {
            key: 'remove',
            labelKey: 'shell.studio.page.removeAction',
            destructive: true,
            onClick: () => {
              onChange(actionPointer, undefined)
              onSelect(widget)
            },
          },
        ]}
      />
    )
  }

  return (
    <PageFieldGroup
      pointer={pointer}
      action={
        actions === undefined || Array.isArray(actions) ? (
          <AxisButtonGroup role="secondary" nameKey="shell.studio.page.addActions" actions={adds} />
        ) : undefined
      }
    >
      {actions !== undefined && !Array.isArray(actions) ? (
        <JsonOnly />
      ) : (
        <Flex vertical gap="small">
          {list.length === 0 && <EmptyState text={t('shell.studio.page.noActions')} />}
          {list.map((action, position) => {
            const actionPointer = `${pointer}/${position}`
            if (!isContent(action)) {
              return (
                <SelectedMark key={position} testId={`studio-action-${position}`} selected={actionPointer === selected}>
                  <Flex vertical gap="small">
                    <Flex justify="flex-end">{removeMenu(actionPointer)}</Flex>
                    <div data-testid={`studio-field-${actionPointer}`}>
                      <JsonOnly />
                    </div>
                  </Flex>
                </SelectedMark>
              )
            }
            return (
              <SelectedMark key={position} testId={`studio-action-${position}`} selected={actionPointer === selected}>
                <Flex vertical gap="small">
                  <Flex gap="small" align="center" justify="space-between">
                    <Flex gap="small" align="center" wrap>
                      <Typography.Text>{text(action.type)}</Typography.Text>
                      {layer !== undefined && typeof action.name === 'string' && (
                        <LayerMarkTag layer={layer} list={pointer} index={position} name={action.name} />
                      )}
                    </Flex>
                    {removeMenu(actionPointer)}
                  </Flex>
                  {action.type === 'navigate' ? (
                    <NavigateFields pointer={actionPointer} action={action} onChange={onChange} />
                  ) : action.type === 'startProcess' ? (
                    <>
                      <NameField
                        pointer={`${actionPointer}/process`}
                        kind="process"
                        current={action.process}
                        required
                        onPick={(next) => onChange(`${actionPointer}/process`, next)}
                      />
                      <ActionLabel pointer={`${actionPointer}/label`} value={action.label} onChange={onChange} />
                    </>
                  ) : action.type === 'delete' ? (
                    <>
                      <ActionLabel pointer={`${actionPointer}/label`} value={action.label} onChange={onChange} />
                      <PageField pointer={`${actionPointer}/visibleWhen`}>
                        <Input
                          value={text(action.visibleWhen)}
                          onChange={(event) =>
                            onChange(
                              `${actionPointer}/visibleWhen`,
                              event.target.value === '' ? undefined : event.target.value,
                            )
                          }
                        />
                      </PageField>
                    </>
                  ) : (
                    <div data-testid={`studio-field-${actionPointer}`}>
                      <JsonOnly />
                    </div>
                  )}
                </Flex>
              </SelectedMark>
            )
          })}
          <RemovedRows layer={layer} list={pointer} />
        </Flex>
      )}
    </PageFieldGroup>
  )
}

/** A ref that scrolls its element into view when `selected` becomes true. */
function useScrolledTo<T extends HTMLElement>(selected: boolean) {
  const ref = useRef<T>(null)
  useEffect(() => {
    if (selected) {
      ref.current?.scrollIntoView?.({ block: 'nearest' })
    }
  }, [selected])
  return ref
}

/**
 * The row of a selectable action or KPI navigate action. The selected one has `aria-current`, scrolls into view and has
 * an outline in the primary colour. An unselected row has no border, see docs/reference/studio-editors-by-kind.md#navigate-actions.
 */
function SelectedMark({ testId, selected, children }: { testId: string; selected: boolean; children: ReactNode }) {
  const { colour, radius } = useDesignTokens()
  const ref = useScrolledTo<HTMLDivElement>(selected)
  return (
    <div
      ref={ref}
      data-testid={testId}
      aria-current={selected ? 'true' : undefined}
      style={selected ? { border: `1px solid ${colour.primary}`, borderRadius: radius.base, padding: 8 } : undefined}
    >
      {children}
    </div>
  )
}
