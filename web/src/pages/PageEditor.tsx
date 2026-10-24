import { Button, Card, Flex, Input, InputNumber, Select, Switch, Typography } from 'antd'
import { lazy, Suspense, useContext, useState } from 'react'
import { getAt } from '../platform/jsonPatch'
import {
  addableTypes,
  childLists,
  containerTypes,
  dataSourceColumns,
  isNumberType,
  type ListPart,
  moveTo,
  newColumn,
  newTab,
  newWidget,
  type PathStep,
  typeChanges,
  widgetLists,
  widgetSummary,
  widgetTypes,
} from '../platform/pageWidgets'
import { referenceOptions, StudioNamesContext } from '../platform/studioNames'
import type { StudioNamePage, StudioNames } from '../platform/studio'
import { useText } from '../platform/texts'
import { LabelTexts } from './LabelTexts'
import { ExpressionCheckContext } from './expressionCheckContext'
import { expressionScope } from './expressionScope'
import { LabelTextsContext } from './labelTextsContext'
import { MappingRows } from './MappingRows'
import { ReferencePicker } from './ReferencePicker'
import type { ChangeHandler } from './SchemaForm'

const LazyExpressionEditor = lazy(() => import('./ExpressionEditor'))

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
 * The editor of a page. It shows the page's name and title, its parameters, its widgets as a tree, and a panel for
 * the selected widget. Each parameter has a name, a type, a Required switch and Remove. A `reference` parameter also
 * has a target entity and an `enum` parameter has its values, and a type change removes the one the new type does not
 * take. The tree nests the widgets inside `columns`, `tabs` and `section`. Every list has an Add widget picker,
 * and every widget has Move up, Move down, Remove and a Move to picker that lists the other widget lists. A new
 * container starts with two columns of width 6, one tab, or an empty section. A container's type is fixed. Its panel
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
 * numbers. A property is a name picked from the application's names, and a table's data source and
 * entity, like a form's or a detail's entity and form, exclude each other, so a pick clears the other. The editor edits
 * the draft through `onChange`, so a key it does not show, on the page or on a widget, stays as it is. A widget of a
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
      <PageParameters value={value.parameters} onChange={onChange} />
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
                <WidgetPanel
                  key={selected}
                  base={selected}
                  widget={getAt(value, selected)}
                  parameters={value.parameters}
                  onChange={onChange}
                />
              )}
            </>
          )}
        </Flex>
      </div>
    </Flex>
  )
}

/** The parameters a page declares, each as a card of a name, a type, a Required switch and the fields its type takes. */
function PageParameters({ value, onChange }: { value: unknown; onChange: ChangeHandler }) {
  const t = useText()
  const list = Array.isArray(value) ? value : []

  return (
    <div data-testid="studio-field-/parameters">
      <Flex vertical gap="small">
        <Typography.Text strong>{t('shell.studio.page.parameters')}</Typography.Text>
        {value !== undefined && !Array.isArray(value) ? (
          <JsonOnly />
        ) : (
          <>
            {list.map((parameter, index) => {
              const base = `/parameters/${index}`
              if (!isContent(parameter)) {
                return (
                  <Card size="small" key={index} data-testid={`studio-field-${base}`}>
                    <JsonOnly />
                  </Card>
                )
              }
              return (
                <Card size="small" key={index} data-testid={`studio-field-${base}`}>
                  <PageParameter base={base} parameter={parameter} onChange={onChange} />
                </Card>
              )
            })}
            <div>
              <Button onClick={() => onChange('/parameters', [...list, { name: '', type: 'text' }])}>
                {t('shell.studio.page.addParameter')}
              </Button>
            </div>
          </>
        )}
      </Flex>
    </div>
  )
}

