import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterAll, afterEach, expect, vi } from 'vitest'
import { resetSignInNeeded } from '../platform/signInNeeded'
import { recordAntdDeprecation, takeAntdDeprecations } from './antdDeprecations'

// Ant Design 6 keeps deprecated props in its types, so only its runtime warning shows them. Record it to fail the test.
const consoleError = console.error.bind(console)
console.error = (...args: unknown[]) => {
  recordAntdDeprecation(args)
  consoleError(...args)
}

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  localStorage.clear()
  resetSignInNeeded()
  expect(takeAntdDeprecations(), 'Ant Design deprecation warnings').toEqual([])
})

// Components can leave timers pending when they unmount: the Ant Design menu starts a 400 ms one
// and never clears it. Let them fire while jsdom still provides `window`. Switch back to real
// timers first, so a file that leaves fake timers on cannot stall the wait.
afterAll(() => {
  vi.useRealTimers()
  return new Promise((resolve) => setTimeout(resolve, 500))
})

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

// jsdom has no ResizeObserver, which React Flow uses. The callback never fires, so nothing re-measures.
if (!('ResizeObserver' in globalThis)) {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  } as unknown as typeof ResizeObserver
}

// jsdom has no DOMMatrixReadOnly, which React Flow reads the zoom from. Parse scale(n) and default to 1.
if (!('DOMMatrixReadOnly' in globalThis)) {
  globalThis.DOMMatrixReadOnly = class {
    m22: number
    constructor(transform?: string) {
      const scale = transform?.match(/scale\(([\d.]+)\)/)?.[1]
      this.m22 = scale === undefined ? 1 : Number(scale)
    }
  } as unknown as typeof DOMMatrixReadOnly
}

// React Flow needs a size for its container and nodes. jsdom reports 0, so give one inside .react-flow only.
for (const [property, style] of [
  ['offsetWidth', 'width'],
  ['offsetHeight', 'height'],
] as const) {
  const original = Object.getOwnPropertyDescriptor(HTMLElement.prototype, property)
  Object.defineProperty(HTMLElement.prototype, property, {
    configurable: true,
    get(this: HTMLElement) {
      if (!this.closest('.react-flow')) return original?.get?.call(this) ?? 0
      return parseFloat(this.style[style]) || 1
    },
  })
}

// React Flow calls getBBox to measure edge labels. jsdom does not implement it.
const svgPrototype = SVGElement.prototype as SVGElement & { getBBox?: () => DOMRect }
if (!svgPrototype.getBBox) {
  svgPrototype.getBBox = () => ({ x: 0, y: 0, width: 0, height: 0 }) as DOMRect
}

// ECharts measures text on a canvas. jsdom has none, so return no context and let it estimate the width.
HTMLCanvasElement.prototype.getContext = (() => null) as unknown as HTMLCanvasElement['getContext']
