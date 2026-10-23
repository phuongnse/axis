import type { ContainerWidgetMetadata, PageWidgetMetadata, TabsWidgetMetadata, WidgetMetadata } from './site'

/** Whether a page widget is a container that holds other widgets. */
export function isContainer(widget: PageWidgetMetadata): widget is ContainerWidgetMetadata {
  return widget.type === 'columns' || widget.type === 'tabs' || widget.type === 'section'
}

/** The widgets directly inside a container, in file order. A tabs widget lists every tab's widgets. */
function childrenOf(container: ContainerWidgetMetadata): PageWidgetMetadata[] {
  switch (container.type) {
    case 'columns':
      return container.columns.flatMap((column) => column.widgets)
    case 'tabs':
      return container.tabs.flatMap((tab) => tab.widgets)
    case 'section':
      return container.widgets
  }
}

/** Every widget that shows something, depth first in document order. Containers are walked, not returned. */
export function leafWidgets(widgets: readonly PageWidgetMetadata[]): WidgetMetadata[] {
  return widgets.flatMap((widget) => (isContainer(widget) ? leafWidgets(childrenOf(widget)) : [widget]))
}

/**
 * The query parameter that keeps the selected tab of each tabs widget. The first tabs widget in document order
 * uses `tab`, the next `tab2`, then `tab3`. Tabs widgets inside a tab count whether the tab is selected or not.
 */
export function tabParameters(widgets: readonly PageWidgetMetadata[]): Map<TabsWidgetMetadata, string> {
  const names = new Map<TabsWidgetMetadata, string>()
  function walk(items: readonly PageWidgetMetadata[]) {
    for (const widget of items) {
      if (!isContainer(widget)) {
        continue
      }
      if (widget.type === 'tabs') {
        names.set(widget, names.size === 0 ? 'tab' : `tab${names.size + 1}`)
      }
      walk(childrenOf(widget))
    }
  }
  walk(widgets)
  return names
}
