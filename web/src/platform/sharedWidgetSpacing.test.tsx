import { DeleteOutlined } from '@ant-design/icons'
import { render } from '@testing-library/react'
import { ConfigProvider, Form, Input } from 'antd'
import type { ReactElement } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { ExpressionField } from '../pages/ExpressionField'
import { ReferencePicker } from '../pages/ReferencePicker'
import { SectionPart, SectionParts } from './SectionParts'
import { withAntdStyles } from '../test/antdStyles'
import { Announcement } from './Announcement'
import { antConfigFor } from './antTheme'
import { AxisActionRow, AxisButton } from './AxisButton'
import { AxisAlert } from './AxisAlert'
import { AxisChoiceField } from './AxisChoiceField'
import { AxisCheckboxField } from './AxisCheckboxField'
import { AxisField } from './AxisField'
import { AxisFieldGroup } from './AxisFieldGroup'
import { AxisFieldRow } from './AxisFieldRow'
import { AcceptTermsField } from './AcceptTermsField'
import { AxisTable } from './AxisTable'
import { CardPage } from './CardPage'
import { EmptyDash } from './EmptyValue'
import { EmptyState } from './EmptyState'
import { StartFromField } from './StartFromField'
import { StepsBar } from './StepsBar'
import { TextProvider } from './TextProvider'

// The shared rule: a shared widget keeps no space around itself, see docs/reference/shared-widgets.md#the-rule.

const texts = {
  'shell.empty.noData': 'No data yet.',
  'shell.table.range': '{from}–{to} of {total}',
  'shell.table.rowActions': 'Actions',
  'shell.table.refresh': 'Refresh',
  'shell.table.selected': '{count} selected',
  'shell.table.search': 'Search',
  'shell.table.columns': 'Columns',
  'shell.table.columnsApply': 'Apply',
  'shell.table.columnsReset': 'Reset',
  'shell.table.columnsMoveUp': 'Move up',
  'shell.table.columnsMoveDown': 'Move down',
  'shell.form.cancel': 'Cancel',
  'shell.table.selectRow': 'Select row',
  'row.open': 'Open',
}
const catalogs = [{ texts, fallbackTexts: texts }]

const onChange = vi.fn()

const widgets: Record<string, ReactElement> = {
  'AxisButton with a text role': <AxisButton role="secondary">Save</AxisButton>,
  'AxisButton with the icon role': <AxisButton role="icon" icon={<DeleteOutlined />} nameKey="row.open" />,
  AxisAlert: <AxisAlert kind="info" message="Saved" />,
  'a paged AxisTable with rows': (
    <AxisTable
      columns={[{ key: 'name', title: 'Name', dataIndex: 'name', kind: 'freeText' }]}
      rowKey={(row: { id: string }) => row.id}
      dataSource={[
        { id: '1', name: 'Buy paper' },
        { id: '2', name: 'Buy pens' },
      ]}
      loading={false}
      pagination={{ current: 1, pageSize: 20, total: 2, pageSizeOptions: [10, 20] }}
      toolbar={{}}
    />
  ),
  EmptyState: <EmptyState text="Nothing here" />,
  'ReferencePicker with plain string options': (
    <ReferencePicker name="kind" value={undefined} required={false} options={['Alpha', 'Beta']} onChange={onChange} />
  ),
  EmptyDash: <EmptyDash />,
  'AxisField without a name': (
    <AxisField label="Name" help="The name of the item">
      <Input />
    </AxisField>
  ),
  AxisFieldGroup: (
    <AxisFieldGroup title="Range" help="The limits">
      <Input />
    </AxisFieldGroup>
  ),
  'AxisCheckboxField with its own value': (
    <AxisCheckboxField label="Enabled" help="Turns the item on" checked={false} onChange={onChange} />
  ),
  AxisFieldRow: (
    <AxisFieldRow title="Range" help="The limits">
      <Input />
      <Input />
    </AxisFieldRow>
  ),
  AxisActionRow: (
    <AxisActionRow>
      <AxisButton role="secondary">Cancel</AxisButton>
    </AxisActionRow>
  ),
  'SectionParts with a part with a heading and one without': (
    <SectionParts>
      <SectionPart testName="named" heading={{ title: 'Named' }}>
        <Input />
      </SectionPart>
      <SectionPart testName="unnamed">
        <Input />
      </SectionPart>
    </SectionParts>
  ),
  'ExpressionField as a plain box': (
    <ExpressionField
      check={null}
      pointer="/expression"
      name="expression"
      value=""
      required={false}
      onChange={onChange}
    />
  ),
  CardPage: <CardPage title="Sign in">Body</CardPage>,
  StepsBar: <StepsBar titles={['Account', 'Team']} current={0} />,
  Announcement: <Announcement>Saved</Announcement>,
}

