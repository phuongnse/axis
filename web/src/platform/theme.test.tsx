import { render, screen, waitFor } from '@testing-library/react'
import { Modal, theme } from 'antd'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ThemeModeProvider } from './theme'
import { useThemeMode } from './themeMode'
import { reducedMotionQuery } from './useReducedMotion'

function Mode() {
  return <output>{useThemeMode().mode}</output>
}

describe('ThemeModeProvider', () => {
  it('starts in the stored mode', () => {
    localStorage.setItem('axis.themeMode', 'dark')

    render(
      <ThemeModeProvider>
        <Mode />
      </ThemeModeProvider>,
    )

    expect(screen.getByRole('status')).toHaveTextContent('dark')
  })

  it('starts in the given mode instead of the stored one', () => {
    localStorage.setItem('axis.themeMode', 'dark')

    render(
      <ThemeModeProvider initialMode="light">
        <Mode />
      </ThemeModeProvider>,
    )

    expect(screen.getByRole('status')).toHaveTextContent('light')
  })
})

function Motion() {
  return <output>{String(theme.useToken().token.motion)}</output>
}

/** Stubs matchMedia, so that only the reduced-motion query matches when `reduce` is set. */
function stubReducedMotion(reduce: boolean) {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: reduce && query === reducedMotionQuery,
    media: query,
    onchange: null,
    addListener() {},
    removeListener() {},
    addEventListener() {},
    removeEventListener() {},
    dispatchEvent: () => false,
  }))
}

describe('ThemeModeProvider and reduced motion', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('turns motion off when the browser asks for reduced motion', () => {
    stubReducedMotion(true)

    render(
      <ThemeModeProvider>
        <Motion />
      </ThemeModeProvider>,
    )

    expect(screen.getByRole('status')).toHaveTextContent('false')
  })

  it('keeps motion on without that preference', () => {
    stubReducedMotion(false)

    render(
      <ThemeModeProvider>
        <Motion />
      </ThemeModeProvider>,
    )

    expect(screen.getByRole('status')).toHaveTextContent('true')
  })

  const motionState = /-(appear|enter|leave)\b/

  function motionClasses() {
    const classes: string[] = []
    for (let node: Element | null = screen.getByRole('dialog'); node; node = node.parentElement) {
      classes.push(...Array.from(node.classList).filter((name) => motionState.test(name)))
    }
    return classes
  }

  // Ant Design's modal still passes through its prepare step on the first render, so the state classes are there at
  // first. With motion off the step ends at once, and with motion on it waits for an animation that jsdom never ends.
  it('leaves no motion state on an opened modal under reduced motion', async () => {
    stubReducedMotion(true)

    render(
      <ThemeModeProvider>
        <Modal open title="Pick">
          x
        </Modal>
      </ThemeModeProvider>,
    )

    await waitFor(() => expect(motionClasses()).toEqual([]))
  })

  it('keeps the motion state on an opened modal without that preference', async () => {
    stubReducedMotion(false)

    render(
      <ThemeModeProvider>
        <Modal open title="Pick">
          x
        </Modal>
      </ThemeModeProvider>,
    )

    await new Promise((resolve) => setTimeout(resolve, 200))
    expect(motionClasses()).not.toEqual([])
  })
})