function PageParameter({ base, parameter, onChange }: { base: string; parameter: Content; onChange: ChangeHandler }) {
  const t = useText()
  const type = typeof parameter.type === 'string' ? parameter.type : undefined
  const values = Array.isArray(parameter.values) ? parameter.values.filter((item) => typeof item === 'string') : []

  return (
    <Flex vertical gap="small">
      <Flex gap="small" align="flex-start" wrap>
        <div data-testid={`studio-field-${base}/name`}>
          <Input
            aria-label={t('shell.studio.page.parameterNameLabel')}
            value={text(parameter.name)}
            onChange={(event) => onChange(`${base}/name`, event.target.value)}
          />
        </div>
        <div data-testid={`studio-field-${base}/type`}>
          <Select
            aria-label={t('shell.studio.page.parameterType')}
            virtual={false}
            style={{ minWidth: 160 }}
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
        </div>
        <div data-testid={`studio-field-${base}/required`}>
          <Flex gap="small" align="center">
            <Switch
              aria-label={t('shell.studio.page.parameterRequired')}
              checked={parameter.required === true}
              onChange={(checked) => onChange(`${base}/required`, checked ? true : undefined)}
            />
            <span>{t('shell.studio.page.parameterRequired')}</span>
          </Flex>
        </div>
      </Flex>
      {type === 'reference' && (
        <NameField
          pointer={`${base}/target`}
          label={t('shell.studio.page.parameterTarget')}
          kind="entity"
          current={parameter.target}
          required={false}
          onPick={(next) => onChange(`${base}/target`, next)}
        />
      )}
      {type === 'enum' && (
        <div data-testid={`studio-field-${base}/values`}>
          <div>{t('shell.studio.page.parameterValues')}</div>
          <Select
            mode="tags"
            aria-label={t('shell.studio.page.parameterValues')}
            style={{ minWidth: 240 }}
            open={false}
            value={values}
            onChange={(next: string[]) => onChange(`${base}/values`, next.length === 0 ? undefined : next)}
          />
        </div>
      )}
      <div>
        <Button danger onClick={() => onChange(base, undefined)}>
          {t('shell.studio.page.removeParameter')}
        </Button>
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
  choices,
  onPick,
}: {
  pointer: string
  label: string
  kind: string
  current: unknown
  required: boolean
  /** A name the picker leaves out, ignoring letter case. */
  without?: string
  /** The names the picker lists instead of every name of the kind. */
  choices?: string[]
  onPick: (next: string | undefined) => void
}) {
  const names = useContext(StudioNamesContext)
  const value = typeof current === 'string' ? current : undefined
  const options =
    names === undefined
      ? []
      : (choices ?? referenceOptions(names, kind, undefined, pointer)).filter(
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
  parameters,
  onChange,
}: {
  /** The pointer of the selected widget. */
  base: string
  widget: unknown
  /** The page's parameters, which a detail's record names. */
  parameters: unknown
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
        {isContent(widget) && type === 'columns' && (
          <ColumnsPanel base={base} columns={widget.columns} onChange={onChange} />
        )}
        {isContent(widget) && type === 'tabs' && <TabsPanel base={base} tabs={widget.tabs} onChange={onChange} />}
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
            deleteAction
            onChange={onChange}
          />
          <Actions
            pointer={`${base}/toolbarActions`}
            title={t('shell.studio.page.toolbarActions')}
            actions={widget.toolbarActions}
            startProcess={false}
            deleteAction={false}
            onChange={onChange}
          />
        </>
      )}
      {(type === 'form' || type === 'detail') && (
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
            title={t('shell.studio.page.actions')}
            actions={widget.actions}
            startProcess
            deleteAction={false}
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
      {type === 'kpi' && <KpiPanel base={base} widget={widget} onChange={onChange} />}
      {type === 'chart' && <ChartPanel base={base} widget={widget} onChange={onChange} />}
    </Flex>
  )
}

/**
 * The picker of a column of the data source a widget names, or a plain box while the names have not loaded. A value
 * the data source does not project stays visible and is tagged unknown.
 */
function ColumnField({
  pointer,
  label,
  current,
  options,
  onPick,
}: {
  pointer: string
  label: string
  current: unknown
  /** The columns the picker lists, or `undefined` while the application's names have not loaded. */
  options: string[] | undefined
  onPick: (next: string) => void
}) {
  const value = typeof current === 'string' ? current : undefined

  return (
    <div data-testid={`studio-field-${pointer}`}>
      <div>{label}</div>
      {options === undefined ? (
        <Input aria-label={label} value={text(value)} onChange={(event) => onPick(event.target.value)} />
      ) : (
        <ReferencePicker
          name={label}
          value={value}
          required
          options={options}
          onChange={(next) => onPick(next ?? '')}
        />
      )}
    </div>
  )
}

/** The names of the data sources whose `groupBy` has the given length, or `undefined` while the names have not loaded. */
function dataSourcesGroupedBy(names: StudioNames | undefined, count: number): string[] | undefined {
  return names?.dataSources.filter((item) => item.groupBy?.length === count).map((item) => item.name)
}

