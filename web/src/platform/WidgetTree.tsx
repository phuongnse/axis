import { Col, Collapse, Flex, Row, Tabs } from 'antd'
import { createContext, useContext, useMemo, type ReactNode } from 'react'
import { useSearchParams } from 'react-router'
import {
  isPlaceholder,
  type ColumnsWidgetMetadata,
  type PageWidgetMetadata,
  type PlaceholderWidgetMetadata,
  type SectionWidgetMetadata,
  type TabsWidgetMetadata,
  type WidgetMetadata,
} from './site'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'
import { tabParameters } from './widgetTree'

type RenderLeaf = (widget: WidgetMetadata) => ReactNode
type RenderPlaceholder = (widget: PlaceholderWidgetMetadata) => ReactNode

// The query parameter of each tabs widget of the page, worked out once at the root.
const TabParameters = createContext<Map<TabsWidgetMetadata, string>>(new Map())

// How the page designer's canvas draws a widget that does not compile. The site has none, so it draws nothing.
const PlaceholderRenderer = createContext<RenderPlaceholder | undefined>(undefined)

/**
 * Draws a page's widget tree. A widget that shows something is drawn by `renderLeaf`, and a container draws its
 * widgets in file order. Each leaf keeps its own loading, empty and error states. A placeholder, which only a draft
 * page has, is drawn by `renderPlaceholder`, or not at all without it.
 */
export function WidgetTree({
  widgets,
  renderLeaf,
  renderPlaceholder,
}: {
  widgets: PageWidgetMetadata[]
  renderLeaf: RenderLeaf
  renderPlaceholder?: RenderPlaceholder
}) {
  const parameters = useMemo(() => tabParameters(widgets), [widgets])
  return (
    <TabParameters.Provider value={parameters}>
      <PlaceholderRenderer.Provider value={renderPlaceholder}>
        <WidgetList widgets={widgets} renderLeaf={renderLeaf} />
      </PlaceholderRenderer.Provider>
    </TabParameters.Provider>
  )
}

function WidgetList({ widgets, renderLeaf }: { widgets: readonly PageWidgetMetadata[]; renderLeaf: RenderLeaf }) {
  return (
    <Flex vertical gap="middle">
      {widgets.map((widget, index) => (
        <Node key={index} widget={widget} renderLeaf={renderLeaf} />
      ))}
    </Flex>
  )
}

function Node({ widget, renderLeaf }: { widget: PageWidgetMetadata; renderLeaf: RenderLeaf }) {
  const renderPlaceholder = useContext(PlaceholderRenderer)
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

// Ant Design's grid has 24 parts and an Axis column width is in twelfths. Below `md`, 768 px, the columns stack.
// A widget wider than its column, such as a table with many fields, scrolls inside the column instead of
// running under the next one.
function Columns({ widget, renderLeaf }: { widget: ColumnsWidgetMetadata; renderLeaf: RenderLeaf }) {
  const { spacing } = useDesignTokens()
  return (
    <Row gutter={[spacing.md, spacing.md]}>
      {widget.columns.map((column, index) => (
        <Col key={index} xs={24} md={column.width * 2} style={{ overflowX: 'auto' }} data-testid="column">
          <WidgetList widgets={column.widgets} renderLeaf={renderLeaf} />
        </Col>
      ))}
    </Row>
  )
}

function TabsNode({ widget, renderLeaf }: { widget: TabsWidgetMetadata; renderLeaf: RenderLeaf }) {
  const t = useText()
  const parameters = useContext(TabParameters)
  const [searchParams, setSearchParams] = useSearchParams()
  const name = parameters.get(widget) ?? 'tab'
  // A value that is not a position of a tab selects the first tab and leaves the address as it is.
  const raw = searchParams.get(name)
  const position = raw !== null && /^\d+$/.test(raw) ? Number(raw) : 0
  const selected = position >= 1 && position <= widget.tabs.length ? position : 1

  function select(key: string) {
    const next = new URLSearchParams(searchParams)
    if (key === '1') {
      next.delete(name)
    } else {
      next.set(name, key)
    }
    setSearchParams(next)
  }

  return (
    <Tabs
      activeKey={String(selected)}
      onChange={select}
      items={widget.tabs.map((tab, index) => ({
        key: String(index + 1),
        label: t(tab.labelKey),
        children: <WidgetList widgets={tab.widgets} renderLeaf={renderLeaf} />,
      }))}
    />
  )
}

function Section({ widget, renderLeaf }: { widget: SectionWidgetMetadata; renderLeaf: RenderLeaf }) {
  const t = useText()
  const title = t(widget.titleKey)
  return (
    <div role="region" aria-label={title}>
      <Collapse
        defaultActiveKey={widget.collapsed ? [] : ['section']}
        items={[
          {
            key: 'section',
            label: title,
            children: <WidgetList widgets={widget.widgets} renderLeaf={renderLeaf} />,
          },
        ]}
      />
    </div>
  )
}