// Named widgets draw inside a vertical form, where the form gives the space between its fields.
const formWidgets: Record<string, ReactElement> = {
  'AxisField with a name': (
    <AxisField name="title" label="Title" help="The title">
      <Input />
    </AxisField>
  ),
  'AxisChoiceField with a name': (
    <AxisChoiceField
      name="choice"
      label="Choice"
      help="Pick one"
      options={[
        { value: 'a', label: 'Alpha', help: '' },
        { value: 'b', label: 'Beta', help: '' },
      ]}
    />
  ),
  'StartFromField without choices': (
    <StartFromField
      name="start"
      label="Start from"
      help="Pick a start"
      blank="Blank"
      choices={undefined}
      failedMessage=""
    />
  ),
  'AxisCheckboxField with a name': <AxisCheckboxField name="enabled" label="Enabled" help="Turns the item on" />,
  'AcceptTermsField with a name': (
    <AcceptTermsField name="accept" text="I accept" documents={{}} requiredMessage="Tick the box." />
  ),
}

function renderWidget(element: ReactElement) {
  return render(
    <ConfigProvider {...antConfigFor('light')}>
      <TextProvider catalogs={catalogs} development={false}>
        {element}
      </TextProvider>
    </ConfigProvider>,
  )
}

// Ant Design sets its margins as the logical shorthands margin-block and margin-inline. jsdom does not apply a logical
// margin to its physical side, so each side is read as a browser applies it: a margin the root's own style sets wins,
// else the logical margin of the stylesheet, else the physical margin. jsdom reads a margin no rule sets as '' or '0'.
const sides = [
  { side: 'top', logical: 'margin-block' },
  { side: 'right', logical: 'margin-inline' },
  { side: 'bottom', logical: 'margin-block' },
  { side: 'left', logical: 'margin-inline' },
]

// Ant Design sets some margins through a CSS variable, such as var(--ant-form-item-margin-bottom). A variable that the
// root does not define makes the margin 0 in a browser too, so an empty value reads as 0px.
function resolved(value: string, style: CSSStyleDeclaration): string {
  const variable = /^var\((--[\w-]+)\)$/.exec(value)
  return variable ? style.getPropertyValue(variable[1]).trim() : value
}

/** The sides of a root that draw a margin other than 0px, each with its value. An empty list means no space. */
function marginsOtherThanNone(root: HTMLElement): [string, string][] {
  const style = getComputedStyle(root)
  return sides
    .map(({ side, logical }): [string, string] => {
      const physical = `margin-${side}`
      const logicalValue = style.getPropertyValue(logical)
      const value =
        root.style.getPropertyValue(physical) === '' && logicalValue !== ''
          ? logicalValue
          : style.getPropertyValue(physical)
      const margin = resolved(value, style)
      return [side, margin === '' || margin === '0' ? '0px' : margin]
    })
    .filter(([, value]) => value !== '0px')
}

describe('shared widgets keep no space around themselves', () => {
  withAntdStyles()

  it.each(Object.entries(widgets))('draws %s with no margin on its root', (_name, element) => {
    const { container } = renderWidget(element)
    const root = container.firstElementChild
    if (!(root instanceof HTMLElement)) throw new Error('The widget draws no element.')

    expect(marginsOtherThanNone(root)).toEqual([])
  })

  it.each(Object.entries(formWidgets))(
    'draws %s in a vertical form with no margin on its form item',
    (_name, element) => {
      const { container } = renderWidget(<Form layout="vertical">{element}</Form>)
      const root = container.querySelector('form')?.firstElementChild
      if (!(root instanceof HTMLElement)) throw new Error('The form draws no item.')

      expect(marginsOtherThanNone(root)).toEqual([])
    },
  )
})