/**
 * The panel of a KPI: a data source with a total row, a measure of it and a label. It can add a comparison with its
 * own measure and label, and a navigate action. Picking another data source keeps the names already set, which the
 * pickers tag unknown when the new data source does not project them.
 */
function KpiPanel({ base, widget, onChange }: { base: string; widget: Content; onChange: ChangeHandler }) {
  const t = useText()
  const names = useContext(StudioNamesContext)
  const measures =
    names === undefined
      ? undefined
      : (dataSourceColumns(names, widget.dataSource)?.measures.map((measure) => measure.name) ?? [])
  const { comparison, navigate } = widget

  return (
    <>
      <NameField
        pointer={`${base}/dataSource`}
        label={t('shell.studio.page.dataSource')}
        kind="dataSource"
        choices={dataSourcesGroupedBy(names, 0)}
        current={widget.dataSource}
        required
        onPick={(next) => onChange(`${base}/dataSource`, next)}
      />
      <ColumnField
        pointer={`${base}/measure`}
        label={t('shell.studio.page.measure')}
        current={widget.measure}
        options={measures}
        onPick={(next) => onChange(`${base}/measure`, next)}
      />
      <div data-testid={`studio-field-${base}/label`}>
        <div>{t('shell.studio.page.kpiLabel')}</div>
        <TextReferenceField pointer={`${base}/label`} value={widget.label} onChange={onChange} />
      </div>
      <div data-testid={`studio-field-${base}/comparison`}>
        <Flex vertical gap="small">
          {comparison === undefined ? (
            <div>
              <Button onClick={() => onChange(`${base}/comparison`, { measure: '', label: { textKey: '' } })}>
                {t('shell.studio.page.addComparison')}
              </Button>
            </div>
          ) : isContent(comparison) ? (
            <>
              <Typography.Text strong>{t('shell.studio.page.comparison')}</Typography.Text>
              <ColumnField
                pointer={`${base}/comparison/measure`}
                label={t('shell.studio.page.measure')}
                current={comparison.measure}
                options={measures}
                onPick={(next) => onChange(`${base}/comparison/measure`, next)}
              />
              <div data-testid={`studio-field-${base}/comparison/label`}>
                <div>{t('shell.studio.page.kpiLabel')}</div>
                <TextReferenceField pointer={`${base}/comparison/label`} value={comparison.label} onChange={onChange} />
              </div>
              <div>
                <Button danger onClick={() => onChange(`${base}/comparison`, undefined)}>
                  {t('shell.studio.page.removeComparison')}
                </Button>
              </div>
            </>
          ) : (
            <JsonOnly />
          )}
        </Flex>
      </div>
      <div data-testid={`studio-field-${base}/navigate`}>
        <Flex vertical gap="small">
          {navigate === undefined ? (
            <div>
              <Button
                onClick={() => onChange(`${base}/navigate`, { type: 'navigate', page: '', label: { textKey: '' } })}
              >
                {t('shell.studio.page.addNavigateAction')}
              </Button>
            </div>
          ) : isContent(navigate) ? (
            <>
              <Typography.Text strong>{t('shell.studio.page.navigate')}</Typography.Text>
              <NavigateFields pointer={`${base}/navigate`} action={navigate} onChange={onChange} />
              <div>
                <Button danger onClick={() => onChange(`${base}/navigate`, undefined)}>
                  {t('shell.studio.page.removeNavigate')}
                </Button>
              </div>
            </>
          ) : (
            <JsonOnly />
          )}
        </Flex>
      </div>
    </>
  )
}

/** The kinds of a chart. */
const chartKinds = ['bar', 'line', 'pie']

/**
 * The panel of a chart: a data source with one group field, a kind, the category from that field, a title and a list
 * of measures of the data source that are numbers, each with a label. The list keeps at least one measure.
 */
