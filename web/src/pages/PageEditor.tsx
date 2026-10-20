import { Button, Card, Flex, Input, Select, Typography } from 'antd'
import { useContext, useState } from 'react'
import { typeChanges, widgetSummary, widgetTypes } from '../platform/pageWidgets'
import { referenceOptions, StudioNamesContext } from '../platform/studioNames'
import { useText } from '../platform/texts'
import { LabelTexts } from './LabelTexts'
import { LabelTextsContext } from './labelTextsContext'
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
 * The editor of a page. It shows the page's name and title, its widgets as a list, and a panel for the selected
 * widget's type with every property that type takes. A `form` panel also lists its `startProcess` actions. The page
 * holds one widget today, so the list has no add, remove or reorder. A property is a name picked from the
 * application's names, and a table's data source and entity, like a form's entity and form, exclude each other, so a
 * pick clears the other. The editor edits the draft through `onChange`, so a key it does not show, on the page or
 * on a widget, stays as it is. A widget of a type it has no panel for shows its type and points to the JSON view.
 */
export function PageEditor({ value, onChange }: { value: Content; onChange: ChangeHandler }) {
  const t = useText()
  const names = useContext(StudioNamesContext)
  const label = useContext(LabelTextsContext)
  const [selected, setSelected] = useState(0)
  const widgets = Array.isArray(value.widgets) ? value.widgets : undefined
  const index = widgets === undefined ? 0 : Math.min(selected, Math.max(widgets.length - 1, 0))

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
              <Flex vertical gap="small" role="list">
                {widgets.map((widget, position) => (
                  <Button
                    key={position}
                    role="listitem"
                    type={position === index ? 'primary' : 'default'}
                    aria-current={position === index}
                    data-testid={`studio-widget-${position}`}
                    style={{ textAlign: 'left' }}
                    onClick={() => setSelected(position)}
                  >
                    {isContent(widget) ? widgetSummary(widget) : ''}
                  </Button>
                ))}
              </Flex>
              {widgets.length > 0 && (
                <WidgetPanel index={index} widget={widgets[index]} pageName={text(value.name)} onChange={onChange} />
              )}
            </>
          )}
        </Flex>
      </div>
    </Flex>
  )
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
  index,
  widget,
  pageName,
  onChange,
}: {
  index: number
  widget: unknown
  pageName: string
  onChange: ChangeHandler
}) {
  const t = useText()
  const base = `/widgets/${index}`
  const type = isContent(widget) && typeof widget.type === 'string' ? widget.type : undefined

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
          <NameField
            pointer={`${base}/formPage`}
            label={t('shell.studio.page.formPage')}
            kind="page"
            current={widget.formPage}
            required={false}
            without={pageName}
            onPick={(next) => pick('formPage', next)}
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
          <Actions pointer={`${base}/actions`} actions={widget.actions} onChange={onChange} />
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

/** The `startProcess` actions of a form widget, each with its process and label. */
function Actions({ pointer, actions, onChange }: { pointer: string; actions: unknown; onChange: ChangeHandler }) {
  const t = useText()
  const names = useContext(StudioNamesContext)
  const label = useContext(LabelTextsContext)
  const list = Array.isArray(actions) ? actions : []

  return (
    <div data-testid={`studio-field-${pointer}`}>
      <Flex vertical gap="small">
        <Typography.Text strong>{t('shell.studio.page.actions')}</Typography.Text>
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
                    <NameField
                      pointer={`${actionPointer}/process`}
                      label={t('shell.studio.page.actionProcess')}
                      kind="process"
                      current={action.process}
                      required
                      onPick={(next) => onChange(`${actionPointer}/process`, next)}
                    />
                    <div data-testid={`studio-field-${actionPointer}/label`}>
                      <div>{t('shell.studio.page.actionLabel')}</div>
                      {names !== undefined && label !== null ? (
                        <LabelTexts pointer={`${actionPointer}/label`} value={action.label} onChange={onChange} />
                      ) : (
                        <Input
                          aria-label={t('shell.studio.labelKey')}
                          value={text(isContent(action.label) ? action.label.textKey : undefined)}
                          onChange={(event) => onChange(`${actionPointer}/label/textKey`, event.target.value)}
                        />
                      )}
                    </div>
                    <div>
                      <Button danger onClick={() => onChange(actionPointer, undefined)}>
                        {t('shell.studio.page.removeAction')}
                      </Button>
                    </div>
                  </Flex>
                </Card>
              )
            })}
            <div>
              <Button
                onClick={() =>
                  onChange(pointer, [...list, { type: 'startProcess', process: '', label: { textKey: '' } }])
                }
              >
                {t('shell.studio.page.addAction')}
              </Button>
            </div>
          </>
        )}
      </Flex>
    </div>
  )
}
