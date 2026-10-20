import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterAll, afterEach, vi } from 'vitest'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  localStorage.clear()
})

// Components can leave timers pending when they unmount: the ProLayout menu starts a 400 ms one
// and never clears it. Let them fire while jsdom still provides `window`. Switch back to real
// timers first, so a file that leaves fake timers on cannot stall the wait.
afterAll(() => {
  vi.useRealTimers()
  return new Promise((resolve) => setTimeout(resolve, 500))
})

// React Flow observes its nodes and container, and jsdom has no layout to report.
globalThis.ResizeObserver ??= class {
  observe() {}
  unobserve() {}
  disconnect() {}
}

// React Flow reads the zoom from a transform matrix, which jsdom does not parse.
globalThis.DOMMatrixReadOnly ??= class {
  m22: number
  constructor(transform = '') {
    const match = /scale\((.+?)\)/.exec(transform)
    this.m22 = match ? Number(match[1]) : 1
  }
} as unknown as typeof DOMMatrixReadOnly

// jsdom reports every element as 0 wide and tall, so React Flow would measure nothing.
Object.defineProperty(HTMLElement.prototype, 'offsetWidth', {
  configurable: true,
  get() {
    return parseFloat(this.style.width) || 1
  },
})
Object.defineProperty(HTMLElement.prototype, 'offsetHeight', {
  configurable: true,
  get() {
    return parseFloat(this.style.height) || 1
  },
})

// Edge labels measure their text, which jsdom does not lay out.
const svg = SVGElement.prototype as SVGElement & { getBBox?: () => DOMRect }
svg.getBBox ??= () => ({ x: 0, y: 0, width: 0, height: 0 }) as DOMRect

// jsdom does not implement matchMedia, which Ant Design and the theme provider use.
Object.defineProperty(window, 'matchMedia', {
  writable: true,
  value: (query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: () => {},
    removeListener: () => {},
    addEventListener: () => {},
    removeEventListener: () => {},
    dispatchEvent: () => false,
  }),
})

// CodeMirror measures text with Range methods that jsdom does not implement.
if (!Range.prototype.getClientRects) {
  Range.prototype.getClientRects = () => [] as unknown as DOMRectList
}
if (!Range.prototype.getBoundingClientRect) {
  Range.prototype.getBoundingClientRect = () =>
    ({ x: 0, y: 0, width: 0, height: 0, top: 0, right: 0, bottom: 0, left: 0, toJSON: () => ({}) }) as DOMRect
}