function ChartPanel({ base, widget, onChange }: { base: string; widget: Content; onChange: ChangeHandler }) {
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
        label={t('shell.studio.page.dataSource')}
        kind="dataSource"
        choices={dataSourcesGroupedBy(names, 1)}
        current={widget.dataSource}
        required
        onPick={(next) => onChange(`${base}/dataSource`, next)}
      />
      <div data-testid={`studio-field-${base}/kind`}>
        <div>{t('shell.studio.page.chartKind')}</div>
        <Select
          aria-label={t('shell.studio.page.chartKind')}
          virtual={false}
          style={{ minWidth: 240 }}
          value={typeof widget.kind === 'string' ? widget.kind : undefined}
          options={chartKinds.map((kind) => ({ value: kind, label: kind }))}
          onChange={(next: string) => onChange(`${base}/kind`, next)}
        />
      </div>
      <ColumnField
        pointer={`${base}/category`}
        label={t('shell.studio.page.category')}
        current={widget.category}
        options={categories}
        onPick={(next) => onChange(`${base}/category`, next)}
      />
      <div data-testid={`studio-field-${base}/title`}>
        <div>{t('shell.studio.page.chartTitle')}</div>
        <TextReferenceField pointer={`${base}/title`} value={widget.title} onChange={onChange} />
      </div>
      <div data-testid={`studio-field-${base}/measures`}>
        <Flex vertical gap="small">
          <Typography.Text strong>{t('shell.studio.page.measures')}</Typography.Text>
          {!Array.isArray(measures) ? (
            <JsonOnly />
          ) : (
            <>
              {measures.map((measure, index) => (
                <Card size="small" key={index} data-testid={`studio-measure-${index}`}>
                  <Flex vertical gap="small">
                    {isContent(measure) ? (
                      <>
                        <ColumnField
                          pointer={`${base}/measures/${index}/name`}
                          label={t('shell.studio.page.measure')}
                          current={measure.name}
                          options={numbers}
                          onPick={(next) => onChange(`${base}/measures/${index}/name`, next)}
                        />
                        <div data-testid={`studio-field-${base}/measures/${index}/label`}>
                          <div>{t('shell.studio.page.measureLabel')}</div>
                          <TextReferenceField
                            // The label editors keep their state by position, so they remount when a measure is
                            // added or removed.
                            key={`${index}:${measures.length}`}
                            pointer={`${base}/measures/${index}/label`}
                            value={measure.label}
                            onChange={onChange}
                          />
                        </div>
                      </>
                    ) : (
                      <JsonOnly />
                    )}
                    <div>
                      <Button
                        danger
                        disabled={measures.length === 1}
                        onClick={() =>
                          onChange(
                            `${base}/measures`,
                            measures.filter((_, at) => at !== index),
                          )
                        }
                      >
                        {t('shell.studio.page.removeMeasure')}
                      </Button>
                    </div>
                  </Flex>
                </Card>
              ))}
              <div>
                <Button
                  onClick={() => onChange(`${base}/measures`, [...measures, { name: '', label: { textKey: '' } }])}
                >
                  {t('shell.studio.page.addMeasure')}
                </Button>
              </div>
            </>
          )}
        </Flex>
      </div>
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
  const t = useText()
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
    <div data-testid={`studio-field-${pointer}`}>
      <div>{t('shell.studio.page.record')}</div>
      <ReferencePicker
        name={t('shell.studio.page.record')}
        value={typeof current === 'string' ? current : undefined}
        required
        options={options}
        onChange={onPick}
      />
    </div>
  )
}

/** The width of each column of a columns widget, and the buttons that add and remove columns. */
function ColumnsPanel({ base, columns, onChange }: { base: string; columns: unknown; onChange: ChangeHandler }) {
  const t = useText()
  if (!Array.isArray(columns)) {
    return <JsonOnly />
  }

  return (
    <Flex vertical gap="small">
      {columns.map((column, index) => (
        <Card size="small" key={index} data-testid={`studio-column-${index}`}>
          <Flex vertical gap="small">
            <Typography.Text>{`${t('shell.studio.page.column')} ${index + 1}`}</Typography.Text>
            {isContent(column) ? (
              <div data-testid={`studio-field-${base}/columns/${index}/width`}>
                <div>{t('shell.studio.page.width')}</div>
                <InputNumber
                  aria-label={t('shell.studio.page.width')}
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
              </div>
            ) : (
              <JsonOnly />
            )}
            <div>
              <Button
                danger
                disabled={columns.length === 1}
                onClick={() =>
                  onChange(
                    `${base}/columns`,
                    columns.filter((_, at) => at !== index),
                  )
                }
              >
                {t('shell.studio.page.removeColumn')}
              </Button>
            </div>
          </Flex>
        </Card>
      ))}
      <div>
        <Button onClick={() => onChange(`${base}/columns`, [...columns, newColumn(columns)])}>
          {t('shell.studio.page.addColumn')}
        </Button>
      </div>
    </Flex>
  )
}

