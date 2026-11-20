import { vi } from 'vitest'

/**
 * Stubs ResizeObserver so that it reports each node of the graph it observes once, as a browser does. React Flow then
 * measures its nodes, fits the view to them, and applies the moves it was asked for. The setup's stub reports nothing.
 */
export function stubResizeObserver() {
  vi.stubGlobal(
    'ResizeObserver',
    class {
      callback: ResizeObserverCallback
      constructor(callback: ResizeObserverCallback) {
        this.callback = callback
      }
      observe(target: Element) {
        if (target.classList.contains('react-flow__node')) {
          this.callback([{ target } as ResizeObserverEntry], this as unknown as ResizeObserver)
        }
      }
      unobserve() {}
      disconnect() {}
    },
  )
}
