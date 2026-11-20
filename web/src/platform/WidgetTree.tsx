import { Tabs } from 'antd'
import { createContext, useContext, useMemo, useState, type ReactNode } from 'react'
import { ColumnGrid, ColumnGridCell } from './ColumnGrid'
import {
  isPlaceholder,
  type ColumnsWidgetMetadata,
  type PageWidgetMetadata,
  type PlaceholderWidgetMetadata,
  type SectionWidgetMetadata,
  type TabsWidgetMetadata,
  type WidgetMetadata,
} from './site'
import { PartBoundary, SectionPart, SectionParts } from './SectionParts'
import { useText } from './texts'
import { useAddressTab } from './useAddressTab'
import { tabParameters } from './widgetTree'

type RenderLeaf = (widget: WidgetMetadata) => ReactNode
type RenderPlaceholder = (widget: PlaceholderWidgetMetadata) => ReactNode
type FrameWidget = (widget: PageWidgetMetadata | PlaceholderWidgetMetadata, drawn: ReactNode) => ReactNode
type FrameList = (pointer: string, drawn: ReactNode) => ReactNode

// The query parameter of each tabs widget of the page, worked out once at the root.
const TabParameters = createContext<Map<TabsWidgetMetadata, string>>(new Map())

// How the page designer's canvas draws a widget that does not compile. The site has none, so it draws nothing.
const PlaceholderRenderer = createContext<RenderPlaceholder | undefined>(undefined)

// Wraps what each widget draws, such as the page designer's marks. The site has none, so it wraps nothing.
const WidgetFrame = createContext<FrameWidget | undefined>(undefined)

// Wraps what each widget list draws, by the list's JSON Pointer in the draft. The site has none, so it wraps nothing.
const ListFrame = createContext<FrameList | undefined>(undefined)

/**
 * Draws a page's widget tree. A widget that shows something is drawn by `renderLeaf`, and a container draws its
 * widgets in file order. Each leaf keeps its own loading, empty and error states. A placeholder, which only a draft
 * page has, is drawn by `renderPlaceholder`, or not at all without it. `frameWidget` wraps what each widget draws, and
 * `frameList` what each widget list draws, by its pointer in the draft: `/widgets` for the page, then `…/widgets` for a
 * section, `…/columns/1/widgets` for a column and `…/tabs/1/widgets` for a tab.
 */
export function WidgetTree({
  widgets,
  renderLeaf,
  renderPlaceholder,
  frameWidget,
  frameList,
}: {
  widgets: PageWidgetMetadata[]
  renderLeaf: RenderLeaf
  renderPlaceholder?: RenderPlaceholder
  frameWidget?: FrameWidget
  frameList?: FrameList
}) {
  const parameters = useMemo(() => tabParameters(widgets), [widgets])
  return (
    <TabParameters.Provider value={parameters}>
      <PlaceholderRenderer.Provider value={renderPlaceholder}>
        <WidgetFrame.Provider value={frameWidget}>
          <ListFrame.Provider value={frameList}>
            <WidgetList widgets={widgets} renderLeaf={renderLeaf} pointer="/widgets" />
          </ListFrame.Provider>
        </WidgetFrame.Provider>
      </PlaceholderRenderer.Provider>
    </TabParameters.Provider>
  )
}

function WidgetList({
  widgets,
  renderLeaf,
  pointer,
}: {
  widgets: readonly PageWidgetMetadata[]
  renderLeaf: RenderLeaf
  /** The list's JSON Pointer in the draft, when it is known. */
  pointer?: string
}) {
  const frameList = useContext(ListFrame)
  const drawn = (
    // A boundary, so a section that comes first in this list draws no rule, even when the list follows another widget.
    <PartBoundary>
      <SectionParts>
        {widgets.map((widget, index) => (
          <Node key={index} widget={widget} renderLeaf={renderLeaf} />
        ))}
      </SectionParts>
    </PartBoundary>
  )
  return frameList && pointer !== undefined ? frameList(pointer, drawn) : drawn
}

/** The pointer of a list inside a widget: the widget's own path and the list's place in it, or none without a path. */
function listPointer(widget: { path?: string }, place: string): string | undefined {
  return widget.path === undefined ? undefined : `${widget.path}/${place}`
}

function Node({ widget, renderLeaf }: { widget: PageWidgetMetadata; renderLeaf: RenderLeaf }) {
  const renderPlaceholder = useContext(PlaceholderRenderer)
  const frameWidget = useContext(WidgetFrame)
  const drawn = drawWidget(widget, renderLeaf, renderPlaceholder)
  return frameWidget ? frameWidget(widget, drawn) : drawn
}

function drawWidget(
  widget: PageWidgetMetadata,
  renderLeaf: RenderLeaf,
  renderPlaceholder: RenderPlaceholder | undefined,
) {
  if (isPlaceholder(widget)) {
    return renderPlaceholder?.(widget) ?? null
  }
  switch (widget.type) {
    case 'columns':
      return <Columns widget={widget} renderLeaf={renderLeaf} />
    case 'tabs':
      return <TabsNode widget={widget} renderLeaf={renderLeaf} />
    case 'section':
      return <Section widget={widget} renderLeaf={renderLeaf} />
    default:
      return renderLeaf(widget)
  }
}

// The columns sit on the shared grid of twelve columns, and an Axis column width is in twelfths. Below narrow width,
// 992 px, the columns stack. The grid's cells decide through `useNarrowWidth`, so a page designer can force the stack.
// A widget wider than its column, such as a table with many fields, scrolls inside the column instead of
// running under the next one.
function Columns({ widget, renderLeaf }: { widget: ColumnsWidgetMetadata; renderLeaf: RenderLeaf }) {
  return (
    <ColumnGrid>
      {widget.columns.map((column, index) => (
        <ColumnGridCell key={index} width={column.width} style={{ overflowX: 'auto' }} data-testid="column">
          <WidgetList
            widgets={column.widgets}
            renderLeaf={renderLeaf}
            pointer={listPointer(widget, `columns/${index}/widgets`)}
          />
        </ColumnGridCell>
      ))}
    </ColumnGrid>
  )
}

function TabsNode({ widget, renderLeaf }: { widget: TabsWidgetMetadata; renderLeaf: RenderLeaf }) {
  const t = useText()
  const parameters = useContext(TabParameters)
  const name = parameters.get(widget) ?? 'tab'
  const keys = useMemo(() => widget.tabs.map((_, index) => String(index + 1)), [widget.tabs])
  const [selected, select] = useAddressTab(name, keys)

  return (
    <Tabs
      activeKey={selected}
      onChange={select}
      items={widget.tabs.map((tab, index) => ({
        key: String(index + 1),
        label: t(tab.labelKey),
        children: (
          <WidgetList
            widgets={tab.widgets}
            renderLeaf={renderLeaf}
            pointer={listPointer(widget, `tabs/${index}/widgets`)}
          />
        ),
      }))}
    />
  )
}

function Section({ widget, renderLeaf }: { widget: SectionWidgetMetadata; renderLeaf: RenderLeaf }) {
  const t = useText()
  const [open, setOpen] = useState(!widget.collapsed)
  const title = t(widget.titleKey)
  return (
    <SectionPart
      testId="section-widget"
      heading={{ title }}
      fold={widget.collapsed ? { open, onToggle: () => setOpen((current) => !current), title } : undefined}
    >
      <WidgetList widgets={widget.widgets} renderLeaf={renderLeaf} pointer={listPointer(widget, 'widgets')} />
    </SectionPart>
  )
}
