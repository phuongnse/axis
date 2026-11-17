import { renderHook } from '@testing-library/react'
import type { ReactNode } from 'react'
import { describe, expect, it } from 'vitest'
import { CanvasActionsContext } from './canvasActions'
import { useSiteTableKey } from './tableChoices'

describe('useSiteTableKey', () => {
  it('gives a site page the key of its site and page in lower case', () => {
    expect(renderHook(() => useSiteTableKey('sample', 'Tasks')).result.current).toBe('site.sample.tasks')
  })

  it('writes the slash of a module page as a dot', () => {
    expect(renderHook(() => useSiteTableKey('sample', 'ops/Tasks')).result.current).toBe('site.sample.ops.tasks')
  })

  it('gives no key on the page designer canvas', () => {
    const wrapper = ({ children }: { children: ReactNode }) => (
      <CanvasActionsContext.Provider value={{ selected: undefined }}>{children}</CanvasActionsContext.Provider>
    )

    expect(renderHook(() => useSiteTableKey('sample', 'Tasks'), { wrapper }).result.current).toBeUndefined()
  })
})
