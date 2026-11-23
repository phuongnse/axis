import { vi } from 'vitest'

/**
 * Stubs `matchMedia` as a window of `width` px answers it, for `max-width` queries such as the narrow-width query. The test
 * sets `window.innerWidth` to the same width itself. `vi.unstubAllGlobals()` undoes the stub.
 */
export function stubWindowWidth(width: number) {
  vi.stubGlobal('matchMedia', (query: string) => {
    const max = /max-width: ([\d.]+)px/.exec(query)
    return {
      matches: max !== null && width <= Number(max[1]),
      media: query,
      onchange: null,
      addListener() {},
      removeListener() {},
      addEventListener() {},
      removeEventListener() {},
      dispatchEvent: () => false,
    }
  })
}
