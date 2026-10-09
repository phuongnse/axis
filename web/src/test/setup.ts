import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterAll, afterEach, vi } from 'vitest'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  localStorage.clear()
})

// Components can leave timers pending when they unmount: the ProLayout menu starts a 400 ms one
// and never clears it. Let them fire while jsdom still provides `window`.
afterAll(() => new Promise((resolve) => setTimeout(resolve, 500)))

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