/** The label of each tab of a tabs widget, and the buttons that add and remove tabs. */
function TabsPanel({ base, tabs, onChange }: { base: string; tabs: unknown; onChange: ChangeHandler }) {
  const t = useText()
  if (!Array.isArray(tabs)) {
    return <JsonOnly />
  }

  return (
    <Flex vertical gap="small">
      {tabs.map((tab, index) => (
        <Card size="small" key={index} data-testid={`studio-tab-${index}`}>
          <Flex vertical gap="small">
            <Typography.Text>{`${t('shell.studio.page.tab')} ${index + 1}`}</Typography.Text>
            {isContent(tab) ? (
              <div data-testid={`studio-field-${base}/tabs/${index}/label`}>
                <div>{t('shell.studio.page.tabLabel')}</div>
                <TextReferenceField
                  // The label editors keep their state by position, so they remount when a tab is added or removed.
                  key={`${index}:${tabs.length}`}
                  pointer={`${base}/tabs/${index}/label`}
                  value={tab.label}
                  onChange={onChange}
                />
              </div>
            ) : (
              <JsonOnly />
            )}
            <div>
              <Button
                danger
                disabled={tabs.length === 1}
                onClick={() =>
                  onChange(
                    `${base}/tabs`,
                    tabs.filter((_, at) => at !== index),
                  )
                }
              >
                {t('shell.studio.page.removeTab')}
              </Button>
            </div>
          </Flex>
        </Card>
      ))}
      <div>
        <Button onClick={() => onChange(`${base}/tabs`, [...tabs, newTab()])}>{t('shell.studio.page.addTab')}</Button>
      </div>
    </Flex>
  )
}

/** The title of a section and its Collapsed switch. Switching it off removes the key, because a section starts expanded. */
function SectionPanel({ base, widget, onChange }: { base: string; widget: Content; onChange: ChangeHandler }) {
  const t = useText()

  return (
    <Flex vertical gap="small">
      <div data-testid={`studio-field-${base}/title`}>
        <div>{t('shell.studio.page.title')}</div>
        <TextReferenceField pointer={`${base}/title`} value={widget.title} onChange={onChange} />
      </div>
      <div data-testid={`studio-field-${base}/collapsed`}>
        <Flex gap="small" align="center">
          <Switch
            aria-label={t('shell.studio.page.collapsed')}
            checked={widget.collapsed === true}
            onChange={(checked) => onChange(`${base}/collapsed`, checked ? true : undefined)}
          />
          <span>{t('shell.studio.page.collapsed')}</span>
        </Flex>
      </div>
    </Flex>
  )
}

