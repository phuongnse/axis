import { renderHook } from '@testing-library/react'
import { createElement, type ReactNode } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { breakpointTokens } from './designTokens'
import { NarrowWidthOverride, useNarrowWidth } from './useNarrowWidth'

function stubWindowWidth(width: number) {
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

describe('useNarrowWidth', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('reads the narrow-width token, 992 px', () => {
    expect(breakpointTokens.narrowWidth).toBe(992)
  })

  it('is narrow at 991 px and not at the token value, 992 px', () => {
    stubWindowWidth(breakpointTokens.narrowWidth - 1)
    const below = renderHook(() => useNarrowWidth())
    expect(below.result.current).toBe(true)
    below.unmount()

    stubWindowWidth(breakpointTokens.narrowWidth)
    expect(renderHook(() => useNarrowWidth()).result.current).toBe(false)
  })

  it('takes the page designer override over the window width', () => {
    stubWindowWidth(1280)
    const override = ({ children }: { children: ReactNode }) =>
      createElement(NarrowWidthOverride.Provider, { value: true }, children)
    const { result } = renderHook(() => useNarrowWidth(), { wrapper: override })

    expect(result.current).toBe(true)
  })
})
