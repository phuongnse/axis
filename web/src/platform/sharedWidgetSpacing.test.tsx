import { DeleteOutlined } from '@ant-design/icons'
import { render } from '@testing-library/react'
import { ConfigProvider, Input } from 'antd'
import type { ReactElement } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { ExpressionField } from '../pages/ExpressionField'
import { ReferencePicker } from '../pages/ReferencePicker'
import { ResourceFormSection, ResourceFormSections } from '../pages/ResourceFormSection'
import { withAntdStyles } from '../test/antdStyles'
import { Announcement } from './Announcement'
import { antConfigFor } from './antTheme'
import { AxisActionRow, AxisButton } from './AxisButton'
import { AxisAlert } from './AxisAlert'
import { AxisCheckboxField } from './AxisCheckboxField'
import { AxisField } from './AxisField'
import { AxisFieldGroup } from './AxisFieldGroup'
import { AxisFieldRow } from './AxisFieldRow'
import { AxisTable } from './AxisTable'
import { CardPage } from './CardPage'
import { EmptyDash } from './EmptyValue'
import { EmptyState } from './EmptyState'
import { StepsBar } from './StepsBar'
import { TextProvider } from './TextProvider'

// The shared rule: a shared widget keeps no space around itself, see docs/reference/shared-widgets.md#the-rule.
// AxisField in a form and the form-only fields are a later part of #4103, so they are not drawn here.

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
  'ResourceFormSections with a section with a heading and one without': (
    <ResourceFormSections>
      <ResourceFormSection testName="named" heading={{ title: 'Named' }}>
        <Input />
      </ResourceFormSection>
      <ResourceFormSection testName="unnamed">
        <Input />
      </ResourceFormSection>
    </ResourceFormSections>
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
      return [side, value === '' || value === '0' ? '0px' : value]
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
})