/** A text reference as a key and a text box per locale, or as the plain key until the names are loaded. */
function TextReferenceField({
  pointer,
  value,
  onChange,
}: {
  pointer: string
  value: unknown
  onChange: ChangeHandler
}) {
  const t = useText()
  const names = useContext(StudioNamesContext)
  const label = useContext(LabelTextsContext)

  if (names !== undefined && label !== null) {
    return <LabelTexts pointer={pointer} value={value} onChange={onChange} />
  }
  return (
    <Input
      aria-label={t('shell.studio.labelKey')}
      value={text(isContent(value) ? value.textKey : undefined)}
      onChange={(event) => onChange(`${pointer}/textKey`, event.target.value)}
    />
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
      <NameField
        pointer={`${pointer}/page`}
        label={t('shell.studio.page.actionPage')}
        kind="page"
        current={action.page}
        required
        onPick={pick}
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
      {page === undefined ? (
        <MappingRows
          pointer={`${pointer}/parameters`}
          title={t('shell.studio.page.actionParameters')}
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
 * One expression for each parameter the target page declares, and nothing for a parameter left empty. A value the
 * page does not declare stays visible with a note and Remove, because the compiler refuses it.
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
  const check = useContext(ExpressionCheckContext)

  if (value !== undefined && !isContent(value)) {
    return (
      <div data-testid={`studio-field-${pointer}`}>
        <div>{t('shell.studio.page.actionParameters')}</div>
        <JsonOnly />
      </div>
    )
  }

  const entries = Object.entries(value ?? {})
  const declared = page.parameters.map((parameter) => parameter.name.toLowerCase())
  const undeclared = entries.filter(([key]) => !declared.includes(key.toLowerCase()))
  const expressionName = t('shell.studio.process.expression')

  function expression(rowPointer: string, current: string) {
    if (check) {
      return (
        <Suspense fallback={<Input aria-label={expressionName} value={current} readOnly />}>
          <LazyExpressionEditor
            application={check.application}
            resourceId={check.resourceId}
            path={rowPointer}
            name={expressionName}
            value={current}
            required={false}
            options={expressionScope(check.kind, check.draft, rowPointer, check.names)}
            onChange={onChange}
          />
        </Suspense>
      )
    }
    return (
      <Input
        aria-label={expressionName}
        value={current}
        onChange={(event) => onChange(rowPointer, event.target.value === '' ? undefined : event.target.value)}
      />
    )
  }

  return (
    <div data-testid={`studio-field-${pointer}`}>
      <Flex vertical gap="small">
        <Typography.Text strong>{t('shell.studio.page.actionParameters')}</Typography.Text>
        {page.parameters.map((parameter) => {
          const key =
            entries.find(([name]) => name.toLowerCase() === parameter.name.toLowerCase())?.[0] ?? parameter.name
          const rowPointer = `${pointer}/${escapeSegment(key)}`
          const current = value?.[key]
          return (
            <div key={parameter.name} data-testid={`studio-field-${rowPointer}`}>
              <Typography.Text type="secondary">
                {`${parameter.name} · ${parameter.type}${parameter.required ? ` · ${t('shell.studio.page.parameterRequired').toLowerCase()}` : ''}`}
              </Typography.Text>
              {expression(rowPointer, typeof current === 'string' ? current : '')}
            </div>
          )
        })}
        {undeclared.map(([key, entry]) => {
          const rowPointer = `${pointer}/${escapeSegment(key)}`
          return (
            <Flex key={key} gap="small" align="flex-start" wrap data-testid={`studio-field-${rowPointer}`}>
              <div>
                <Typography.Text>{key}</Typography.Text>
                <div>
                  <Typography.Text type="warning">{t('shell.studio.page.undeclaredParameter')}</Typography.Text>
                </div>
              </div>
              <div style={{ flex: 1, minWidth: 240 }}>
                {expression(rowPointer, typeof entry === 'string' ? entry : '')}
              </div>
              <Button onClick={() => onChange(rowPointer, undefined)}>{t('shell.studio.process.removeMapping')}</Button>
            </Flex>
          )
        })}
      </Flex>
    </div>
  )
}

/**
 * A list of actions of a widget, each with its fields. A `navigate` action has a page, a label, a `record` and
 * parameters. A `startProcess` action has a process and a label. A `delete` action has a label and a `visibleWhen`.
 * Any other action is JSON only, and can be removed. The list always offers Add navigate action, offers Add action
 * for `startProcess` only when `startProcess` is set and offers Add delete action only when `deleteAction` is set.
 */
function Actions({
  pointer,
  title,
  actions,
  startProcess,
  deleteAction,
  onChange,
}: {
  pointer: string
  title: string
  actions: unknown
  startProcess: boolean
  deleteAction: boolean
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
                    ) : action.type === 'startProcess' ? (
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
                    ) : action.type === 'delete' ? (
                      <>
                        <ActionLabel pointer={`${actionPointer}/label`} value={action.label} onChange={onChange} />
                        <div data-testid={`studio-field-${actionPointer}/visibleWhen`}>
                          <div>{t('shell.studio.page.actionVisibleWhen')}</div>
                          <Input
                            aria-label={t('shell.studio.page.actionVisibleWhen')}
                            value={text(action.visibleWhen)}
                            onChange={(event) =>
                              onChange(
                                `${actionPointer}/visibleWhen`,
                                event.target.value === '' ? undefined : event.target.value,
                              )
                            }
                          />
                        </div>
                      </>
                    ) : (
                      <div data-testid={`studio-field-${actionPointer}`}>
                        <JsonOnly />
                      </div>
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
              {deleteAction && (
                <Button onClick={() => onChange(pointer, [...list, { type: 'delete', label: { textKey: '' } }])}>
                  {t('shell.studio.page.addDeleteAction')}
                </Button>
              )}
            </Flex>
          </>
        )}
      </Flex>
    </div>
  )
}
